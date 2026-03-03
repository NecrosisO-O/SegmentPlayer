namespace PortablePlayer.Domain.Models;

public sealed class ResolvedPlaylistItem
{
    public int Index { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string FullPath { get; set; } = string.Empty;

    public double Loops { get; set; }

    public int? Group { get; set; }

    public bool Missing { get; set; }
}
