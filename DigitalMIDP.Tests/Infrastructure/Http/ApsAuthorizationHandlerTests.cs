using System.Net;
using DigitalMIDP.Application.Interfaces;
using DigitalMIDP.Infrastructure.Http;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace DigitalMIDP.Tests.Infrastructure.Http;

/// <summary>
/// Tests for <see cref="ApsAuthorizationHandler"/>.
/// </summary>
public sealed class ApsAuthorizationHandlerTests
{
    private readonly ITokenProvider _tokenProvider = Substitute.For<ITokenProvider>();

    private HttpClient CreateClient(RecordingHandler inner) =>
        new(new ApsAuthorizationHandler(
            _tokenProvider,
            Substitute.For<ILogger<ApsAuthorizationHandler>>())
        {
            InnerHandler = inner
        });

    [Fact]
    public async Task SendAsync_ToAnApsHost_AddsTheBearerHeader()
    {
        _tokenProvider.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns("access-1");

        var inner = new RecordingHandler();
        using var client = CreateClient(inner);

        await client.GetAsync("https://developer.api.autodesk.com/data/v1/projects/b.p1/folders/f1/contents");

        inner.LastRequest!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        inner.LastRequest.Headers.Authorization.Parameter.Should().Be("access-1");
    }

    [Fact]
    public async Task SendAsync_ToANonApsHost_DoesNotAddTheHeaderOrAskForAToken()
    {
        var inner = new RecordingHandler();
        using var client = CreateClient(inner);

        // A pre-signed S3 transfer URL. S3 rejects a signed request that ALSO presents a bearer
        // token, so attaching one would break every upload and template download.
        await client.GetAsync("https://s3.amazonaws.com/bucket/object?X-Amz-Signature=abc");

        inner.LastRequest!.Headers.Authorization.Should().BeNull();
        await _tokenProvider.DidNotReceive().GetAccessTokenAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_WithManyRequests_AsksTheProviderOncePerRequestAndNothingElse()
    {
        _tokenProvider.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns("access-1");

        var inner = new RecordingHandler();
        using var client = CreateClient(inner);

        for (var i = 0; i < 10; i++)
        {
            await client.GetAsync($"https://developer.api.autodesk.com/data/v1/items/{i}");
        }

        // The handler is deliberately thin - one provider call and one header assignment. The
        // provider is what makes that call cheap (it answers from a cached token); the handler must
        // not add work of its own on top, because every APS call in the application passes here.
        await _tokenProvider.Received(10).GetAccessTokenAsync(Arg.Any<CancellationToken>());
        inner.Requests.Should().Be(10);
    }

    [Fact]
    public async Task SendAsync_WhenNotSignedIn_ThrowsWithoutSendingTheRequest()
    {
        _tokenProvider
            .GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("No access token available."));

        var inner = new RecordingHandler();
        using var client = CreateClient(inner);

        var act = () => client.GetAsync("https://developer.api.autodesk.com/data/v1/hubs");

        await act.Should().ThrowAsync<InvalidOperationException>();

        // Sending it anonymously would return a 401 that reads like an ACC permissions problem
        // rather than "you are signed out".
        inner.Requests.Should().Be(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SendAsync_WithABlankToken_ThrowsWithoutSendingTheRequest(string token)
    {
        _tokenProvider.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns(token);

        var inner = new RecordingHandler();
        using var client = CreateClient(inner);

        var act = () => client.GetAsync("https://developer.api.autodesk.com/data/v1/hubs");

        await act.Should().ThrowAsync<InvalidOperationException>();
        inner.Requests.Should().Be(0);
    }

    [Fact]
    public async Task SendAsync_WhenTheProviderFailsUnexpectedly_SurfacesTheFailure()
    {
        _tokenProvider
            .GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("token endpoint unreachable"));

        var inner = new RecordingHandler();
        using var client = CreateClient(inner);

        var act = () => client.GetAsync("https://developer.api.autodesk.com/data/v1/hubs");

        // A refresh that could not complete is not quietly downgraded to an anonymous request.
        await act.Should().ThrowAsync<HttpRequestException>();
        inner.Requests.Should().Be(0);
    }

    [Fact]
    public async Task SendAsync_PassesTheRequestsCancellationThroughToTheProvider()
    {
        using var cts = new CancellationTokenSource();

        var wasCancellable = false;
        var sawCancellation = false;

        _tokenProvider
            .GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                // Inspected from inside the call, not afterwards: HttpClient links the caller's token
                // with its own timeout token and disposes that linked source once the request
                // completes, so the token is already detached by the time the await returns.
                var token = call.Arg<CancellationToken>();
                wasCancellable = token.CanBeCanceled;

                cts.Cancel();
                sawCancellation = token.IsCancellationRequested;

                return "access-1";
            });

        var inner = new RecordingHandler();
        using var client = CreateClient(inner);

        try
        {
            await client.GetAsync("https://developer.api.autodesk.com/data/v1/hubs", cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected - the token was cancelled mid-flight by the stub above.
        }

        // Not compared by identity (it cannot be). What has to hold is that cancelling the caller's
        // token cancels what the provider was given - otherwise a cancelled project load would sit
        // waiting on a token refresh it no longer needs.
        wasCancellable.Should().BeTrue();
        sawCancellation.Should().BeTrue();
    }

    [Fact]
    public void Constructor_WithMissingDependencies_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ApsAuthorizationHandler(
            null!, Substitute.For<ILogger<ApsAuthorizationHandler>>()));
        Assert.Throws<ArgumentNullException>(() => new ApsAuthorizationHandler(_tokenProvider, null!));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            Requests++;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
