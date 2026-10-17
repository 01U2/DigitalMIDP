using System.IO;
using DigitalMIDP.Services;
using DigitalMIDP.ViewModels;
using FluentAssertions;
using Xunit;

namespace DigitalMIDP.Tests.UI;

public sealed class ApsClientConfigurationTests : IDisposable
{
    private const string ValidId = "abcdefghij0123456789ABCDEFGHIJ";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "midp-cfg-" + Guid.NewGuid().ToString("N"));

    public ApsClientConfigurationTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "appsettings.json"),
            """{ "Autodesk": { "ClientId": "", "RedirectUri": "http://localhost:8080/callback/" } }""");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string CompanyPath => Path.Combine(_root, "company", "autodesk.json");
    private string UserPath => Path.Combine(_root, "user", "autodesk.json");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("short")]
    [InlineData("abcdefghij0123456789-bad")]
    [InlineData("../../etc/passwd/aaaaaaaaaaaaaa")]
    public void IsValidClientId_RejectsMalformedValues(string? value) =>
        ApsClientConfiguration.IsValidClientId(value).Should().BeFalse();

    [Fact]
    public void IsValidClientId_AcceptsAlphanumericId() =>
        ApsClientConfiguration.IsValidClientId(ValidId).Should().BeTrue();

    [Fact]
    public void Build_WithNoExternalFiles_HasNoValidClientId()
    {
        var config = ApsClientConfiguration.Build(_root, CompanyPath, UserPath);

        ApsClientConfiguration.HasValidClientId(config).Should().BeFalse();
        config["Autodesk:RedirectUri"].Should().Be("http://localhost:8080/callback/");
    }

    [Fact]
    public void Build_UsesCompanyFile_AndUserFileOverridesIt()
    {
        ApsClientConfiguration.SaveClientId(CompanyPath, "COMPANY0123456789ABCDEF");
        ApsClientConfiguration.Build(_root, CompanyPath, UserPath)[ApsClientConfiguration.ClientIdKey]
            .Should().Be("COMPANY0123456789ABCDEF");

        ApsClientConfiguration.SaveClientId(UserPath, ValidId);
        ApsClientConfiguration.Build(_root, CompanyPath, UserPath)[ApsClientConfiguration.ClientIdKey]
            .Should().Be(ValidId);
    }

    [Fact]
    public void SaveClientId_PreservesOtherSettings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(UserPath)!);
        File.WriteAllText(UserPath, """{ "Autodesk": { "Scopes": "data:read" }, "Other": 1 }""");

        ApsClientConfiguration.SaveClientId(UserPath, ValidId);

        var config = ApsClientConfiguration.Build(_root, CompanyPath, UserPath);
        config["Autodesk:Scopes"].Should().Be("data:read");
        config["Other"].Should().Be("1");
        config[ApsClientConfiguration.ClientIdKey].Should().Be(ValidId);
    }

    [Fact]
    public void SaveClientId_RejectsInvalidId()
    {
        var act = () => ApsClientConfiguration.SaveClientId(UserPath, "bad id");

        act.Should().Throw<ArgumentException>();
        File.Exists(UserPath).Should().BeFalse();
    }

    [Fact]
    public void SetupViewModel_SaveWritesFileAndRequestsClose()
    {
        var vm = new ClientIdSetupViewModel(UserPath, "http://localhost:8080/callback/");
        bool? result = null;
        vm.CloseRequested += (_, saved) => result = saved;

        vm.SaveCommand.CanExecute(null).Should().BeFalse();
        vm.ClientId = ValidId;
        vm.SaveCommand.CanExecute(null).Should().BeTrue();
        vm.SaveCommand.Execute(null);

        result.Should().BeTrue();
        File.Exists(UserPath).Should().BeTrue();
    }

    [Fact]
    public void SetupViewModel_InvalidInputShowsError()
    {
        var vm = new ClientIdSetupViewModel(UserPath, "x") { ClientId = "nope" };

        vm.ErrorMessage.Should().NotBeNullOrEmpty();
    }
}
