using System.Windows;
using DigitalMIDP.ViewModels;

namespace DigitalMIDP.Views;

/// <summary>
/// Modal wizard for creating a deliverable from a template.
///
/// Minimal code-behind - view initialization, closing on cancel, and closing the instant the user's
/// input is ready. Deliberately does NOT wait for anything to actually be created: the wizard's own
/// steps are fast, but the write itself is not (several sequential API calls per document), so this
/// dialog closes as soon as <see cref="CreateDeliverableViewModel.CreationRequested"/> fires and
/// leaves running the batch entirely to the caller (see
/// <c>MainWindow.NewDeliverableButton_Click</c>), which reads the request back off the same event.
/// </summary>
public partial class CreateDeliverableWindow : Window
{
    public CreateDeliverableWindow(CreateDeliverableViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContext = viewModel;

        // Reported as a successful dialog result, unlike Cancel, so the caller knows a request was
        // actually produced (as opposed to the user backing out) without needing a second signal.
        viewModel.CreationRequested += _ => DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
