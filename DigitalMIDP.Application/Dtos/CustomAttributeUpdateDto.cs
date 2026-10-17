namespace DigitalMIDP.Application.Dtos;

/// <summary>
/// A single custom attribute value change to send to ACC.
/// </summary>
public sealed record CustomAttributeUpdateRequestDto(int AttributeId, string? Value);

/// <summary>
/// A single custom attribute result as confirmed by ACC after an update.
/// </summary>
public sealed record CustomAttributeUpdateResultDto(int AttributeId, string? Name, string? Type, string? Value);
