using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.Exceptions;
using DigitalMIDP.Application.Interfaces;
using DigitalMIDP.Domain.ValueObjects;
using DigitalMIDP.Infrastructure.Authentication;
using DigitalMIDP.Infrastructure.Caching;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace DigitalMIDP.Tests.Infrastructure.Authentication;

/// <summary>
/// Tests for <see cref="ApsTokenProvider"/>, the per-request token fast path.
///
/// <para>
/// The behaviour under test is mostly about what does NOT happen: a request that reuses a valid
/// token must not touch the token store, must not re-check expiry, and must not log. That is the
/// whole reason the class exists, so most of these assertions are "received no calls".
/// </para>
/// </summary>
public sealed class ApsTokenProviderTests
{
    private readonly ITokenStorage _tokenStorage = Substitute.For<ITokenStorage>();
    private readonly IAuthenticationService _authenticationService = Substitute.For<IAuthenticationService>();
    private readonly IApsMetadataCache _metadataCache = Substitute.For<IApsMetadataCache>();

    private ApsTokenProvider CreateProvider() => new(
        _tokenStorage,
        _authenticationService,
        _metadataCache,
        Substitute.For<ILogger<ApsTokenProvider>>());

    private static AccessToken ValidToken(string token = "access-1", int expiresInSeconds = 3600) =>
        AccessToken.Create(token, expiresInSeconds);

    [Fact]
    public async Task GetAccessTokenAsync_AfterSave_ServesFromCacheWithoutReadingStorage()
    {
        using var provider = CreateProvider();

        await provider.SaveTokensAsync("access-1", "refresh-1", 3600);
        _tokenStorage.ClearReceivedCalls();

        var first = await provider.GetAccessTokenAsync(CancellationToken.None);
        var second = await provider.GetAccessTokenAsync(CancellationToken.None);

        first.Should().Be("access-1");
        second.Should().Be("access-1");

        // The point of the class: signing in primes the cache, so the very first APS request after
        // sign-in already takes the fast path instead of going back to the store to learn what it
        // was just handed.
        await _tokenStorage.DidNotReceive().GetAccessTokenAsync();
        await _tokenStorage.DidNotReceive().GetRefreshTokenAsync();
    }

    [Fact]
    public async Task GetAccessTokenAsync_WithStoredTokenAndColdCache_ReadsStorageExactlyOnce()
    {
        _tokenStorage.GetAccessTokenAsync().Returns(ValidToken());

        using var provider = CreateProvider();

        for (var i = 0; i < 25; i++)
        {
            (await provider.GetAccessTokenAsync(CancellationToken.None)).Should().Be("access-1");
        }

        // Twenty-five requests, one storage read. This is the regression guard for the old chain,
        // which performed a storage round trip and an expiry check for every single request.
        await _tokenStorage.Received(1).GetAccessTokenAsync();
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenExpiringSoon_RefreshesAndCachesTheNewToken()
    {
        // Two minutes left, inside the five-minute lead time, so it counts as spent.
        _tokenStorage.GetAccessTokenAsync().Returns(ValidToken("old-access", expiresInSeconds: 120));
        _tokenStorage.GetRefreshTokenAsync().Returns(RefreshToken.Create("refresh-1"));

        _authenticationService
            .RefreshAccessTokenAsync("refresh-1", Arg.Any<CancellationToken>())
            .Returns(new AuthenticationResultDto
            {
                AccessToken = "new-access",
                RefreshToken = "refresh-2",
                ExpiresInSeconds = 3600
            });

        using var provider = CreateProvider();

        var first = await provider.GetAccessTokenAsync(CancellationToken.None);
        var second = await provider.GetAccessTokenAsync(CancellationToken.None);

        first.Should().Be("new-access");
        second.Should().Be("new-access");

        await _authenticationService.Received(1)
            .RefreshAccessTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        // The refreshed pair is written back, so a later cold start does not fall back to the
        // expired one.
        await _tokenStorage.Received(1).SaveTokensAsync(
            Arg.Is<AccessToken>(t => t.Token == "new-access"),
            Arg.Is<RefreshToken>(t => t.Token == "refresh-2"));
    }

    [Fact]
    public async Task GetAccessTokenAsync_WithConcurrentCallersAndExpiringToken_RefreshesOnlyOnce()
    {
        _tokenStorage.GetAccessTokenAsync().Returns(ValidToken("old-access", expiresInSeconds: 60));
        _tokenStorage.GetRefreshTokenAsync().Returns(RefreshToken.Create("refresh-1"));

        var refreshStarted = new TaskCompletionSource();
        var releaseRefresh = new TaskCompletionSource();

        _authenticationService
            .RefreshAccessTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                refreshStarted.TrySetResult();
                await releaseRefresh.Task;

                return new AuthenticationResultDto
                {
                    AccessToken = "new-access",
                    RefreshToken = "refresh-2",
                    ExpiresInSeconds = 3600
                };
            });

        using var provider = CreateProvider();

        // Start one caller, wait until it is actually inside the refresh, then pile on twenty more.
        // Without the gate around the refresh, each of those would start its own.
        var first = provider.GetAccessTokenAsync(CancellationToken.None);
        await refreshStarted.Task;

        var rest = Enumerable.Range(0, 20)
            .Select(_ => provider.GetAccessTokenAsync(CancellationToken.None))
            .ToList();

        releaseRefresh.SetResult();

        var tokens = await Task.WhenAll(rest.Prepend(first));

