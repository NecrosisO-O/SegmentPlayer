using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PortablePlayer.UI.ViewModels;

namespace PortablePlayer.UI.Views;

public partial class GroupSelectionView : UserControl
{
    public GroupSelectionView()
    {
        InitializeComponent();
    }

    private void OnPlayRowClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not GroupSelectionViewModel vm ||
            sender is not FrameworkElement { DataContext: GroupItemViewModel item })
        {
            return;
        }

        vm.PlayGroup(item);
        e.Handled = true;
    }

    private void OnEditRowClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not GroupSelectionViewModel vm ||
            sender is not FrameworkElement { DataContext: GroupItemViewModel item })
        {
            return;
        }

        vm.EditGroup(item);
        e.Handled = true;
    }

    private void OnGroupListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not GroupSelectionViewModel vm)
        {
            return;
        }

        var clickedContainer = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (clickedContainer?.DataContext is not GroupItemViewModel item)
        {
            return;
        }

        vm.PlayGroup(item);
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T target)
            {
                return target;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }
}
