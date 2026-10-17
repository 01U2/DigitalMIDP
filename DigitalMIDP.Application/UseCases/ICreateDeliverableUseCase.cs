using DigitalMIDP.Application.Dtos;

namespace DigitalMIDP.Application.UseCases;

/// <summary>
/// Creates a deliverable from a template: allocates a unique document number, resolves (and if
/// necessary creates) the WIP discipline folder, copies the template into it under the number, and
/// writes the required attributes onto the new file.
/// </summary>
public interface ICreateDeliverableUseCase
{
    /// <summary>
    /// Creates a deliverable.
    /// </summary>
    /// <param name="request">The creation request.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>
    /// The outcome. A refused creation (most often a duplicate document number) comes back as an
    /// unsuccessful result carrying an explanation, rather than as an exception - it is an expected
    /// path through the workflow, not a fault.
    /// </returns>
    Task<CreateDeliverableResultDto> CreateAsync(
        CreateDeliverableRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates several deliverables that share every attribute except their titles.
    ///
    /// Items are created one after another, never concurrently. Each item's document number is
    /// allocated by scanning what already exists in the destination WIP folder, so a later item must
    /// not be allocated until the previous item's file (if it was created) is visible to that scan -
    /// running them in parallel would race and risk two items landing on the same number. This is
    /// also why the batch is not all-or-nothing: a rejected or duplicate item does not stop the rest,
    /// since each remaining item still scans the folder afresh and gets a valid number regardless of
    /// what happened to the ones before it.
    /// </summary>
    /// <param name="request">The batch request - shared attributes plus one title set per item.</param>
    /// <param name="progress">
    /// Optional callback invoked once per item as it finishes, so a caller running this in the
    /// background can show live progress instead of only learning the outcome at the very end.
    /// </param>
    /// <param name="cancellationToken">
    /// Checked between items (not mid-item, so an item already in flight always finishes cleanly).
    /// Cancelling stops the batch early; items already created remain created.
    /// </param>
    /// <returns>One result per item completed before cancellation or the end of the batch, in request order.</returns>
    Task<CreateDeliverableBatchResultDto> CreateManyAsync(
        CreateDeliverableBatchRequestDto request,
        IProgress<DeliverableBatchProgressDto>? progress = null,
        CancellationToken cancellationToken = default);
}