        tokens.Should().AllBe("new-access");
        await _authenticationService.Received(1)
            .RefreshAccessTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAccessTokenAsync_WithNoStoredToken_ThrowsSoTheRequestIsNeverSentAnonymously()
    {
        _tokenStorage.GetAccessTokenAsync().Returns((AccessToken?)null);

        using var provider = CreateProvider();

        var act = () => provider.GetAccessTokenAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*authenticate*");
    }

    [Fact]
    public async Task GetAccessTokenAsync_WithExpiredTokenAndNoRefreshToken_ThrowsForReauthentication()
    {
        _tokenStorage.GetAccessTokenAsync().Returns(ValidToken("old", expiresInSeconds: 30));
        _tokenStorage.GetRefreshTokenAsync().Returns((RefreshToken?)null);

        using var provider = CreateProvider();

        var act = () => provider.GetAccessTokenAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*re-authenticate*");
    }

    [Theory]
    [InlineData("", "refresh-2", 3600)]
    [InlineData("new-access", "", 3600)]
    [InlineData("new-access", "refresh-2", 0)]
    [InlineData("new-access", "refresh-2", -5)]
    public async Task GetAccessTokenAsync_WithUnusableRefreshResponse_ThrowsRatherThanCachingIt(
        string accessToken, string refreshToken, int expiresInSeconds)
    {
        _tokenStorage.GetAccessTokenAsync().Returns(ValidToken("old", expiresInSeconds: 30));
        _tokenStorage.GetRefreshTokenAsync().Returns(RefreshToken.Create("refresh-1"));

        _authenticationService
            .RefreshAccessTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AuthenticationResultDto
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresInSeconds = expiresInSeconds
            });

        using var provider = CreateProvider();

        var act = () => provider.GetAccessTokenAsync(CancellationToken.None);

        // Validated rather than cached: an empty or already-expired token would otherwise be stamped
        // onto every subsequent request, which fails as an unexplained 401 far from the cause.
        await act.Should().ThrowAsync<InvalidTokenException>();

        await _tokenStorage.DidNotReceive().SaveTokensAsync(
            Arg.Any<AccessToken>(), Arg.Any<RefreshToken>());
    }

    [Fact]
    public async Task GetCurrentAccessTokenAsync_WithNoToken_ReturnsNullInsteadOfThrowing()
    {
        _tokenStorage.GetAccessTokenAsync().Returns((AccessToken?)null);

        using var provider = CreateProvider();

        // Logout calls this, and must not be derailed by the user not being signed in.
        (await provider.GetCurrentAccessTokenAsync()).Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentAccessTokenAsync_WithExpiringToken_DoesNotTriggerARefresh()
    {
        _tokenStorage.GetAccessTokenAsync().Returns(ValidToken("about-to-expire", expiresInSeconds: 30));

        using var provider = CreateProvider();

        var token = await provider.GetCurrentAccessTokenAsync();

        // Refreshing here would mint a new token only for logout to revoke, leaving the token the
        // session actually used still valid at Autodesk.
        token.Should().Be("about-to-expire");
        await _authenticationService.DidNotReceive()
            .RefreshAccessTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClearTokensAsync_DropsTheCacheTheStoreAndTheProjectMetadata()
    {
        using var provider = CreateProvider();

        await provider.SaveTokensAsync("access-1", "refresh-1", 3600);
        await provider.ClearTokensAsync();

        await _tokenStorage.Received(1).ClearTokensAsync();

        // Project data was read under the outgoing user's credentials, so it goes with them.
        _metadataCache.Received(1).Clear();

        // And the cached token itself is gone - a request after sign-out must not still be served.
        _tokenStorage.GetAccessTokenAsync().Returns((AccessToken?)null);

        var act = () => provider.GetAccessTokenAsync(CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SaveTokensAsync_WithMissingValues_Throws()
    {
        using var provider = CreateProvider();

        await Assert.ThrowsAsync<ArgumentException>(() => provider.SaveTokensAsync("", "refresh", 3600));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.SaveTokensAsync("access", "", 3600));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => provider.SaveTokensAsync("access", "refresh", 0));
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_IsTheSamePathAsGetAccessTokenAsync()
    {
        _tokenStorage.GetAccessTokenAsync().Returns(ValidToken());

        using var provider = CreateProvider();

        // Both interfaces are implemented by one object specifically so they cannot drift apart and
        // end up consulting different caches.
        (await provider.GetValidAccessTokenAsync(CancellationToken.None)).Should().Be("access-1");
        (await provider.GetAccessTokenAsync(CancellationToken.None)).Should().Be("access-1");

        await _tokenStorage.Received(1).GetAccessTokenAsync();
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenRefreshFails_DoesNotCacheAndRetriesOnTheNextCall()
    {
        _tokenStorage.GetAccessTokenAsync().Returns(ValidToken("old", expiresInSeconds: 30));
        _tokenStorage.GetRefreshTokenAsync().Returns(RefreshToken.Create("refresh-1"));

        _authenticationService
            .RefreshAccessTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidTokenException("Autodesk is unreachable"));

        using var provider = CreateProvider();

        await Assert.ThrowsAsync<InvalidTokenException>(
            () => provider.GetAccessTokenAsync(CancellationToken.None));

        // A failed refresh must leave the cache cold rather than poisoned, so a later attempt (once
        // the network is back) tries again instead of failing forever.
        await Assert.ThrowsAsync<InvalidTokenException>(
            () => provider.GetAccessTokenAsync(CancellationToken.None));

        await _authenticationService.Received(2)
            .RefreshAccessTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
