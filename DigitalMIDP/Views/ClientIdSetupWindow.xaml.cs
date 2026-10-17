using System.Windows;
using DigitalMIDP.ViewModels;

namespace DigitalMIDP.Views;

/// <summary>First-run window for entering the company's APS Client ID.</summary>
public partial class ClientIdSetupWindow : Window
{
    private readonly ClientIdSetupViewModel _viewModel;

    public ClientIdSetupWindow(ClientIdSetupViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;
        _viewModel.CloseRequested += OnCloseRequested;
        Closed += (_, _) => _viewModel.CloseRequested -= OnCloseRequested;
    }

    private void OnCloseRequested(object? sender, bool saved) => DialogResult = saved;
}
