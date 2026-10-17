using System.Windows;
using DigitalMIDP.ViewModels;

namespace DigitalMIDP.Views;

/// <summary>
/// Modal dialog for editing a single file's ACC custom attribute values.
/// Minimal code-behind - only for view initialization and closing on cancel.
/// Stays open after a successful save so the user can see the success message;
/// the caller listens to ViewModel.AttributesUpdated to refresh without waiting for close.
/// </summary>
public partial class EditCustomAttributesWindow : Window
{
    public EditCustomAttributesWindow(EditCustomAttributesViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContext = viewModel;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
