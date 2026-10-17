namespace DigitalMIDP.Application.Dtos;

/// <summary>
/// A template file available in the project's template repository.
///
/// A template is an ordinary ACC file - there is no template object type in the API. "Creating from
/// a template" means copying this file's bytes into a new item under a new name, so the app never
/// interprets the contents; only the extension matters, because the created deliverable inherits it.
/// </summary>
/// <param name="ItemId">The template item's id, used to fetch its versions and download its content.</param>
/// <param name="Name">The template's filename as stored in ACC, e.g. "Report Template.docx".</param>
/// <param name="ItemUrn">The item URN, where available.</param>
/// <param name="Extension">The file extension including the leading dot, e.g. ".docx". Empty when the template has none.</param>
/// <param name="Size">Size in bytes, where ACC reported one.</param>
/// <param name="ModifiedAt">Last modification timestamp, where ACC reported one.</param>
public sealed record DeliverableTemplateDto(
    string ItemId,
    string Name,
    string? ItemUrn,
    string Extension,
    long? Size,
    DateTime? ModifiedAt
)
{
    /// <summary>
    /// The template name without its extension, e.g. "Report Template". Used to present templates in
    /// the selection step without repeating the file type in the label.
    /// </summary>
    public string DisplayName =>
        string.IsNullOrEmpty(Extension) || !Name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)
            ? Name
            : Name[..^Extension.Length];
}
