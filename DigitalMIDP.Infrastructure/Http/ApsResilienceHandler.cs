using System.Net;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace DigitalMIDP.Infrastructure.Http;

/// <summary>
/// Retries APS requests that failed for a reason worth trying again, with an increasing delay
/// between attempts.
///
/// <para>
/// Before this existed, a single dropped connection or one 503 from APS failed a whole project load
/// or abandoned a half-finished deliverable batch. APS returns transient failures routinely -
/// 429 when the application's own parallel fan-out (path metadata for every file in a project) runs
/// ahead of the rate limit, 502/503/504 from its gateways, and plain socket errors - and all of
/// those succeed on a second attempt moments later.
/// </para>
///
/// <para><b>What is NOT retried, and why it matters:</b></para>
/// <list type="bullet">
///   <item>
///     <description>
///     <b>401/403</b> - a missing token or a missing ACC permission fails identically every time.
///     Retrying only delays the "you need to sign in" or "you cannot write to this folder" message
///     the user has to act on. Token refresh is <see cref="ApsAuthorizationHandler"/>'s job, and it
///     happens before the request is ever sent.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>400/422</b> - a value ACC rejected is still rejected on the next attempt. These map to
///     <c>AttributeValidationException</c>, which callers use to decide to retry attributes one at a
///     time (see <c>CreateDeliverableUseCase.ApplyAttributesAsync</c>); blind retries here would
///     multiply that work for nothing.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>404</b> - ambiguous, and deliberately left alone. It is genuinely transient right after a
///     file is created (the Docs API indexes a new version a moment after the Data Management API
///     creates it), but it is permanent for an item that was deleted. That specific transient case
///     is already handled where the distinction is knowable - see
///     <c>CreateDeliverableUseCase.WriteAttributesWithRetryAsync</c> - so retrying it blindly here
///     would turn every genuinely-missing item into a multi-second stall.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>Non-idempotent writes</b> - POSTs that create something are excluded entirely. A create
///     that timed out may well have succeeded server-side, and retrying it would produce a second
///     file or a second folder. See <see cref="IsRetryableRequest"/>.
///     </description>
///   </item>
/// </list>
///
/// <para>
/// Implemented as a <see cref="DelegatingHandler"/> over a Polly v8 pipeline so it composes with
/// the existing handler chain (it sits outside <see cref="ApsAuthorizationHandler"/>, so a retried
/// request re-enters that handler and picks up a refreshed token if one became due mid-sequence).
/// </para>
/// </summary>
internal sealed class ApsResilienceHandler : DelegatingHandler
{
    /// <summary>
    /// Four attempts total: three retries after the first try. With the delays below that is at
    /// most ~7s of waiting (1s + 2s + 4s, plus jitter) before the error is surfaced - long enough to
    /// ride out a gateway blip, short enough that a genuinely broken call still fails while the user
    /// is looking at it.
    /// </summary>
    private const int MaxRetries = 3;

    private static readonly TimeSpan DefaultBaseDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Ceiling on a server-supplied <c>Retry-After</c>. APS can answer a 429 with a window far
    /// longer than a user will sit through; past this point failing is more honest than appearing
    /// frozen.
    /// </summary>
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(20);

    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    public ApsResilienceHandler(ILogger<ApsResilienceHandler> logger)
        : this(logger, DefaultBaseDelay)
    {
    }

    /// <summary>
    /// Overload taking the first retry delay, so tests can exercise the real retry logic without
    /// waiting out the real backoff schedule (which would otherwise add tens of seconds to a unit
    /// test run). Production always uses <see cref="DefaultBaseDelay"/> via the constructor above.
    /// </summary>
    internal ApsResilienceHandler(ILogger<ApsResilienceHandler> logger, TimeSpan baseDelay)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = MaxRetries,
                BackoffType = DelayBackoffType.Exponential,
                Delay = baseDelay,

                // Jitter so a fan-out that gets rate-limited as a group does not come back as a
                // group and get rate-limited again in lockstep.
                UseJitter = true,

                ShouldHandle = args => ValueTask.FromResult(ShouldRetry(args.Outcome)),

