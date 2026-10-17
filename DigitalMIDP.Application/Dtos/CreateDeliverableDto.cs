namespace DigitalMIDP.Application.Dtos;

/// <summary>
/// Everything needed to create one deliverable.
/// </summary>
/// <param name="HubId">The ID of the hub.</param>
/// <param name="ProjectId">The ID of the project (Data Management API format with "b." prefix).</param>
/// <param name="AccessToken">The OAuth access token.</param>
/// <param name="Template">
/// The template to create from, or null to create an empty file. A template is normally required;
/// the null case exists only for the explicitly-offered "no template" path.
/// </param>
/// <param name="DisciplineCode">The role/discipline code, which determines the WIP destination folder.</param>
/// <param name="FieldValues">
/// Chosen values for the naming-convention filename fields, keyed by custom attribute id. The
/// sequential number is excluded - the application allocates it.
/// </param>
/// <param name="MetadataValues">
/// Values for the non-filename attributes (Status, Revision and the directly entered Deliverable
/// Title), keyed by custom attribute id. Description is preferred where it exists in the related
/// project metadata; an attribute actually named Deliverable Title is also supported.
/// </param>
public sealed record CreateDeliverableRequestDto(
    string HubId,
    string ProjectId,
    string AccessToken,
    DeliverableTemplateDto? Template,
    string DisciplineCode,
    IReadOnlyDictionary<int, string> FieldValues,
    IReadOnlyDictionary<int, string?> MetadataValues
);

/// <summary>
/// The outcome of a deliverable creation attempt.
/// </summary>
/// <param name="Success">True when the file was created. Attribute failures do not make this false - see <paramref name="Warnings"/>.</param>
/// <param name="CreatedItem">The created file, when creation succeeded.</param>
/// <param name="DocumentNumber">The allocated document number.</param>
/// <param name="FileName">The created file's name, including extension.</param>
/// <param name="DestinationFolder">The WIP discipline folder the file was created in.</param>
/// <param name="AttributesApplied">
/// False when the file was created but its custom attributes could not be written. The file exists
/// but is not yet a compliant deliverable, so this is reported rather than folded into failure.
/// </param>
/// <param name="Warnings">Non-fatal problems worth showing the user.</param>
/// <param name="ErrorMessage">Why creation failed, when it did.</param>
/// <param name="SuggestedDocumentNumber">
/// When creation was refused because the number was taken, the next free number to offer instead.
/// </param>
public sealed record CreateDeliverableResultDto(
    bool Success,
    ItemDto? CreatedItem,
    string? DocumentNumber,
    string? FileName,
    string? DestinationFolder,
    bool AttributesApplied,
    IReadOnlyList<string> Warnings,
    string? ErrorMessage,
    string? SuggestedDocumentNumber = null
)
{
    public static CreateDeliverableResultDto Failed(string errorMessage, string? suggestedNumber = null) =>
        new(false, null, null, null, null, false, [], errorMessage, suggestedNumber);
}

/// <summary>
/// Everything needed to create several deliverables in one operation, all sharing the same filename
/// segments (Project, Originator, Volume, Type, Role, ...) and the same shared metadata, but each
/// with its own directly entered Deliverable Title and sequential document number.
///
/// This is the shape the client described directly: "5 numbers, all with the same attributes, but
/// different titles, and thus different final numbers." The document number does not depend on the
/// title at all - only on the shared field values plus an auto-incrementing sequence - so producing
/// N distinct, valid, non-colliding numbers requires nothing beyond running the existing single-item
/// creation N times, once per title set, in order.
/// </summary>
/// <param name="HubId">The ID of the hub.</param>
/// <param name="ProjectId">The ID of the project (Data Management API format with "b." prefix).</param>
/// <param name="AccessToken">The OAuth access token.</param>
/// <param name="Template">The template every item is created from, or null for the "no template" path.</param>
/// <param name="DisciplineCode">The role/discipline code shared by every item, which determines the WIP destination folder.</param>
/// <param name="FieldValues">
/// Chosen values for the naming-convention filename fields, keyed by custom attribute id, shared by
/// every item. The sequential number is excluded - the application allocates a fresh one per item.
/// </param>
/// <param name="SharedMetadataValues">
/// Shared metadata values (Status, Revision, and any other project-defined metadata) shared by
/// every item, keyed by custom attribute id.
/// </param>
/// <param name="PerItemTitleValues">
/// One entry per deliverable to create, holding its related Description (or Deliverable Title)
/// attribute value keyed by custom attribute id. The list's length is the batch size.
/// </param>
public sealed record CreateDeliverableBatchRequestDto(
    string HubId,
    string ProjectId,
    string AccessToken,
    DeliverableTemplateDto? Template,
    string DisciplineCode,
    IReadOnlyDictionary<int, string> FieldValues,
    IReadOnlyDictionary<int, string?> SharedMetadataValues,
    IReadOnlyList<IReadOnlyDictionary<int, string?>> PerItemTitleValues
);

/// <summary>
/// The outcome of a batch creation, as one result per requested item, in the same order they were
/// requested. A batch is not all-or-nothing: ACC has no cross-file transaction, and refusing every
/// remaining item just because one collided or was rejected would throw away deliverables that were
/// otherwise perfectly valid. Each item's own <see cref="CreateDeliverableResultDto.Success"/> is
/// what determines whether it exists.
/// </summary>
/// <param name="Items">One result per requested item, in request order.</param>
public sealed record CreateDeliverableBatchResultDto(IReadOnlyList<CreateDeliverableResultDto> Items)
{
    public int SuccessCount => Items.Count(i => i.Success);

    public int FailureCount => Items.Count - SuccessCount;

    public bool AllSucceeded => Items.Count > 0 && FailureCount == 0;

    public bool AnySucceeded => SuccessCount > 0;
}

/// <summary>
/// Reported once per item as a batch progresses, so a caller running the batch in the background
/// (see <see cref="ICreateDeliverableUseCase.CreateManyAsync"/>) can show live progress - "creating
/// deliverable 3 of 5" - without waiting for the whole batch to finish.
/// </summary>
/// <param name="Completed">How many items have finished (successfully or not), including this one.</param>
/// <param name="Total">Total items in the batch.</param>
/// <param name="LastResult">The outcome of the item that just finished.</param>
public sealed record DeliverableBatchProgressDto(int Completed, int Total, CreateDeliverableResultDto LastResult);
