using FluentAssertions;
using DigitalMIDP.Converters;
using DigitalMIDP.ViewModels;
using System.Globalization;
using Xunit;

namespace DigitalMIDP.Tests.UI.Converters;

public sealed class CustomAttributeValueConverterTests
{
    [Fact]
    public void Convert_WithNullValue_ReturnsEmptyString()
    {
        var converter = new CustomAttributeValueConverter();

        var result = converter.Convert(null, typeof(string), "Category", CultureInfo.InvariantCulture);

        result.Should().Be(string.Empty);
    }

    [Fact]
    public void Convert_WithMissingField_ReturnsEmptyString()
    {
        var converter = new CustomAttributeValueConverter();
        var attributes = new Dictionary<string, string?> { { "Category", "Structural" } };

        var result = converter.Convert(attributes, typeof(string), "Status", CultureInfo.InvariantCulture);

        result.Should().Be(string.Empty);
    }

    [Fact]
    public void Convert_WithMatchingField_ReturnsValue()
    {
        var converter = new CustomAttributeValueConverter();
        var attributes = new Dictionary<string, string?> { { "Category", "Structural" } };

        var result = converter.Convert(attributes, typeof(string), "Category", CultureInfo.InvariantCulture);

        result.Should().Be("Structural");
    }

    [Fact]
    public void Convert_DeliverableTitle_DoesNotFallBackToGenericTitle()
    {
        var converter = new CustomAttributeValueConverter();
        var attributes = new Dictionary<string, string?>
        {
            ["Title"] = "Generic",
            [ColumnKeys.DeliverableTitle] = "Deliverable"
        };

        converter.Convert(attributes, typeof(string), ColumnKeys.DeliverableTitle, CultureInfo.InvariantCulture)
            .Should().Be("Deliverable");
        converter.Convert(attributes, typeof(string), "Title", CultureInfo.InvariantCulture)
            .Should().Be("Generic");
        attributes.Remove(ColumnKeys.DeliverableTitle);
        converter.Convert(attributes, typeof(string), ColumnKeys.DeliverableTitle, CultureInfo.InvariantCulture)
            .Should().Be(string.Empty);
    }

    [Fact]
    public void Convert_WithNullAttributeValue_ReturnsEmptyString()
    {
        var converter = new CustomAttributeValueConverter();
        var attributes = new Dictionary<string, string?> { { "Category", null } };

        var result = converter.Convert(attributes, typeof(string), "Category", CultureInfo.InvariantCulture);

        result.Should().Be(string.Empty);
    }

    [Fact]
    public void Convert_WithNullParameter_ReturnsEmptyString()
    {
        var converter = new CustomAttributeValueConverter();
        var attributes = new Dictionary<string, string?> { { "Category", "Structural" } };

        var result = converter.Convert(attributes, typeof(string), null, CultureInfo.InvariantCulture);

        result.Should().Be(string.Empty);
    }

    [Fact]
    public void Convert_WithReadOnlyDictionary_ReturnsValue()
    {
        var converter = new CustomAttributeValueConverter();
        var attributes = new Dictionary<string, string?> { { "Category", "Structural" } }.AsReadOnly();

        var result = converter.Convert(attributes, typeof(string), "Category", CultureInfo.InvariantCulture);

        result.Should().Be("Structural");
    }

    /// <summary>
    /// Tests option description lookup - the new feature.
    /// When options are provided and a value matches an option, the description should be returned.
    /// </summary>
    [Fact]
    public void Convert_WithMatchingOptionCode_ReturnsOptionDescription()
    {
        var converter = new CustomAttributeValueConverter();
        var attributes = new Dictionary<string, string?> { { "Status", "001" } };

        // Parameter format: "FieldName|[{"Value":"code","Description":"label"}]"
        var parameter = @"Status|[{""Value"":""001"",""Description"":""In Progress""},{""Value"":""002"",""Description"":""Complete""}]";

        var result = converter.Convert(attributes, typeof(string), parameter, CultureInfo.InvariantCulture);

        result.Should().Be("In Progress");
    }

    [Fact]
    public void Convert_WithNonMatchingOptionCode_ReturnsFallbackValue()
    {
        var converter = new CustomAttributeValueConverter();
        var attributes = new Dictionary<string, string?> { { "Status", "999" } };

        // Value "999" doesn't match any option, should fall back to raw value
        var parameter = @"Status|[{""Value"":""001"",""Description"":""In Progress""},{""Value"":""002"",""Description"":""Complete""}]";

        var result = converter.Convert(attributes, typeof(string), parameter, CultureInfo.InvariantCulture);

        result.Should().Be("999");
    }

    [Fact]
    public void Convert_WithCaseInsensitiveOptionMatch_ReturnsDescription()
    {
        var converter = new CustomAttributeValueConverter();
        var attributes = new Dictionary<string, string?> { { "Status", "DRAFT" } };

        // Should match case-insensitively
        var parameter = @"Status|[{""Value"":""draft"",""Description"":""Draft Document""},{""Value"":""final"",""Description"":""Final Document""}]";

        var result = converter.Convert(attributes, typeof(string), parameter, CultureInfo.InvariantCulture);

        result.Should().Be("Draft Document");
    }

    [Fact]
    public void Convert_WithEmptyOptionsJson_ReturnsFallbackValue()
    {
        var converter = new CustomAttributeValueConverter();
        var attributes = new Dictionary<string, string?> { { "Status", "001" } };

        // Empty options array - should fall back to raw value
        var parameter = @"Status|[]";

        var result = converter.Convert(attributes, typeof(string), parameter, CultureInfo.InvariantCulture);

        result.Should().Be("001");
    }

    [Fact]
    public void Convert_WithSpecialCharactersInDescription_ReturnsCorrectValue()
    {
        var converter = new CustomAttributeValueConverter();
        var attributes = new Dictionary<string, string?> { { "Status", "001" } };

        // Test with special characters in description
        var parameter = @"Status|[{""Value"":""001"",""Description"":""In Progress (Phase 1)""}]";

        var result = converter.Convert(attributes, typeof(string), parameter, CultureInfo.InvariantCulture);

        result.Should().Be("In Progress (Phase 1)");
    }

    [Fact]
    public void Convert_WithNullDescription_ReturnsFallbackValue()
    {
        var converter = new CustomAttributeValueConverter();
        var attributes = new Dictionary<string, string?> { { "Status", "001" } };

        // Option exists but description is empty - should fall back to raw value
        var parameter = @"Status|[{""Value"":""001"",""Description"":""""}]";

        var result = converter.Convert(attributes, typeof(string), parameter, CultureInfo.InvariantCulture);

        result.Should().Be("001");
    }

    [Fact]
    public void ConvertBack_ThrowsNotSupportedException()
    {
        var converter = new CustomAttributeValueConverter();

        var action = () => converter.ConvertBack("value", typeof(string), null, CultureInfo.InvariantCulture);

        action.Should().Throw<NotSupportedException>();
    }
}

