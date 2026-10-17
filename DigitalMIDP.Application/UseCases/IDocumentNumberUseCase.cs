using DigitalMIDP.Application.Dtos;

namespace DigitalMIDP.Application.UseCases;

/// <summary>
/// Composes, allocates and validates document numbers - the structured identifiers that are a
/// deliverable's authoritative identity.
///
/// Uniqueness is scoped to the discipline: a number must be unique within its WIP discipline folder,
/// but the same number legitimately reappears in Shared and Published as the deliverable matures
/// through the lifecycle, so those areas are deliberately not consulted.
/// </summary>
public interface IDocumentNumberUseCase
{
    /// <summary>
    /// Builds a document number from the values chosen for each naming-convention field.
    /// </summary>
    /// <param name="convention">The project's naming convention.</param>
    /// <param name="chosenValues">Values keyed by custom attribute id, covering the user-supplied fields.</param>
    /// <param name="sequence">The already-formatted sequential segment, e.g. "001".</param>
    /// <returns>The composed number.</returns>
    DocumentNumberDto Compose(
        NamingConventionDto convention,
        IReadOnlyDictionary<int, string> chosenValues,
        string sequence);

    /// <summary>
    /// Allocates the next free document number for a discipline.
    ///
    /// The sequence continues from the highest already in use rather than filling gaps left by
    /// deleted files, so a number is never reissued.
    /// </summary>
    /// <param name="projectId">The ID of the project.</param>
    /// <param name="discipline">The target discipline. A discipline whose folder does not exist yet starts at the first sequence.</param>
    /// <param name="convention">The project's naming convention.</param>
    /// <param name="chosenValues">Values keyed by custom attribute id, covering the user-supplied fields.</param>
    /// <param name="accessToken">The OAuth access token.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>The next available document number.</returns>
    Task<DocumentNumberDto> AllocateNextAsync(
        string projectId,
        DisciplineFolderDto discipline,
        NamingConventionDto convention,
        IReadOnlyDictionary<int, string> chosenValues,
        string accessToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Allocates the next free document number for a discipline AND confirms it is free, from a
    /// single read of the discipline folder.
    ///
    /// <para>
    /// This replaces calling <see cref="AllocateNextAsync"/> followed by
    /// <see cref="CheckAvailabilityAsync"/>, which is what the creation flow used to do. Each of
    /// those methods independently lists the folder's contents and batch-reads every file's custom
    /// attributes, and they ran back to back with nothing in between - so the second scan read the
    /// same state as the first and cost the same again. On a 28-file discipline folder that was the
    /// single largest component of the ~30s a creation took.
    /// </para>
    ///
    /// <para>
    /// What is lost by scanning once: nothing that was actually being protected. The stated purpose
    /// of the second scan was to narrow the window between allocating a number and creating the
    /// file, but with no work separating the two calls that window was already effectively zero.
    /// The race it cannot close - another user creating the same number concurrently in ACC - was
    /// never closed by either version, because ACC offers no way to reserve a number; it is still
    /// caught after the fact by the conflict result.
    /// </para>
    /// </summary>
    /// <param name="projectId">The ID of the project.</param>
    /// <param name="discipline">The target discipline. A discipline whose folder does not exist yet starts at the first sequence.</param>
    /// <param name="convention">The project's naming convention.</param>
    /// <param name="chosenValues">Values keyed by custom attribute id, covering the user-supplied fields.</param>
    /// <param name="reservedNumbers">
    /// Numbers that must be treated as already taken even though a scan will not find them yet.
    ///
    /// <para>
    /// This is what makes batch creation correct rather than merely fast. A file created a moment
    /// ago may not be visible to the next scan - ACC indexes a new version shortly after the Data
    /// Management API creates it - so a batch of five documents could otherwise allocate the same
    /// number more than once. Passing the numbers already issued in this batch removes the
    /// dependency on how quickly ACC catches up.
    /// </para>
    /// </param>
    /// <param name="accessToken">The OAuth access token.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>The allocated number together with its availability.</returns>
    Task<DocumentNumberAllocationDto> AllocateAndVerifyNextAsync(
        string projectId,
        DisciplineFolderDto discipline,
        NamingConventionDto convention,
        IReadOnlyDictionary<int, string> chosenValues,
        IReadOnlyCollection<string> reservedNumbers,
        string accessToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a proposed document number is free within its discipline.
    /// </summary>
    /// <param name="projectId">The ID of the project.</param>
    /// <param name="discipline">The target discipline.</param>
    /// <param name="convention">The project's naming convention.</param>
    /// <param name="proposedNumber">The number to check.</param>
    /// <param name="accessToken">The OAuth access token.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>
    /// The availability result. When the number is taken, the result carries the conflicting file's
    /// name and the next free number to offer instead.
    /// </returns>
    Task<DocumentNumberAvailabilityDto> CheckAvailabilityAsync(
        string projectId,
        DisciplineFolderDto discipline,
        NamingConventionDto convention,
        DocumentNumberDto proposedNumber,
        string accessToken,
        CancellationToken cancellationToken = default);
}
