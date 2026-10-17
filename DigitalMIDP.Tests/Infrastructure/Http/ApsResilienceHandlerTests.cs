using System.Net;
using System.Net.Http.Json;
using DigitalMIDP.Infrastructure.Http;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace DigitalMIDP.Tests.Infrastructure.Http;

/// <summary>
/// Tests for <see cref="ApsResilienceHandler"/>.
///
/// <para>
/// Half of these assert that something is NOT retried. That half is the important half: retrying the
/// wrong request is worse than not retrying at all - a repeated create can leave a duplicate file in
/// ACC, and a retried 403 just delays the message the user needs to read.
/// </para>
/// </summary>
public sealed class ApsResilienceHandlerTests
{
    private const string ApsUrl = "https://developer.api.autodesk.com/data/v1/projects/b.p1/folders/f1/contents";

    /// <summary>
    /// Builds the handler with a 1ms first delay instead of the production 1s, so these tests
    /// exercise the real retry logic without waiting out the real backoff schedule. The one test
    /// that asserts on timing (<see cref="SendAsync_WithRetryAfterHeader_WaitsRoughlyThatLongRatherThanGuessing"/>)
    /// is unaffected, because a server-supplied Retry-After replaces the computed delay entirely.
    /// </summary>
    private static HttpClient CreateClient(CountingHandler inner) =>
        new(new ApsResilienceHandler(
            Substitute.For<ILogger<ApsResilienceHandler>>(),
            TimeSpan.FromMilliseconds(1))
        {
            InnerHandler = inner
        });

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task SendAsync_WithTransientStatus_RetriesUpToFourAttempts(HttpStatusCode status)
    {
        var inner = new CountingHandler(_ => new HttpResponseMessage(status));
        using var client = CreateClient(inner);

        var response = await client.GetAsync(ApsUrl);

        // One try plus three retries. The failure is still returned afterwards rather than thrown -
        // the caller's own status handling decides what it means.
        response.StatusCode.Should().Be(status);
        inner.Attempts.Should().Be(4);
    }

    [Fact]
    public async Task SendAsync_WithTransientFailureThatThenSucceeds_ReturnsTheSuccess()
    {
        var inner = new CountingHandler(attempt => attempt switch
        {
            1 => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            2 => new HttpResponseMessage(HttpStatusCode.BadGateway),
            _ => new HttpResponseMessage(HttpStatusCode.OK)
        });

        using var client = CreateClient(inner);

        var response = await client.GetAsync(ApsUrl);

        // The behaviour that matters in practice: a project load no longer dies because APS had a
        // momentary blip on one of the hundreds of requests it makes.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        inner.Attempts.Should().Be(3);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.UnsupportedMediaType)]
    public async Task SendAsync_WithNonTransientStatus_DoesNotRetry(HttpStatusCode status)
    {
        var inner = new CountingHandler(_ => new HttpResponseMessage(status));
        using var client = CreateClient(inner);

        var response = await client.GetAsync(ApsUrl);

        // 401/403: a missing token or a missing ACC permission fails the same way every time.
        // 400: a value ACC rejected stays rejected, and callers map it to a validation error they
        //      handle specifically.
        // 404: permanent for a deleted item; the one transient case (a version not yet indexed) is
        //      retried where that is actually knowable, not blindly here.
        response.StatusCode.Should().Be(status);
        inner.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_WithConnectionFailure_RetriesThenSurfacesTheException()
    {
        var inner = new CountingHandler(_ => throw new HttpRequestException("connection reset"));
        using var client = CreateClient(inner);

        var act = () => client.GetAsync(ApsUrl);

        await act.Should().ThrowAsync<HttpRequestException>();
        inner.Attempts.Should().Be(4);
    }

    [Fact]
    public async Task SendAsync_WithCancellation_DoesNotRetry()
    {
        using var cts = new CancellationTokenSource();

        var inner = new CountingHandler(_ =>
        {
            cts.Cancel();
            cts.Token.ThrowIfCancellationRequested();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = CreateClient(inner);

        var act = () => client.GetAsync(ApsUrl, cts.Token);

        // Retrying would ignore a cancellation the user asked for - the Cancel button on a project
        // load would appear to do nothing for several more seconds.
        await act.Should().ThrowAsync<OperationCanceledException>();
        inner.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_WithCreatingPost_DoesNotRetryEvenOnATransientFailure()
    {
        var inner = new CountingHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var client = CreateClient(inner);

        var response = await client.PostAsync(
            "https://developer.api.autodesk.com/data/v1/projects/b.p1/items",
            JsonContent.Create(new { data = "x" }));

        // A create that timed out may well have succeeded server-side. Retrying it risks a second
        // file in ACC that nothing in the application knows about - strictly worse than the error.
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        inner.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_WithBatchGetPost_DoesRetryBecauseItIsReallyARead()
    {
        var inner = new CountingHandler(attempt => attempt == 1
            ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            : new HttpResponseMessage(HttpStatusCode.OK));

        using var client = CreateClient(inner);

        var response = await client.PostAsync(
            "https://developer.api.autodesk.com/bim360/docs/v1/projects/p1/versions:batch-get",
            JsonContent.Create(new { urns = new[] { "urn:a" } }));

        // batch-get is a POST only because its urn list is too long for a query string. It creates
        // nothing, so repeating it is safe - and it is the request most likely to be rate-limited,
        // since it is issued for every file in a project.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        inner.Attempts.Should().Be(2);
    }

    [Fact]
    public async Task SendAsync_WithRetryAfterHeader_WaitsRoughlyThatLongRatherThanGuessing()
    {
        var inner = new CountingHandler(attempt =>
        {
            if (attempt > 1)
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            var throttled = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            throttled.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
                TimeSpan.FromSeconds(2));
            return throttled;
        });

        using var client = CreateClient(inner);

        var started = DateTimeOffset.UtcNow;
        var response = await client.GetAsync(ApsUrl);
        var elapsed = DateTimeOffset.UtcNow - started;

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Honouring the server's stated window matters: the default first delay is ~1s, so guessing
        // would send the next attempt while still inside APS's own throttle and get rejected again.
        elapsed.Should().BeGreaterThan(TimeSpan.FromSeconds(1.5));
        inner.Attempts.Should().Be(2);
    }

    /// <summary>
    /// Terminal handler that counts attempts and builds each response from the attempt number, so a
    /// test can describe a sequence of failures followed by a success.
    /// </summary>
    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly Func<int, HttpResponseMessage> _respond;
        private int _attempts;

        public CountingHandler(Func<int, HttpResponseMessage> respond) => _respond = respond;

        public int Attempts => Volatile.Read(ref _attempts);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var attempt = Interlocked.Increment(ref _attempts);
            return Task.FromResult(_respond(attempt));
        }
    }
}
