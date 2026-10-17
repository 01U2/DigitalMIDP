using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.Exceptions;
using DigitalMIDP.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace DigitalMIDP.Application.UseCases;

/// <summary>
/// Concrete implementation of <see cref="ICreateDeliverableUseCase"/>.
/// </summary>
public sealed class CreateDeliverableUseCase : ICreateDeliverableUseCase
{
    private readonly ICdeStructureUseCase _cdeStructureUseCase;
    private readonly IDocumentNumberUseCase _documentNumberUseCase;
    private readonly IDataManagementService _dataManagementService;
    private readonly ILogger<CreateDeliverableUseCase> _logger;

    public CreateDeliverableUseCase(
        ICdeStructureUseCase cdeStructureUseCase,
        IDocumentNumberUseCase documentNumberUseCase,
        IDataManagementService dataManagementService,
        ILogger<CreateDeliverableUseCase> logger)
    {
        ArgumentNullException.ThrowIfNull(cdeStructureUseCase);
        ArgumentNullException.ThrowIfNull(documentNumberUseCase);
        ArgumentNullException.ThrowIfNull(dataManagementService);
        ArgumentNullException.ThrowIfNull(logger);

        _cdeStructureUseCase = cdeStructureUseCase;
        _documentNumberUseCase = documentNumberUseCase;
        _dataManagementService = dataManagementService;
        _logger = logger;
    }

    public Task<CreateDeliverableResultDto> CreateAsync(
        CreateDeliverableRequestDto request,
        CancellationToken cancellationToken = default) =>
        CreateAsync(request, reservedNumbers: [], cancellationToken);

