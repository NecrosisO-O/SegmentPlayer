namespace PortablePlayer.Domain.Models;

public sealed class AppSettings
{
    public int Version { get; set; } = 1;

    public string UiLanguage { get; set; } = "zh-CN";

    public string GroupsRoot { get; set; } = "media_groups";

    public int ThumbnailFrameIndex { get; set; } = 30;

    public bool ThumbnailCacheEnabled { get; set; } = true;

    public string ThumbnailCacheDir { get; set; } = "cache/thumbnails";
}
