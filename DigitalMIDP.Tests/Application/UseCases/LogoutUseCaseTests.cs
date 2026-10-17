using DigitalMIDP.Application.Interfaces;
using DigitalMIDP.Application.UseCases;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace DigitalMIDP.Tests.Application.UseCases;

/// <summary>
/// Tests for <see cref="LogoutUseCase"/>.
///
/// <para>
/// The behaviour being pinned down is that logout revokes the token at Autodesk - which the
/// application previously never did, despite having the method for it - and that it clears the local
/// tokens no matter what happens to that revocation.
/// </para>
/// </summary>
public sealed class LogoutUseCaseTests
{
    private readonly IAuthenticationService _authenticationService = Substitute.For<IAuthenticationService>();
    private readonly ITokenStorageService _tokenStorageService = Substitute.For<ITokenStorageService>();

    private LogoutUseCase CreateUseCase() => new(
        _authenticationService,
        _tokenStorageService,
        Substitute.For<ILogger<LogoutUseCase>>());

    [Fact]
    public async Task ExecuteAsync_WithASignedInUser_RevokesThenClears()
    {
        _tokenStorageService.GetCurrentAccessTokenAsync().Returns("access-1");

        var revoked = await CreateUseCase().ExecuteAsync(CancellationToken.None);

        revoked.Should().BeTrue();

        await _authenticationService.Received(1).RevokeAsync("access-1", Arg.Any<CancellationToken>());
        await _tokenStorageService.Received(1).ClearTokensAsync();

        // Order matters: clearing first would leave nothing to revoke, which is exactly how the
        // refresh token used to stay valid at Autodesk for its full lifetime after a "logout".
        Received.InOrder(() =>
        {
            _authenticationService.RevokeAsync("access-1", Arg.Any<CancellationToken>());
            _tokenStorageService.ClearTokensAsync();
        });
    }

    [Fact]
    public async Task ExecuteAsync_ReadsTheTokenWithoutForcingARefresh()
    {
        _tokenStorageService.GetCurrentAccessTokenAsync().Returns("access-1");

        await CreateUseCase().ExecuteAsync(CancellationToken.None);

        // GetValidAccessTokenAsync would mint a fresh token on the way out, revoke that one, and
        // leave the token the session had actually been using alive.
        await _tokenStorageService.Received(1).GetCurrentAccessTokenAsync();
        await _tokenStorageService.DidNotReceive().GetValidAccessTokenAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenRevocationFails_StillClearsTheLocalTokens()
    {
        _tokenStorageService.GetCurrentAccessTokenAsync().Returns("access-1");
        _authenticationService
            .RevokeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("network down"));

        var revoked = await CreateUseCase().ExecuteAsync(CancellationToken.None);

        // Reported honestly - the session may still be live at Autodesk - but the local credentials
        // are gone regardless. Failing the logout here would leave the application holding a live
        // token while telling the user it had signed them out.
        revoked.Should().BeFalse();
        await _tokenStorageService.Received(1).ClearTokensAsync();
    }

    [Fact]
    public async Task ExecuteAsync_WithNoTokenHeld_ClearsWithoutCallingAutodesk()
    {
        _tokenStorageService.GetCurrentAccessTokenAsync().Returns((string?)null);

        var revoked = await CreateUseCase().ExecuteAsync(CancellationToken.None);

        revoked.Should().BeFalse();
        await _authenticationService.DidNotReceive()
            .RevokeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        // Still cleared: logging out when not signed in is a no-op the user should never see fail.
        await _tokenStorageService.Received(1).ClearTokensAsync();
    }

    [Fact]
    public async Task ExecuteAsync_WithABlankToken_TreatsItAsNoTokenAtAll()
    {
        _tokenStorageService.GetCurrentAccessTokenAsync().Returns("   ");

        var revoked = await CreateUseCase().ExecuteAsync(CancellationToken.None);

        revoked.Should().BeFalse();
        await _authenticationService.DidNotReceive()
            .RevokeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _tokenStorageService.Received(1).ClearTokensAsync();
    }

    [Fact]
    public async Task ExecuteAsync_WhenRevocationIsCancelled_StillClears()
    {
        _tokenStorageService.GetCurrentAccessTokenAsync().Returns("access-1");
        _authenticationService
            .RevokeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var revoked = await CreateUseCase().ExecuteAsync(cts.Token);

        // Cancellation is not special-cased: the local tokens must go either way, so the method
        // completes rather than propagating the cancellation and skipping the clear.
        revoked.Should().BeFalse();
        await _tokenStorageService.Received(1).ClearTokensAsync();
    }

    [Fact]
    public void Constructor_WithMissingDependencies_Throws()
    {
        var logger = Substitute.For<ILogger<LogoutUseCase>>();

        Assert.Throws<ArgumentNullException>(() => new LogoutUseCase(null!, _tokenStorageService, logger));
        Assert.Throws<ArgumentNullException>(() => new LogoutUseCase(_authenticationService, null!, logger));
        Assert.Throws<ArgumentNullException>(() => new LogoutUseCase(_authenticationService, _tokenStorageService, null!));
    }
}
