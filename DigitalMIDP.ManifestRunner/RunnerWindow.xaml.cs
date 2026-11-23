using System.ComponentModel;
using System.Windows;
using DigitalMIDP.ManifestRunner.ViewModels;

namespace DigitalMIDP.ManifestRunner;

public partial class RunnerWindow : Window
{
    private bool closing;
    private bool drained;

    public RunnerWindow(RunnerViewModel model)
    {
        InitializeComponent();
        DataContext = model;
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (drained) return;
        e.Cancel = true;
        if (closing) return;
        closing = true;
        IsEnabled = false;
        await Task.Yield(); // Exit the WPF closing event before attempting the final Close.
        try { await ((RunnerViewModel)DataContext).ShutdownAsync(); }
        catch (Exception ex) { MessageBox.Show(this, $"Scheduler shutdown failed: {ex.GetType().Name}", Title); }
        drained = true;
        Close();
    }
}
