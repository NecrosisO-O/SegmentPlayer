using System.Windows.Media.Imaging;
using PortablePlayer.Core;
using PortablePlayer.Domain.Enums;
using PortablePlayer.Domain.Models;
using PlayerMediaType = PortablePlayer.Domain.Enums.MediaType;

namespace PortablePlayer.UI.ViewModels;

public sealed class GroupItemViewModel : ObservableObject
{
    private BitmapSource? _previewImage;

    public GroupDescriptor Descriptor { get; init; } = new();

    public string Name => Descriptor.Name;

    public string Status { get; init; } = string.Empty;

    public bool IsValid => Descriptor.IsValid;

    public MediaType MediaType => Descriptor.MediaType;

    public bool IsUnknownType => Descriptor.MediaType == PlayerMediaType.Unknown;

    public string MediaTypeText { get; init; } = string.Empty;

    public int ItemCount { get; init; }

    public string ItemCountText { get; init; } = string.Empty;

    public string NoPreviewText { get; init; } = string.Empty;

    public string? PreviewSourcePath { get; init; }

    public BitmapSource? PreviewImage
    {
        get => _previewImage;
        set
        {
            if (SetProperty(ref _previewImage, value))
            {
                RaisePropertyChanged(nameof(HasPreview));
            }
        }
    }

    public bool HasPreview => PreviewImage is not null;
}
