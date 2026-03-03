namespace PortablePlayer.Domain.Models;

public sealed class PlaybackProgress
{
    public int CurrentIndex { get; set; }

    public int? CurrentGroupNumber { get; set; }

    public int CurrentInUnit { get; set; }

    public int TotalInUnit { get; set; }

    public bool IsInfinite { get; set; }
}