    /// <summary>
    /// Creates one deliverable, treating <paramref name="reservedNumbers"/> as already taken.
    ///
    /// <para>
    /// The reserved list is how a batch stays correct without waiting for ACC. Each item's number is
    /// allocated by scanning the destination folder, and a file created seconds ago is not reliably
    /// visible to that scan yet - the item is created through the Data Management API and indexed by
    /// the Docs API a moment later. Passing the numbers already issued in this batch means the next
    /// one steps past them regardless of how fast ACC catches up.
    /// </para>
    /// </summary>
    private async Task<CreateDeliverableResultDto> CreateAsync(
        CreateDeliverableRequestDto request,
        IReadOnlyCollection<string> reservedNumbers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogInformation(
            "Creating deliverable in project {ProjectId} for discipline '{Discipline}' from template '{Template}'",
            request.ProjectId, request.DisciplineCode, request.Template?.Name ?? "(none)");

        try
        {
            // Routed through the cache: the wizard already resolved this same convention moments ago
            // to build its input form, and re-fetching it here would repeat several HTTP round trips
            // for data that has not changed.
            var convention = await _cdeStructureUseCase.GetNamingConventionAsync(
                request.HubId, request.ProjectId, cancellationToken);

            if (!convention.IsConfigured)
            {
                return CreateDeliverableResultDto.Failed("No naming standard is configured for this project.");
            }

            var structure = await _cdeStructureUseCase.GetStructureAsync(
                request.HubId, request.ProjectId, request.AccessToken, cancellationToken);

            if (structure.Wip is null)
            {
                return CreateDeliverableResultDto.Failed("No WIP folder was found in this project.");
            }

            var discipline = structure.FindDiscipline(request.DisciplineCode);
            if (discipline is null)
            {
                return CreateDeliverableResultDto.Failed($"'{request.DisciplineCode}' is not a known discipline.");
            }

            var warnings = new List<string>();

            // The destination folder is created on demand rather than treated as a precondition, so a
            // discipline that has never been used before does not block the first deliverable in it.
            var destinationFolder = discipline.Folder;
            if (destinationFolder is null)
            {
                _logger.LogInformation(
                    "Creating missing WIP folder '{FolderName}' for discipline '{Code}'",
                    discipline.ExpectedFolderName, discipline.Code);

                destinationFolder = await _dataManagementService.CreateFolderAsync(
                    request.ProjectId, structure.Wip.Id, discipline.ExpectedFolderName, cancellationToken);

                // The cached structure still says this folder is missing, which would make the next
                // deliverable in the same discipline try to create it a second time.
                _cdeStructureUseCase.Invalidate(request.ProjectId);

                // Worded to avoid repeating "Created" right after the headline "Created {file} in
                // WIP/{folder}." that always precedes this in the UI.
                warnings.Add($"New folder: \"{discipline.ExpectedFolderName}\".");
                discipline = discipline with { Folder = destinationFolder };
            }

            // One scan, allocating and verifying together. This used to be AllocateNextAsync
            // followed by CheckAvailabilityAsync, each of which listed the discipline folder and
            // batch-read every file's attributes - back to back, with nothing in between, so the
            // second scan could only ever see what the first had just seen. Halving that is the
            // other half of why a creation no longer takes half a minute (the first half being that
            // neither scan now fetches per-file path metadata it does not read).
            var allocation = await _documentNumberUseCase.AllocateAndVerifyNextAsync(
                request.ProjectId, discipline, convention, request.FieldValues, reservedNumbers,
                request.AccessToken, cancellationToken);

            var documentNumber = allocation.Number;

            if (!allocation.IsUsable)
            {
                var availability = allocation.Availability;

                _logger.LogWarning(
                    "Refusing to create {Number}: already used by '{ConflictingFile}'",
                    documentNumber.Value, availability.ConflictingFileName);

                return CreateDeliverableResultDto.Failed(
                    $"Document number '{documentNumber.Value}' is already used by '{availability.ConflictingFileName}' " +
                    $"in {discipline.ExpectedFolderName}.",
                    availability.SuggestedNumber);
            }

            var extension = request.Template?.Extension ?? string.Empty;
            var fileName = $"{documentNumber.Value}{extension}";

            var createdItem = request.Template is not null
                ? await _dataManagementService.CreateItemFromTemplateAsync(
                    request.ProjectId, destinationFolder.Id, request.Template.ItemId, fileName, cancellationToken)
                : await _dataManagementService.CreateItemAsync(
                    request.ProjectId, destinationFolder.Id, fileName, [], cancellationToken);

            createdItem = await DescribeLocationAsync(
                request, structure, destinationFolder, createdItem, cancellationToken);

            var attributesApplied = await ApplyAttributesAsync(
                request, convention, createdItem, warnings, cancellationToken);

            _logger.LogInformation(
                "Created deliverable '{FileName}' ({Number}) in '{Folder}'; attributes applied: {Applied}",
                fileName, documentNumber.Value, destinationFolder.Name, attributesApplied);

            return new CreateDeliverableResultDto(
                Success: true,
                CreatedItem: createdItem,
                DocumentNumber: documentNumber.Value,
                FileName: fileName,
                DestinationFolder: destinationFolder.Name,
                AttributesApplied: attributesApplied,
                Warnings: warnings,
                ErrorMessage: null);
        }
        catch (PermissionDeniedException ex)
        {
            _logger.LogWarning(ex, "Permission denied creating deliverable in project {ProjectId}", request.ProjectId);
            return CreateDeliverableResultDto.Failed(ex.Message);
        }
        catch (AttributeValidationException ex)
        {
            _logger.LogWarning(ex, "Validation error creating deliverable in project {ProjectId}", request.ProjectId);
            return CreateDeliverableResultDto.Failed(ex.Message);
        }
        catch (Exceptions.ApplicationException ex)
        {
            _logger.LogError(ex, "Failed to create deliverable in project {ProjectId}", request.ProjectId);
            return CreateDeliverableResultDto.Failed(ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unexpected error creating deliverable in project {ProjectId}", request.ProjectId);
            return CreateDeliverableResultDto.Failed($"Unexpected error creating the deliverable: {ex.Message}");
        }
    }

    public async Task<CreateDeliverableBatchResultDto> CreateManyAsync(
        CreateDeliverableBatchRequestDto request,
        IProgress<DeliverableBatchProgressDto>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.PerItemTitleValues.Count == 0)
        {
            return new CreateDeliverableBatchResultDto([]);
        }

        var total = request.PerItemTitleValues.Count;

        _logger.LogInformation(
            "Creating {Count} deliverable(s) in project {ProjectId} for discipline '{Discipline}' from template '{Template}'",
            total, request.ProjectId, request.DisciplineCode, request.Template?.Name ?? "(none)");

        var results = new List<CreateDeliverableResultDto>(total);

        // Every number this batch has successfully issued. Carried into each subsequent allocation so
        // the sequence advances even when ACC has not yet made the previous file visible to a scan.
        var reservedNumbers = new List<string>(total);

        // Deliberately a plain sequential loop, not Task.WhenAll. Each item's number is allocated by
        // scanning the destination folder's current contents (see DocumentNumberUseCase), so the next
        // item must not start until the previous one has actually landed in ACC (or definitely
        // failed to). Running these concurrently would have every item scan the same "before" state
        // and could allocate the same next number to more than one of them.
        //
        // Cancellation is checked only between items, never inside CreateAsync itself, so a document
        // already being written always finishes cleanly rather than being abandoned half-created.
        foreach (var titleValues in request.PerItemTitleValues)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation(
                    "Batch creation for project {ProjectId} cancelled after {Completed} of {Total} items",
                    request.ProjectId, results.Count, total);
                break;
            }

