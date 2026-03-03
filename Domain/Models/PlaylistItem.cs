namespace PortablePlayer.Domain.Models;

public sealed class PlaylistItem
{
    public string File { get; set; } = string.Empty;

    public double Loops { get; set; } = 1;

    public int? Group { get; set; }

    public bool Missing { get; set; }
}
