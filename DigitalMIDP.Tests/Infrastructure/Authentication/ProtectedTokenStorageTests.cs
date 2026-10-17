using System.Text;
using DigitalMIDP.Domain.ValueObjects;
using DigitalMIDP.Infrastructure.Authentication;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace DigitalMIDP.Tests.Infrastructure.Authentication;

/// <summary>
/// Tests for <see cref="ProtectedTokenStorage"/>.
///
/// <para>
/// The property that matters is that the store never holds a token as readable text between uses -
/// that is the whole point of protecting it, since a .NET string cannot be zeroed and shows up
/// verbatim in a crash dump or page file. These tests use a reversible stand-in protector so the
/// store's own logic can be checked without depending on a real Windows user profile;
/// <see cref="DpapiTokenProtectorTests"/> covers the real DPAPI round trip.
/// </para>
/// </summary>
public sealed class ProtectedTokenStorageTests
{
    private readonly RecordingProtector _protector = new();

    private ProtectedTokenStorage CreateStorage() =>
        new(_protector, Substitute.For<ILogger<ProtectedTokenStorage>>());

    [Fact]
    public async Task SaveTokensAsync_ThenGet_RoundTripsBothTokens()
    {
        using var storage = CreateStorage();
        var accessToken = AccessToken.Create("access-1", 3600);

        await storage.SaveTokensAsync(accessToken, RefreshToken.Create("refresh-1"));

        var storedAccess = await storage.GetAccessTokenAsync();
        var storedRefresh = await storage.GetRefreshTokenAsync();

        storedAccess!.Token.Should().Be("access-1");
        storedAccess.ExpiresAt.Should().BeCloseTo(accessToken.ExpiresAt, TimeSpan.FromSeconds(1));
        storedRefresh!.Token.Should().Be("refresh-1");
    }

    [Fact]
    public async Task SaveTokensAsync_PutsBothTokensThroughTheProtector()
    {
        using var storage = CreateStorage();

        await storage.SaveTokensAsync(AccessToken.Create("access-1", 3600), RefreshToken.Create("refresh-1"));

        // Neither token reaches a field as plaintext. The refresh token is the one that really
        // matters - it is long-lived and on its own enough to mint new access tokens.
        _protector.Protected.Should().BeEquivalentTo(new[] { "access-1", "refresh-1" });
    }

    [Fact]
    public async Task GetRefreshTokenAsync_DecryptsOnlyWhenAsked()
    {
        using var storage = CreateStorage();

        await storage.SaveTokensAsync(AccessToken.Create("access-1", 3600), RefreshToken.Create("refresh-1"));
        _protector.UnprotectCalls.Should().Be(0, "saving should not need to decrypt anything");

        await storage.GetRefreshTokenAsync();

        // Decrypted for the moment a refresh needs it, not held decrypted between refreshes.
        _protector.UnprotectCalls.Should().Be(1);
    }

    [Fact]
    public async Task GetAccessTokenAsync_WithNothingStored_ReturnsNull()
    {
        using var storage = CreateStorage();

        (await storage.GetAccessTokenAsync()).Should().BeNull();
        (await storage.GetRefreshTokenAsync()).Should().BeNull();
    }

    [Fact]
    public async Task ClearTokensAsync_RemovesBothTokens()
    {
        using var storage = CreateStorage();

        await storage.SaveTokensAsync(AccessToken.Create("access-1", 3600), RefreshToken.Create("refresh-1"));
        await storage.ClearTokensAsync();

        (await storage.GetAccessTokenAsync()).Should().BeNull();
        (await storage.GetRefreshTokenAsync()).Should().BeNull();
    }

    [Fact]
    public async Task ClearTokensAsync_ZeroesTheCiphertextRatherThanJustDroppingTheReference()
    {
        using var storage = CreateStorage();

        await storage.SaveTokensAsync(AccessToken.Create("access-1", 3600), RefreshToken.Create("refresh-1"));

        // The buffers the store actually holds, captured as the protector handed them over.
        var buffers = _protector.IssuedBuffers.ToList();
        buffers.Should().HaveCount(2);
        buffers.Should().OnlyContain(b => b.Any(x => x != 0));

        await storage.ClearTokensAsync();

        // Overwritten, not merely abandoned - otherwise a cleared token lingers in its heap block
        // until the GC happens to reuse it, which is precisely what a dump would capture.
        buffers.Should().OnlyContain(b => b.All(x => x == 0));
    }

