using PortablePlayer.Domain.Enums;

namespace PortablePlayer.Infrastructure.Services;

public static class MediaFileRules
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4",
        ".gif",
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".bmp",
        ".webp",
    };

    public static bool IsVideo(string path) => VideoExtensions.Contains(Path.GetExtension(path));

    public static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path));

    public static MediaType DetectMediaType(string path)
    {
        if (IsVideo(path))
        {
            return MediaType.Video;
        }

        if (IsImage(path))
        {
            return MediaType.Image;
        }

        return MediaType.Unknown;
    }

    public static string ToPlaylistMediaType(MediaType mediaType) => mediaType switch
    {
        MediaType.Video => "video",
        MediaType.Image => "image",
        _ => "video",
    };

    public static MediaType FromPlaylistMediaType(string? mediaType) => mediaType?.ToLowerInvariant() switch
    {
        "video" => MediaType.Video,
        "image" => MediaType.Image,
        _ => MediaType.Unknown,
    };
}
