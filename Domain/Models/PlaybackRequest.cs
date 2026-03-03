using PortablePlayer.Domain.Enums;

namespace PortablePlayer.Domain.Models;

public sealed class PlaybackRequest
{
    public MediaType MediaType { get; set; } = MediaType.Unknown;

    public string FullPath { get; set; } = string.Empty;

    public double DurationSeconds { get; set; }
}
