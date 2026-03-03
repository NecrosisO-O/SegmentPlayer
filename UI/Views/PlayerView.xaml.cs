using System.Windows.Controls;
using System.Windows.Input;
using PortablePlayer.Infrastructure.Diagnostics;
using PortablePlayer.UI.ViewModels;

namespace PortablePlayer.UI.Views;

public partial class PlayerView : UserControl
{
    private bool _started;

    public PlayerView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        if (DataContext is not PlayerViewModel vm)
        {
            AppLog.Warn("PlayerView", "Loaded but DataContext is not PlayerViewModel.");
            return;
        }

        try
        {
            AppLog.Info("PlayerView", "Loaded event. Starting playback.");
            await vm.StartPlaybackAsync();
            AppLog.Info("PlayerView", "StartPlaybackAsync finished.");
        }
        catch (Exception ex)
        {
            AppLog.Error("PlayerView", "StartPlaybackAsync failed.", ex);
        }
    }

    private async void OnNavigationDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not PlayerViewModel vm)
        {
            return;
        }

        if (sender is not ListBox list || list.SelectedItem is not NavigationItemViewModel item)
        {
            return;
        }

        await vm.JumpToAsync(item);
    }
}