    [Fact]
    public async Task SaveTokensAsync_WhenCalledAgain_ZeroesThePreviousCiphertext()
    {
        using var storage = CreateStorage();

        await storage.SaveTokensAsync(AccessToken.Create("access-1", 3600), RefreshToken.Create("refresh-1"));
        var firstBuffers = _protector.IssuedBuffers.ToList();

        await storage.SaveTokensAsync(AccessToken.Create("access-2", 3600), RefreshToken.Create("refresh-2"));

        // A token refresh replaces the pair several times over a long session; each superseded copy
        // has to go, not accumulate.
        firstBuffers.Should().OnlyContain(b => b.All(x => x == 0));
        (await storage.GetRefreshTokenAsync())!.Token.Should().Be("refresh-2");
    }

    [Fact]
    public async Task Dispose_ClearsTheTokens()
    {
        var storage = CreateStorage();

        await storage.SaveTokensAsync(AccessToken.Create("access-1", 3600), RefreshToken.Create("refresh-1"));
        var buffers = _protector.IssuedBuffers.ToList();

        storage.Dispose();

        buffers.Should().OnlyContain(b => b.All(x => x == 0));
    }

    [Fact]
    public async Task GetRefreshTokenAsync_WhenDecryptionFails_ReportsNoTokenRatherThanThrowing()
    {
        using var storage = CreateStorage();

        await storage.SaveTokensAsync(AccessToken.Create("access-1", 3600), RefreshToken.Create("refresh-1"));

        // What a different Windows user, or a migrated profile, looks like: the ciphertext is there
        // but cannot be decrypted. There is nothing to recover, and the caller treats null as "not
        // signed in" and sends the user back through the browser.
        _protector.FailUnprotect = true;

        (await storage.GetRefreshTokenAsync()).Should().BeNull();
        (await storage.GetAccessTokenAsync()).Should().BeNull();
    }

    [Fact]
    public async Task SaveTokensAsync_WithNullArguments_Throws()
    {
        using var storage = CreateStorage();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => storage.SaveTokensAsync(null!, RefreshToken.Create("refresh-1")));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => storage.SaveTokensAsync(AccessToken.Create("access-1", 3600), null!));
    }

    /// <summary>
    /// A reversible stand-in for DPAPI that records what it was asked to protect and hands back the
    /// exact buffers the store keeps, so a test can inspect them afterwards.
    /// </summary>
    private sealed class RecordingProtector : ITokenProtector
    {
        public List<string> Protected { get; } = [];

        public List<byte[]> IssuedBuffers { get; } = [];

        public int UnprotectCalls { get; private set; }

        public bool FailUnprotect { get; set; }

        public byte[] Protect(string token)
        {
            Protected.Add(token);

            // Trivially reversible, and distinct from the plaintext so a test cannot pass by
            // accident if the store forgot to protect at all.
            var buffer = Encoding.UTF8.GetBytes(token).Select(b => (byte)(b ^ 0x5A)).ToArray();
            IssuedBuffers.Add(buffer);

            return buffer;
        }

        public string? Unprotect(byte[] protectedToken)
        {
            UnprotectCalls++;

            if (FailUnprotect)
            {
                return null;
            }

            return Encoding.UTF8.GetString(protectedToken.Select(b => (byte)(b ^ 0x5A)).ToArray());
        }
    }
}

/// <summary>
/// Round-trip test for the real Windows DPAPI protector.
/// </summary>
public sealed class DpapiTokenProtectorTests
{
    [Fact]
    public void ProtectThenUnprotect_RoundTripsTheToken()
    {
        if (!OperatingSystem.IsWindows())
        {
            // The production registration only selects this protector on Windows.
            return;
        }

        var protector = new DpapiTokenProtector(Substitute.For<ILogger<DpapiTokenProtector>>());
        const string token = "a-refresh-token-value.with.dots-and-dashes_123";

        var ciphertext = protector.Protect(token);

        // Not merely transformed - actually unreadable, and longer than the input because DPAPI adds
        // its own header and integrity data.
        Encoding.UTF8.GetString(ciphertext).Should().NotContain(token);
        protector.Unprotect(ciphertext).Should().Be(token);
    }

    [Fact]
    public void Unprotect_WithBytesThatAreNotDpapiOutput_ReturnsNull()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var protector = new DpapiTokenProtector(Substitute.For<ILogger<DpapiTokenProtector>>());

        // Must not throw: the caller treats null as "no usable token held".
        protector.Unprotect(Encoding.UTF8.GetBytes("not encrypted")).Should().BeNull();
        protector.Unprotect([]).Should().BeNull();
    }

    [Fact]
    public void Protect_WithBlankToken_Throws()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var protector = new DpapiTokenProtector(Substitute.For<ILogger<DpapiTokenProtector>>());

        Assert.Throws<ArgumentException>(() => protector.Protect(""));
        Assert.Throws<ArgumentException>(() => protector.Protect("   "));
    }
}
