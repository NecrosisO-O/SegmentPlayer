using System.Windows;
using PortablePlayer.UI.ViewModels;

namespace PortablePlayer.UI.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.RequestClose += HandleRequestClose;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.RequestClose -= HandleRequestClose;
        }
    }

    private void HandleRequestClose(bool saved)
    {
        DialogResult = saved;
        Close();
    }
}
