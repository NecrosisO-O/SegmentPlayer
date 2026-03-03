using PortablePlayer.Application.Interfaces;
using PortablePlayer.Domain.Enums;
using PortablePlayer.Domain.Models;

namespace PortablePlayer.Infrastructure.Services;

public sealed class PlaylistMergeService : IPlaylistMergeService
{
    public PlaylistDocument MergeWithFilesystem(
        PlaylistDocument existing,
        IReadOnlyCollection<string> mediaFiles,
        MediaType mediaType)
    {
        var normalizedSet = new HashSet<string>(mediaFiles, StringComparer.OrdinalIgnoreCase);
        var result = new PlaylistDocument
        {
            Version = 1,
            MediaType = MediaFileRules.ToPlaylistMediaType(mediaType),
            Items = existing.Items.Select(item => new PlaylistItem
            {
                File = item.File,
                Loops = item.Loops,
                Group = item.Group,
                Missing = !normalizedSet.Contains(item.File),
            }).ToList(),
        };

        var existingFiles = new HashSet<string>(existing.Items.Select(item => item.File), StringComparer.OrdinalIgnoreCase);
        var defaultLoops = mediaType == MediaType.Image ? 3.0 : 1.0;

        var added = mediaFiles
            .Where(file => !existingFiles.Contains(file))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase);

        foreach (var file in added)
        {
            result.Items.Add(new PlaylistItem
            {
                File = file,
                Loops = defaultLoops,
                Group = null,
                Missing = false,
            });
        }

        return result;
    }
}
