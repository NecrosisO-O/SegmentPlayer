using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PortablePlayer.UI.ViewModels;

namespace PortablePlayer.UI.Views;

public partial class PlaylistEditorWindow : Window
{
    private Point _dragStart;
    private int _dragSourceIndex = -1;

    public PlaylistEditorWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is PlaylistEditorViewModel vm)
        {
            vm.RequestClose += HandleRequestClose;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is PlaylistEditorViewModel vm)
        {
            vm.RequestClose -= HandleRequestClose;
        }
    }

    private void HandleRequestClose(bool saved)
    {
        DialogResult = saved;
        Close();
    }

    private void OnListPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragSourceIndex = GetItemIndex(e.OriginalSource as DependencyObject);
    }

    private void OnListMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragSourceIndex < 0)
        {
            return;
        }

        var currentPosition = e.GetPosition(null);
        var diff = _dragStart - currentPosition;
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop(ItemsList, _dragSourceIndex, DragDropEffects.Move);
        _dragSourceIndex = -1;
    }

    private void OnListDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not PlaylistEditorViewModel vm)
        {
            return;
        }

        if (!e.Data.GetDataPresent(typeof(int)))
        {
            return;
        }

        var fromIndex = (int)e.Data.GetData(typeof(int))!;
        var toIndex = GetItemIndex(e.OriginalSource as DependencyObject);
        if (toIndex < 0)
        {
            if (vm.Items.Count == 0)
            {
                return;
            }

            toIndex = vm.Items.Count - 1;
        }

        vm.MoveItem(fromIndex, toIndex);
    }

    private int GetItemIndex(DependencyObject? source)
    {
        var container = FindAncestor<ListBoxItem>(source);
        return container is null ? -1 : ItemsList.ItemContainerGenerator.IndexFromContainer(container);
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T target)
            {
                return target;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
