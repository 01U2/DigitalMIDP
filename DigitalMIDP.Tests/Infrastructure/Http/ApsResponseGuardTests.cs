using System.Net;
using DigitalMIDP.Infrastructure.Http;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace DigitalMIDP.Tests.Infrastructure.Http;

/// <summary>
/// Tests for <see cref="ApsResponseGuard"/>, the shared response validation and safe-logging
/// helpers.
/// </summary>
public sealed class ApsResponseGuardTests
{
    private sealed record Payload(string Id);

    [Fact]
    public void EnsureDeserialized_WithAValue_ReturnsIt()
    {
        var payload = new Payload("a");

        ApsResponseGuard.EnsureDeserialized(payload, "hubs").Should().BeSameAs(payload);
    }

    [Fact]
    public void EnsureDeserialized_WithNull_ThrowsNamingTheOperation()
    {
        // An empty body, or a literal "null", deserialises to null. Previously that surfaced as a
        // NullReferenceException from wherever the result was first dereferenced, which told nobody
        // which call had failed.
        var act = () => ApsResponseGuard.EnsureDeserialized<Payload>(null, "hubs page 0");

        act.Should().Throw<DigitalMIDP.Application.Exceptions.ApplicationException>()
            .WithMessage("*hubs page 0*");
    }

    [Fact]
    public void TruncateForLog_WithAShortBody_ReturnsItUnchanged()
    {
        ApsResponseGuard.TruncateForLog("{\"a\":1}").Should().Be("{\"a\":1}");
    }

    [Fact]
    public void TruncateForLog_WithNoBody_SaysSo()
    {
        ApsResponseGuard.TruncateForLog(null).Should().Be("(empty)");
        ApsResponseGuard.TruncateForLog("").Should().Be("(empty)");
    }

    [Fact]
    public void TruncateForLog_WithALongBody_TrimsItAndSaysHowMuchWasDropped()
    {
        var body = new string('x', 5000);

        var logged = ApsResponseGuard.TruncateForLog(body);

        // Trimmed, so a failing response body cannot dump a whole page of project data into the log,
        // and labelled, so a truncated body is never mistaken for a complete one.
        logged.Length.Should().BeLessThan(600);
        logged.Should().Contain("5000 chars total");
    }

    [Fact]
    public void Describe_ReportsTheStatusAndSizeButNotTheContent()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);

        var described = ApsResponseGuard.Describe(response, "{\"files\":[\"Secret-Project.rvt\"]}");

        // This is what replaced logging whole bodies at Information. A response body carries every
        // file name, folder path, attribute value and user name in the project.
        described.Should().Contain("200");
        described.Should().Contain("chars");
        described.Should().NotContain("Secret-Project");
    }

    [Fact]
    public void Describe_WithNoBody_ReportsZeroLength()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NotFound);

        ApsResponseGuard.Describe(response, null).Should().Contain("0 chars");
    }

    [Fact]
    public void KeepValid_WithAllEntriesValid_KeepsThemAndLogsNothing()
    {
        var logger = Substitute.For<ILogger>();
        var items = new[] { new Payload("a"), new Payload("b") };

        var kept = ApsResponseGuard.KeepValid(
            items, p => !string.IsNullOrWhiteSpace(p.Id), logger, "hubs", "page 0");

        kept.Should().HaveCount(2);
        logger.DidNotReceiveWithAnyArgs().Log<object>(default, default, default!, default, default!);
    }

    [Fact]
    public void KeepValid_WithInvalidEntries_DropsThemAndWarnsOnce()
    {
        var logger = Substitute.For<ILogger>();
        var items = new[] { new Payload("a"), new Payload(""), new Payload("   ") };

        var kept = ApsResponseGuard.KeepValid(
            items, p => !string.IsNullOrWhiteSpace(p.Id), logger, "hubs", "page 0");

        // Dropped, because an entry with no id cannot be fetched, edited or correlated with
        // anything - but warned about, because dropping silently is how data goes missing without
        // anyone noticing.
        kept.Should().ContainSingle().Which.Id.Should().Be("a");

        logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public void KeepValid_WithAnEmptySource_ReturnsEmptyAndLogsNothing()
    {
        var logger = Substitute.For<ILogger>();

        var kept = ApsResponseGuard.KeepValid(
            Array.Empty<Payload>(), _ => true, logger, "hubs", "page 0");

        kept.Should().BeEmpty();
        logger.DidNotReceiveWithAnyArgs().Log<object>(default, default, default!, default, default!);
    }

    [Fact]
    public void KeepValid_EnumeratesTheSourceOnlyOnce()
    {
        var logger = Substitute.For<ILogger>();
        var enumerations = 0;

        IEnumerable<Payload> Source()
        {
            enumerations++;
            yield return new Payload("a");
            yield return new Payload("");
        }

        var kept = ApsResponseGuard.KeepValid(
            Source(), p => !string.IsNullOrWhiteSpace(p.Id), logger, "hubs", "page 0");

        // The callers pass deserialised collections, but a lazy sequence must not be walked twice -
        // for a deferred source that could mean issuing the underlying request again.
        kept.Should().HaveCount(1);
        enumerations.Should().Be(1);
    }
}
