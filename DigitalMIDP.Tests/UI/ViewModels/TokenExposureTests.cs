using System.Reflection;
using DigitalMIDP.ViewModels;
using FluentAssertions;
using Xunit;

namespace DigitalMIDP.Tests.UI.ViewModels;

/// <summary>
/// Guards that OAuth tokens stay out of the presentation layer.
///
/// <para>
/// Tokens are held only by the DPAPI-protected token store and attached to requests by
/// <c>ApsAuthorizationHandler</c>. The ViewModels used to carry the raw access and refresh tokens on
/// bindable properties for the whole session; these tests stop that from quietly coming back.
/// </para>
/// </summary>
public sealed class TokenExposureTests
{
    [Fact]
    public void ViewModels_ExposeNoTokenValues()
    {
        var offending = typeof(MainViewModel).Assembly
            .GetTypes()
            .Where(t => t.Namespace == typeof(MainViewModel).Namespace)
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Concat(t.GetProperties(BindingFlags.NonPublic | BindingFlags.Instance))
                .Where(p => p.PropertyType == typeof(string) && IsTokenName(p.Name))
                .Select(p => $"{t.Name}.{p.Name}"))
            .ToList();

        offending.Should().BeEmpty();
    }

    private static bool IsTokenName(string name) =>
        name.Contains("AccessToken", StringComparison.OrdinalIgnoreCase)
        || name.Contains("RefreshToken", StringComparison.OrdinalIgnoreCase);
}
