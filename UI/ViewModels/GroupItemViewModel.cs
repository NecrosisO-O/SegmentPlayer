using PortablePlayer.Domain.Enums;
using PortablePlayer.Domain.Models;

namespace PortablePlayer.UI.ViewModels;

public sealed class GroupItemViewModel
{
    public GroupDescriptor Descriptor { get; init; } = new();

    public string Name => Descriptor.Name;

    public string Status { get; init; } = string.Empty;

    public bool IsValid => Descriptor.IsValid;

    public MediaType MediaType => Descriptor.MediaType;
}
