using System.Collections.ObjectModel;

namespace PortablePlayer.UI.ViewModels;

public sealed class NavigationGroupViewModel
{
    public string Header { get; init; } = string.Empty;

    public ObservableCollection<NavigationItemViewModel> Items { get; } = [];
}
