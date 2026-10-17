using FluentAssertions;
using DigitalMIDP.Application.UseCases;
using Xunit;

namespace DigitalMIDP.Tests.Application.UseCases;

/// <summary>
/// Unit tests for DocumentNumberSegmentValidator, which guards a document-number segment value
/// against breaking the delimited number structure or producing a filesystem-illegal file name.
/// </summary>
public sealed class DocumentNumberSegmentValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithNoValue_ReturnsNull(string? value)
    {
        DocumentNumberSegmentValidator.Validate("Role", value, "-").Should().BeNull();
    }

    [Fact]
    public void Validate_WithAcceptableValue_ReturnsNull()
    {
        DocumentNumberSegmentValidator.Validate("Role", "AR", "-").Should().BeNull();
    }

    [Fact]
    public void Validate_WithValueContainingSpace_ReturnsSpaceError()
    {
        var result = DocumentNumberSegmentValidator.Validate("Role", "A R", "-");

        result.Should().Be("'Role' can't contain spaces. Try 'AR'.");
    }

    [Fact]
    public void Validate_WithValueContainingDelimiter_ReturnsDelimiterError()
    {
        var result = DocumentNumberSegmentValidator.Validate("Role", "A-R", "-");

        result.Should().Be("'Role' can't contain '-'.");
    }

    [Theory]
    [InlineData('\\')]
    [InlineData('/')]
    [InlineData(':')]
    [InlineData('*')]
    [InlineData('?')]
    [InlineData('"')]
    [InlineData('<')]
    [InlineData('>')]
    [InlineData('|')]
    public void Validate_WithReservedFileNameCharacter_ReturnsCharacterError(char reserved)
    {
        var value = $"A{reserved}R";

        var result = DocumentNumberSegmentValidator.Validate("Role", value, "-");

        result.Should().Be($"'Role' can't contain '{reserved}'.");
    }

    [Fact]
    public void Validate_WithValueShorterThanMinLength_ReturnsMinLengthError()
    {
        var result = DocumentNumberSegmentValidator.Validate("Project", "P1", "-", minLength: 3);

        result.Should().Be("'Project' must be at least 3 characters.");
    }

    [Fact]
    public void Validate_WithValueLongerThanMaxLength_ReturnsMaxLengthError()
    {
        var result = DocumentNumberSegmentValidator.Validate("Project", "P0001", "-", maxLength: 3);

        result.Should().Be("'Project' must be at most 3 characters.");
    }

    [Fact]
    public void Validate_WithFixedLength_ReturnsExactLengthErrorWording()
    {
        var tooShort = DocumentNumberSegmentValidator.Validate("Project", "P1", "-", minLength: 3, maxLength: 3);
        var tooLong = DocumentNumberSegmentValidator.Validate("Project", "P0001", "-", minLength: 3, maxLength: 3);

        tooShort.Should().Be("'Project' must be 3 characters.");
        tooLong.Should().Be("'Project' must be 3 characters.");
    }

    [Fact]
    public void Validate_WithValueWithinLengthBounds_ReturnsNull()
    {
        DocumentNumberSegmentValidator.Validate("Project", "P01", "-", minLength: 2, maxLength: 4).Should().BeNull();
    }

    [Fact]
    public void Validate_LengthChecksTakePriorityOverCharacterChecks()
    {
        // ACC's own configured length rule is checked before this app's filename-safety rules.
        var result = DocumentNumberSegmentValidator.Validate("Project", "P", "-", minLength: 3);

        result.Should().Be("'Project' must be at least 3 characters.");
    }

    [Fact]
    public void Validate_TrimsValueBeforeCheckingLength()
    {
        DocumentNumberSegmentValidator.Validate("Project", "  P01  ", "-", minLength: 3, maxLength: 3).Should().BeNull();
    }
}
