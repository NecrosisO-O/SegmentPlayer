using System.Windows.Controls;
using System.Windows.Input;
using PortablePlayer.UI.ViewModels;

namespace PortablePlayer.UI.Views;

public partial class PlayerView : UserControl
{
    public PlayerView()
    {
        InitializeComponent();
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
