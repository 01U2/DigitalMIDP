using System.IO;
using DigitalMIDP.Infrastructure.Logging;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DigitalMIDP.Tests.Infrastructure.Logging;

/// <summary>
/// Tests for <see cref="FileLoggerProvider"/>, the on-disk log that support reads from a user's
/// machine. Each test writes into its own temporary folder.
/// </summary>
public sealed class FileLoggerProviderTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "DigitalMIDP.Tests.Logs", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Log_WritesTheEntryWithLevelCategoryAndException()
    {
        string path;
        using (var provider = new FileLoggerProvider(_directory))
        {
            var logger = provider.CreateLogger("DigitalMIDP.Sample");
            logger.LogWarning(new InvalidOperationException("boom"), "Something happened for {ProjectId}", "b.p1");
            path = provider.CurrentFilePath;
        }

        var text = File.ReadAllText(path);
        text.Should().Contain("[WRN]")
            .And.Contain("DigitalMIDP.Sample: Something happened for b.p1")
            .And.Contain("System.InvalidOperationException: boom");
    }

    [Fact]
    public void Log_BelowTheMinimumLevel_IsNotWritten()
    {
        string path;
        using (var provider = new FileLoggerProvider(_directory, LogLevel.Information))
        {
            var logger = provider.CreateLogger("Cat");
            logger.LogDebug("debug detail");
            logger.LogInformation("kept");
            path = provider.CurrentFilePath;
        }

        var text = File.ReadAllText(path);
        text.Should().Contain("kept").And.NotContain("debug detail");
    }

    [Theory]
    [InlineData("Authorization: Bearer eyJhbGciOi.abc-def_123", "eyJhbGciOi")]
    [InlineData("{\"access_token\":\"secret-value\",\"expires_in\":3599}", "secret-value")]
    [InlineData("grant_type=refresh_token&refresh_token=rt-secret&client_id=x", "rt-secret")]
    [InlineData("GET http://localhost:8080/callback/?code=auth-code-123&state=s", "auth-code-123")]
    [InlineData("PUT https://bucket.s3.amazonaws.com/obj?X-Amz-Credential=ASIAKEY%2F20261021&X-Amz-Signature=abc123def&X-Amz-Date=1", "abc123def")]
    [InlineData("PUT https://bucket.s3.amazonaws.com/obj?X-Amz-Security-Token=FwoGZXIvYXdzEJr&X-Amz-Date=1", "FwoGZXIvYXdzEJr")]
    [InlineData("S3 upload failed with status 403: <Error><Code>SignatureDoesNotMatch</Code><AWSAccessKeyId>ASIAEXAMPLEKEY</AWSAccessKeyId><SignatureProvided>sig-xyz</SignatureProvided></Error>", "sig-xyz")]
    [InlineData("<Error><AWSAccessKeyId>ASIAEXAMPLEKEY</AWSAccessKeyId></Error>", "ASIAEXAMPLEKEY")]
    public void Redact_MasksCredentials(string input, string secret)
    {
        var redacted = FileLoggerProvider.Redact(input);

        redacted.Should().NotContain(secret).And.Contain("***");
    }

    [Fact]
    public void Redact_KeepsTheS3ErrorCodeReadable()
    {
        var redacted = FileLoggerProvider.Redact(
            "<Error><Code>SignatureDoesNotMatch</Code><SignatureProvided>sig-xyz</SignatureProvided></Error>");

        // The error code is what support needs; only the signature is masked, and the XML stays intact.
        redacted.Should().Be(
            "<Error><Code>SignatureDoesNotMatch</Code><SignatureProvided>***</SignatureProvided></Error>");
    }

    [Fact]
    public void Redact_LeavesOrdinaryMessagesAlone()
    {
        const string message = "Fetching items for folder urn:adsk.wipprod:fs.folder:co.abc in project b.p1";

        FileLoggerProvider.Redact(message).Should().Be(message);
    }

    [Fact]
    public void Constructor_PurgesLogFilesOlderThanTheRetentionWindow()
    {
        Directory.CreateDirectory(_directory);
        var oldFile = Path.Combine(_directory, "DigitalMIDP-20000101.log");
        var recentFile = Path.Combine(_directory, "DigitalMIDP-recent.log");
        File.WriteAllText(oldFile, "old");
        File.WriteAllText(recentFile, "recent");
        File.SetLastWriteTime(oldFile, DateTime.Now.AddDays(-30));

        using var provider = new FileLoggerProvider(_directory, retentionDays: 14);

        File.Exists(oldFile).Should().BeFalse();
        File.Exists(recentFile).Should().BeTrue();
    }

    [Fact]
    public void Log_AfterDispose_DoesNotThrow()
    {
        var provider = new FileLoggerProvider(_directory);
        var logger = provider.CreateLogger("Cat");
        provider.Dispose();

        var act = () => logger.LogError("late message");

        act.Should().NotThrow();
    }
}
