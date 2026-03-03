using System.Windows.Media.Imaging;
using PortablePlayer.Core;

namespace PortablePlayer.UI.ViewModels;

public sealed class NavigationItemViewModel : ObservableObject
{
    private BitmapSource? _thumbnail;

    public int Index { get; init; }

    public string FileName { get; init; } = string.Empty;

    public bool IsMissing { get; init; }

    public double Loops { get; init; }

    public BitmapSource? Thumbnail
    {
        get => _thumbnail;
        set => SetProperty(ref _thumbnail, value);
    }
}