                // Honour Retry-After when APS sends one: guessing an exponential delay against a
                // server that has stated its own window just gets the next attempt rejected too.
                DelayGenerator = args => ValueTask.FromResult(ResolveRetryAfter(args.Outcome)),

                OnRetry = args =>
                {
                    var reason = args.Outcome.Result is { } response
                        ? $"HTTP {(int)response.StatusCode}"
                        : args.Outcome.Exception?.GetType().Name ?? "unknown error";

                    logger.LogWarning(
                        "APS request failed ({Reason}); retry {Attempt}/{MaxRetries} in {Delay:0.##}s",
                        reason, args.AttemptNumber + 1, MaxRetries, args.RetryDelay.TotalSeconds);

                    return default;
                }
            })
            .Build();
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!IsRetryableRequest(request))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        return await _pipeline.ExecuteAsync(
            async token => await base.SendAsync(request, token),
            cancellationToken);
    }

    /// <summary>
    /// Decides whether a request may be sent more than once at all, independently of how it failed.
    ///
    /// <para>
    /// Reads and the idempotent writes are fair game. A POST is not: the create-item, create-folder
    /// and create-storage calls in <c>DataManagementService.Creation</c> each produce a new resource,
    /// and a retry after a timeout risks a duplicate file in ACC that nothing in the application
    /// knows about - strictly worse than the error. The one POST that is safe to repeat,
    /// <c>versions:batch-get</c>, is a read expressed as a POST only because its URN list is too
    /// long for a query string; it is allowed through by name.
    /// </para>
    ///
    /// <para>
    /// The custom-attributes batch-update POST is also excluded here, even though it is effectively
    /// idempotent (writing the same values twice is a no-op). Its retry policy is not generic: see
    /// <c>CreateDeliverableUseCase.WriteAttributesWithRetryAsync</c>, which retries it with
    /// knowledge this handler does not have - that a brand-new version takes a moment to become
    /// visible to the Docs API, and that a validation failure must instead fan out into one request
    /// per attribute. Two retry layers over the same call would multiply those attempts.
    /// </para>
    /// </summary>
    private static bool IsRetryableRequest(HttpRequestMessage request)
    {
        if (request.Method == HttpMethod.Get
            || request.Method == HttpMethod.Head
            || request.Method == HttpMethod.Options)
        {
            return true;
        }

        if (request.Method == HttpMethod.Post)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            return path.EndsWith("versions:batch-get", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>
    /// Transient-failure classification. See the class remarks for why each excluded status is
    /// excluded.
    /// </summary>
    private static bool ShouldRetry(Outcome<HttpResponseMessage> outcome)
    {
        if (outcome.Exception is not null)
        {
            // A connection that never completed. OperationCanceledException is excluded because the
            // caller asked to stop - retrying would ignore a cancellation the user requested.
            return outcome.Exception is HttpRequestException or TimeoutException;
        }

        var response = outcome.Result;
        if (response is null)
        {
            return false;
        }

        return response.StatusCode switch
        {
            HttpStatusCode.RequestTimeout => true,          // 408
            HttpStatusCode.TooManyRequests => true,         // 429 - the rate limiter, by far the most common here
            HttpStatusCode.BadGateway => true,              // 502
            HttpStatusCode.ServiceUnavailable => true,      // 503
            HttpStatusCode.GatewayTimeout => true,          // 504
            HttpStatusCode.InternalServerError => true,     // 500 - APS returns these transiently
            _ => false
        };
    }

    /// <summary>
    /// Returns the server's own <c>Retry-After</c> (clamped to <see cref="MaxDelay"/>), or null to
    /// let Polly apply its exponential-with-jitter schedule.
    /// </summary>
    private static TimeSpan? ResolveRetryAfter(Outcome<HttpResponseMessage> outcome)
    {
        var retryAfter = outcome.Result?.Headers.RetryAfter;

        if (retryAfter is null)
        {
            return null;
        }

        var delay = retryAfter.Delta
                    ?? (retryAfter.Date.HasValue ? retryAfter.Date.Value - DateTimeOffset.UtcNow : null);

        if (delay is null || delay <= TimeSpan.Zero)
        {
            return null;
        }

        return delay > MaxDelay ? MaxDelay : delay;
    }
}
