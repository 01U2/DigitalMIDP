using System.Globalization;
using System.Windows.Data;
using DigitalMIDP.Application.Dtos;

namespace DigitalMIDP.Converters;

/// <summary>
/// Converter to extract custom attribute values from ItemDto.CustomAttributes dictionary
/// and optionally map them to human-readable descriptions based on available options.
/// 
/// Used by DataGrid columns to display custom attribute values.
/// 
/// Usage: 
///   - Binding="CustomAttributes" with ConverterParameter set to a serialized MetadataOptionDisplayDto array (JSON)
///   - Will look up the attribute value in the options and return the Description if found
///   - Falls back to the raw value if no matching option is found
/// </summary>
public sealed class CustomAttributeValueConverter : IValueConverter
{
    /// <summary>
    /// Deserialize options from JSON string. Expected format: [{"Value":"code","Description":"label"}]
    /// </summary>
    private static IReadOnlyList<MetadataOptionDisplayDto>? DeserializeOptions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            // Simple JSON parsing for options array
            // Format: [{"Value":"code","Description":"label"},...]
            var options = new List<MetadataOptionDisplayDto>();

            // Very basic parsing - assumes well-formed JSON from our own serialization
            // In production, consider using System.Text.Json if adding to project dependencies
            var trimmed = json.Trim();
            if (!trimmed.StartsWith("[") || !trimmed.EndsWith("]"))
            {
                return null;
            }

            var items = trimmed[1..^1].Split(new[] { "},{" }, StringSplitOptions.None);

            foreach (var item in items)
            {
                var cleanItem = item.Replace("{", "").Replace("}", "").Trim();
                if (string.IsNullOrWhiteSpace(cleanItem))
                    continue;

                string? value = null;
                string? description = null;

                // Parse Value property
                var valueMatch = System.Text.RegularExpressions.Regex.Match(cleanItem, @"""Value""\s*:\s*""([^""]*?)""");
                if (valueMatch.Success)
                    value = valueMatch.Groups[1].Value;

                // Parse Description property
                var descMatch = System.Text.RegularExpressions.Regex.Match(cleanItem, @"""Description""\s*:\s*""([^""]*?)""");
                if (descMatch.Success)
                    description = descMatch.Groups[1].Value;

                if (value is not null)
                {
                    options.Add(new MetadataOptionDisplayDto(value, description));
                }
            }

            return options.Count > 0 ? options.AsReadOnly() : null;
        }
        catch
        {
            // If parsing fails, return null and fall back to raw value
            return null;
        }
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // value = CustomAttributes (IReadOnlyDictionary<string, string?> or null)
        // parameter = Serialized MetadataOptionDisplayDto array as JSON, or field name for backward compatibility

        var customAttributes = value as IReadOnlyDictionary<string, string?>;
        var dictionaryAttributes = value as IDictionary<string, string?>;
        var parameterString = parameter as string;

        // If CustomAttributes is null, return empty
        if (customAttributes is null && dictionaryAttributes is null)
        {
            return string.Empty;
        }

        // If we have a dictionary but not readonly, convert it
        if (customAttributes is null && dictionaryAttributes is not null)
        {
            customAttributes = new Dictionary<string, string?>(dictionaryAttributes, StringComparer.OrdinalIgnoreCase);
        }

        if (string.IsNullOrWhiteSpace(parameterString) || customAttributes is null)
        {
            return string.Empty;
        }

        // Split parameter into field name and options
        // Format: "FieldName|[{\"Value\":\"code\",\"Description\":\"label\"}]"
        var parts = parameterString.Split(new[] { '|' }, 2);
        var fieldName = parts[0];
        var optionsJson = parts.Length > 1 ? parts[1] : null;

        if (!customAttributes.TryGetValue(fieldName, out var fieldValue))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(fieldValue))
        {
            return string.Empty;
        }

        // If options are provided, try to look up the description
        if (!string.IsNullOrWhiteSpace(optionsJson))
        {
            var options = DeserializeOptions(optionsJson);
            if (options is not null && options.Count > 0)
            {
                // Find matching option by value (case-insensitive)
                var matchingOption = options.FirstOrDefault(o =>
                    string.Equals(o.Value, fieldValue, StringComparison.OrdinalIgnoreCase));

                if (matchingOption is not null && !string.IsNullOrWhiteSpace(matchingOption.Description))
                {
                    return matchingOption.Description;
                }
            }
        }

        // Fall back to raw value
        return fieldValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("CustomAttributeValueConverter does not support reverse conversion");
    }
}
