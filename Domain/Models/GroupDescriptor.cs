using PortablePlayer.Domain.Enums;

namespace PortablePlayer.Domain.Models;

public sealed class GroupDescriptor
{
    public string Name { get; set; } = string.Empty;

    public string FullPath { get; set; } = string.Empty;

    public string PlaylistPath { get; set; } = string.Empty;

    public MediaType MediaType { get; set; } = MediaType.Unknown;

    public bool IsValid { get; set; }

    public List<ValidationIssue> Issues { get; set; } = [];
}
