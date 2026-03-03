namespace PortablePlayer.Domain.Models;

public sealed class PlaylistDocument
{
    public int Version { get; set; } = 1;

    public string MediaType { get; set; } = "video";

    public List<PlaylistItem> Items { get; set; } = [];
}
