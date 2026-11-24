using Xunit;

namespace DigitalMIDP.Tests.UI.ViewModels;

public class MainViewModelTests
{
    [Fact]
    public System.Threading.Tasks.Task WindowSelectionRefreshWatchAndLateReadUseOnlyTheSelectedLocalCatalog()
        => ManifestGridScenario.RunAsync();
}