            var mergedMetadata = new Dictionary<int, string?>(request.SharedMetadataValues);
            foreach (var (attributeId, value) in titleValues)
            {
                mergedMetadata[attributeId] = value;
            }

            var itemRequest = new CreateDeliverableRequestDto(
                HubId: request.HubId,
                ProjectId: request.ProjectId,
                AccessToken: request.AccessToken,
                Template: request.Template,
                DisciplineCode: request.DisciplineCode,
                FieldValues: request.FieldValues,
                MetadataValues: mergedMetadata);

            // CancellationToken.None: a cancellation mid-item would leave a partially created file
            // with no attributes and nothing recorded about it. The loop's own check above is where
            // cancellation takes effect - between items, never inside one.
            var itemResult = await CreateAsync(itemRequest, reservedNumbers, CancellationToken.None);
            results.Add(itemResult);

            if (itemResult.Success && !string.IsNullOrWhiteSpace(itemResult.DocumentNumber))
            {
                reservedNumbers.Add(itemResult.DocumentNumber!);
            }

            progress?.Report(new DeliverableBatchProgressDto(results.Count, total, itemResult));
        }

        _logger.LogInformation(
            "Batch creation finished for project {ProjectId}: {SuccessCount} of {TotalCount} succeeded ({Completed} attempted)",
            request.ProjectId, results.Count(r => r.Success), total, results.Count);

        return new CreateDeliverableBatchResultDto(results);
    }

    /// <summary>
    /// Fills in the created item's location and lifecycle stage, the two fields the files grid
    /// derives from where a file sits rather than reading from ACC.
    ///
    /// <para>
    /// Without this the grid would show a new row with an empty Location column and no stage, which
    /// matters now that a created file is added to the grid directly instead of appearing via a full
    /// project reload (see <c>FilesViewModel.AddCreatedItemsAsync</c>) - the reload used to derive
    /// both while walking the folder tree.
    /// </para>
    ///
    /// <para>
    /// The path is composed exactly as <see cref="FileListingUseCase"/> composes it during that walk
    /// - root folder name, then each folder descended into - so a created row sorts, filters and
    /// classifies identically to one that was listed. The root folder lookup is a cached call (see
    /// <c>DataManagementService.GetRootFolderAsync</c>), so this costs no extra round trip in
    /// practice. A failure to resolve it is not worth failing a successfully created file over: the
    /// row simply arrives without a location and the next refresh fills it in.
    /// </para>
    /// </summary>
    private async Task<ItemDto> DescribeLocationAsync(
        CreateDeliverableRequestDto request,
        CdeStructureDto structure,
        FolderDto destinationFolder,
        ItemDto createdItem,
        CancellationToken cancellationToken)
    {
        try
        {
            var rootFolder = await _dataManagementService.GetRootFolderAsync(
                request.HubId, request.ProjectId, request.AccessToken, cancellationToken);

            var segments = new[] { rootFolder.Name, structure.Wip?.Name, destinationFolder.Name }
                .Where(segment => !string.IsNullOrWhiteSpace(segment));

            var location = string.Join("/", segments);

            return createdItem with
            {
                Location = location,
                LifecycleStage = FolderNameMatcher.ClassifyLifecycleStage(location)
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex, "Could not determine the project-relative location of created file '{FileName}'", createdItem.Name);

            return createdItem;
        }
    }

    /// <summary>
    /// Writes the document number's own segments plus the required metadata onto the new file.
    ///
    /// A failure here is reported but does not undo the file: ACC offers no transactional create, so
    /// deleting a successfully created file to "roll back" a metadata failure would risk destroying
    /// content the user can otherwise fix by editing attributes. The result carries
    /// <c>AttributesApplied = false</c> instead, so the UI can tell the user exactly what is missing.
    /// </summary>
    private async Task<bool> ApplyAttributesAsync(
        CreateDeliverableRequestDto request,
        NamingConventionDto convention,
        ItemDto createdItem,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(createdItem.TipVersionUrn))
        {
            warnings.Add("Attributes couldn't be set - use Edit Attributes to add them.");
            return false;
        }

        var updatesByAttributeId = new Dictionary<int, CustomAttributeUpdateRequestDto>();

        // The document number's own segments are deliberately NOT written here.
        //
        // ACC owns those attributes: it parses the filename against the project's naming standard and
        // populates them itself as part of creating the version - they come back already set in the
        // create-item response. Attempting to write them anyway is rejected with "Some items in the
        // payload is invalid", because a naming-standard-derived attribute is not directly settable.
        //
        // This costs nothing. Duplicate detection reads these values back from ACC, and ACC has
        // already put the right ones there; CollectExistingNumbersAsync also falls back to parsing the
        // filename for any file where they are missing.
        foreach (var (attributeId, value) in request.MetadataValues.Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value)))
        {
            updatesByAttributeId[attributeId] = new CustomAttributeUpdateRequestDto(attributeId, value);
        }

        var updates = updatesByAttributeId.Values.ToList();

        if (updates.Count == 0)
        {
            return true;
        }

        _logger.LogInformation(
            "Writing {Count} attribute(s) to version {VersionUrn}",
            updates.Count, createdItem.TipVersionUrn);

        try
        {
            await WriteAttributesWithRetryAsync(
                request.ProjectId, createdItem.TipVersionUrn!, updates, cancellationToken);

            return true;
        }
        catch (AttributeValidationException ex)
        {
            // The batch endpoint is all-or-nothing: a single value ACC dislikes discards every other
            // value in the request. Retry one at a time so the acceptable attributes are still saved,
            // and so the user is told exactly which field is the problem instead of losing all of
            // them to an error that names none.
            _logger.LogWarning(
                ex, "Batch attribute write rejected for '{FileName}'; retrying individually", createdItem.Name);

            var failures = await WriteAttributesIndividuallyAsync(
                request.ProjectId, createdItem.TipVersionUrn!, updates, cancellationToken);

            if (failures.Count == 0)
            {
                _logger.LogInformation(
                    "All attributes saved individually for '{FileName}' after the batch was rejected", createdItem.Name);
                return true;
            }

            // Names the rejected fields but leaves ACC's raw error wording out of the user-facing
            // message - it's already captured per-attribute in the log from WriteAttributesIndividuallyAsync.
            var fieldNames = string.Join(", ", failures.Select(f => ResolveAttributeName(convention, f.AttributeId)));

            warnings.Add($"{fieldNames} couldn't be saved - use Edit Attributes to fix them.");

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex, "File '{FileName}' was created but its attributes could not be written", createdItem.Name);

            warnings.Add("Attributes couldn't be saved - use Edit Attributes to set them.");

            return false;
        }
    }

    /// <summary>
    /// Writes attributes one at a time, returning the ones ACC refused. Used only as a fallback after
    /// a rejected batch, so the cost of one request per attribute is paid only on the failure path.
    /// </summary>
    private async Task<IReadOnlyList<FailedAttribute>> WriteAttributesIndividuallyAsync(
        string projectId,
        string versionUrn,
        IReadOnlyCollection<CustomAttributeUpdateRequestDto> updates,
        CancellationToken cancellationToken)
    {
        var failures = new List<FailedAttribute>();

        foreach (var update in updates)
        {
            try
            {
                await _dataManagementService.UpdateItemCustomAttributesAsync(
                    projectId, versionUrn, [update], cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    "Attribute {AttributeId} rejected: {Message}",
                    update.AttributeId, ex.Message);

                failures.Add(new FailedAttribute(update.AttributeId, update.Value, ex.Message));
            }
        }

        return failures;
    }

    /// <summary>
    /// Maps an attribute id back to the display name the user saw, so a rejection message names the
    /// field rather than a numeric id that means nothing outside ACC's database.
    /// </summary>
    private static string ResolveAttributeName(NamingConventionDto convention, int attributeId)
    {
        return convention.Fields.FirstOrDefault(f => f.AttributeId == attributeId)?.Name
            ?? convention.Metadata.FirstOrDefault(f => f.AttributeId == attributeId)?.Name
            ?? $"attribute {attributeId}";
    }

    /// <summary>An attribute ACC refused, with its value and the reason given.</summary>
    private sealed record FailedAttribute(int AttributeId, string? Value, string Error);

    /// <summary>
    /// Writes attributes, retrying briefly when the failure looks transient.
    ///
    /// A version created seconds ago is not always visible to the BIM 360 Docs API yet - the item is
    /// created through the Data Management API, and the docs side indexes it a moment later. Writing
    /// attributes immediately can therefore fail with a not-found or server error on a file that is
    /// perfectly valid, and succeed on a retry a second later.
    ///
    /// Validation and permission failures are NOT retried: a rejected value and a missing permission
    /// will fail identically every time, and retrying only delays the error the user needs to see.
    /// </summary>
    private async Task WriteAttributesWithRetryAsync(
        string projectId,
        string versionUrn,
        IReadOnlyCollection<CustomAttributeUpdateRequestDto> updates,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 4;
        var delay = TimeSpan.FromSeconds(1);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await _dataManagementService.UpdateItemCustomAttributesAsync(
                    projectId, versionUrn, updates, cancellationToken);
                return;
            }
            catch (Exception ex) when (
                attempt < maxAttempts
                && ex is not AttributeValidationException
                && ex is not PermissionDeniedException
                && ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    "Attribute write attempt {Attempt}/{MaxAttempts} for version {VersionUrn} failed ({Message}); retrying in {Delay}s",
                    attempt, maxAttempts, versionUrn, ex.Message, delay.TotalSeconds);

                await Task.Delay(delay, cancellationToken);
                delay *= 2;
            }
        }
    }

}

/// <summary>
/// Names of the ACC custom attributes this workflow treats specially, and the values it defaults
/// them to.
///
/// Matching is by name because that is how the project's administrator identifies them in ACC; the
/// numeric attribute ids differ per project and cannot be hardcoded.
/// </summary>
public static class DeliverableAttributeNames
{
    public const string Status = "Status";
    public const string Revision = "Revision";
    public const string Description = "Description";

    /// <summary>
    /// ISO 19650 status for work in progress / not yet shared - the correct starting state for a
    /// file created in WIP.
    /// </summary>
    public const string DefaultStatus = "S0";

    /// <summary>First preliminary revision, matching a brand-new WIP draft.</summary>
    public const string DefaultRevision = "P01.01";

    /// <summary>
    /// The default value for an attribute, or null when the attribute has no workflow-defined
    /// default and should start empty.
    /// </summary>
    public static string? DefaultValueFor(string attributeName) => attributeName switch
    {
        _ when string.Equals(attributeName, Status, StringComparison.OrdinalIgnoreCase) => DefaultStatus,
        _ when string.Equals(attributeName, Revision, StringComparison.OrdinalIgnoreCase) => DefaultRevision,
        _ => null
    };

}
