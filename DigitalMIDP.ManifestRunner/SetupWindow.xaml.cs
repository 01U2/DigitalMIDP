using System.Windows;
using DigitalMIDP.ViewModels;

namespace DigitalMIDP.ManifestRunner;

public partial class SetupWindow : Window
{
    public SetupWindow(ClientIdSetupViewModel setup)
    {
        InitializeComponent();
        DataContext = setup;
        setup.CloseRequested += (_, saved) => DialogResult = saved;
    }
}
