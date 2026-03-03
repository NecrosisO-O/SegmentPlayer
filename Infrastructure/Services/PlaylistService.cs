using System.Text.Json;
using PortablePlayer.Application.Interfaces;
using PortablePlayer.Domain.Enums;
using PortablePlayer.Domain.Models;

namespace PortablePlayer.Infrastructure.Services;

public sealed class PlaylistService : IPlaylistService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public async Task<PlaylistDocument> LoadAsync(string playlistPath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(playlistPath);
        var parsed = await JsonSerializer.DeserializeAsync<PlaylistDocument>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        return parsed ?? new PlaylistDocument();
    }

    public async Task SaveAsync(string playlistPath, PlaylistDocument document, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(playlistPath)!);
        await using var stream = File.Create(playlistPath);
        await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public Task<PlaylistDocument> CreateDefaultAsync(string groupPath, MediaType mediaType, CancellationToken cancellationToken = default)
    {
        var files = Directory.EnumerateFiles(groupPath)
            .Where(file => mediaType switch
            {
                MediaType.Video => MediaFileRules.IsVideo(file),
                MediaType.Image => MediaFileRules.IsImage(file),
                _ => false,
            })
            .OrderBy(file => Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)
            .Select(Path.GetFileName)
            .Where(static file => !string.IsNullOrWhiteSpace(file))
            .Cast<string>()
            .ToList();

        var defaultLoops = mediaType == MediaType.Image ? 3.0 : 1.0;

        var document = new PlaylistDocument
        {
            Version = 1,
            MediaType = MediaFileRules.ToPlaylistMediaType(mediaType),
            Items = files.Select(file => new PlaylistItem
            {
                File = file,
                Loops = defaultLoops,
                Group = null,
            }).ToList(),
        };

        return Task.FromResult(document);
    }

    public IReadOnlyList<ValidationIssue> Validate(PlaylistDocument document, MediaType mediaType)
    {
        var issues = new List<ValidationIssue>();
        if (document is null)
        {
            issues.Add(new ValidationIssue
            {
                Code = "playlist.null",
                Message = "Playlist content is null.",
            });
            return issues;
        }

        var actualType = MediaFileRules.FromPlaylistMediaType(document.MediaType);
        if (actualType != mediaType)
        {
            issues.Add(new ValidationIssue
            {
                Code = "playlist.mediaType.mismatch",
                Message = $"Playlist mediaType '{document.MediaType}' mismatches group media type '{mediaType}'.",
            });
        }

        for (var i = 0; i < document.Items.Count; i++)
        {
            var item = document.Items[i];
            if (string.IsNullOrWhiteSpace(item.File))
            {
                issues.Add(new ValidationIssue
                {
                    Code = "playlist.item.file.empty",
                    Message = $"Item #{i + 1} file is empty.",
                });
            }

            if (item.Group is <= 0)
            {
                issues.Add(new ValidationIssue
                {
                    Code = "playlist.item.group.invalid",
                    Message = $"Item '{item.File}' has invalid group number.",
                });
            }

            if (mediaType == MediaType.Video && !IsValidVideoLoops(item.Loops))
            {
                issues.Add(new ValidationIssue
                {
                    Code = "playlist.item.loops.video.invalid",
                    Message = $"Item '{item.File}' has invalid loops for video.",
                });
            }

            if (mediaType == MediaType.Image && !IsValidImageLoops(item.Loops))
            {
                issues.Add(new ValidationIssue
                {
                    Code = "playlist.item.loops.image.invalid",
                    Message = $"Item '{item.File}' has invalid loops for image.",
                });
            }
        }

        return issues;
    }

    private static bool IsValidVideoLoops(double loops)
    {
        if (loops == -1 || loops == 0)
        {
            return true;
        }

        if (loops < 1)
        {
            return false;
        }

        return Math.Abs(loops - Math.Round(loops)) < 0.0001;
    }

    private static bool IsValidImageLoops(double loops)
    {
        if (loops == -1 || loops == 0)
        {
            return true;
        }

        if (loops < 0.1)
        {
            return false;
        }

        return Math.Abs(loops * 10 - Math.Round(loops * 10)) < 0.0001;
    }
}
