using PortablePlayer.Application.Interfaces;
using PortablePlayer.Domain.Enums;
using PortablePlayer.Domain.Models;

namespace PortablePlayer.Infrastructure.Services;

public sealed class GroupScanner : IGroupScanner
{
    private readonly ISettingsService _settingsService;
    private readonly IPlaylistService _playlistService;

    public GroupScanner(ISettingsService settingsService, IPlaylistService playlistService)
    {
        _settingsService = settingsService;
        _playlistService = playlistService;
    }

    public async Task<IReadOnlyList<GroupDescriptor>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var root = _settingsService.ResolveFromAppRoot(_settingsService.Current.GroupsRoot);
        Directory.CreateDirectory(root);

        var groups = new List<GroupDescriptor>();
        foreach (var folder in Directory.EnumerateDirectories(root).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var descriptor = new GroupDescriptor
            {
                Name = Path.GetFileName(folder),
                FullPath = folder,
                PlaylistPath = Path.Combine(folder, "playlist.json"),
                IsValid = true,
            };

            var allFiles = Directory.EnumerateFiles(folder)
                .Where(file => !string.Equals(Path.GetFileName(file), "playlist.json", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var videoFiles = allFiles.Where(MediaFileRules.IsVideo).ToList();
            var imageFiles = allFiles.Where(MediaFileRules.IsImage).ToList();

            if (videoFiles.Count > 0 && imageFiles.Count > 0)
            {
                descriptor.IsValid = false;
                descriptor.MediaType = MediaType.Unknown;
                descriptor.Issues.Add(new ValidationIssue
                {
                    Code = "group.media.mixed",
                    Message = "Mixed video and image files are not allowed in one group.",
                });
                groups.Add(descriptor);
                continue;
            }

            if (videoFiles.Count == 0 && imageFiles.Count == 0)
            {
                descriptor.IsValid = false;
                descriptor.MediaType = MediaType.Unknown;
                descriptor.Issues.Add(new ValidationIssue
                {
                    Code = "group.media.empty",
                    Message = "No playable media files found.",
                });
                groups.Add(descriptor);
                continue;
            }

            descriptor.MediaType = videoFiles.Count > 0 ? MediaType.Video : MediaType.Image;

            if (!File.Exists(descriptor.PlaylistPath))
            {
                var defaultPlaylist = await _playlistService.CreateDefaultAsync(folder, descriptor.MediaType, cancellationToken).ConfigureAwait(false);
                await _playlistService.SaveAsync(descriptor.PlaylistPath, defaultPlaylist, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                try
                {
                    var doc = await _playlistService.LoadAsync(descriptor.PlaylistPath, cancellationToken).ConfigureAwait(false);
                    var issues = _playlistService.Validate(doc, descriptor.MediaType);
                    if (issues.Count > 0)
                    {
                        descriptor.IsValid = false;
                        descriptor.Issues.AddRange(issues);
                    }
                }
                catch (Exception ex)
                {
                    descriptor.IsValid = false;
                    descriptor.Issues.Add(new ValidationIssue
                    {
                        Code = "group.playlist.parse",
                        Message = $"playlist.json parse failure: {ex.Message}",
                    });
                }
            }

            groups.Add(descriptor);
        }

        return groups;
    }

    public async Task<RefreshDiff> BuildRefreshDiffAsync(GroupDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        var diff = new RefreshDiff();
        if (!File.Exists(descriptor.PlaylistPath))
        {
            return diff;
        }

        var playlist = await _playlistService.LoadAsync(descriptor.PlaylistPath, cancellationToken).ConfigureAwait(false);
        var fsFiles = GetMediaFiles(descriptor.FullPath, descriptor.MediaType);

        var fsSet = new HashSet<string>(fsFiles, StringComparer.OrdinalIgnoreCase);
        var plSet = new HashSet<string>(playlist.Items.Select(item => item.File), StringComparer.OrdinalIgnoreCase);

        diff.AddedFiles.AddRange(fsSet.Where(file => !plSet.Contains(file)).OrderBy(file => file, StringComparer.OrdinalIgnoreCase));
        diff.MissingFiles.AddRange(plSet.Where(file => !fsSet.Contains(file)).OrderBy(file => file, StringComparer.OrdinalIgnoreCase));
        return diff;
    }

    private static IReadOnlyCollection<string> GetMediaFiles(string fullPath, MediaType mediaType)
    {
        return Directory.EnumerateFiles(fullPath)
            .Where(file => mediaType switch
            {
                MediaType.Video => MediaFileRules.IsVideo(file),
                MediaType.Image => MediaFileRules.IsImage(file),
                _ => false,
            })
            .Select(Path.GetFileName)
            .Where(static file => !string.IsNullOrWhiteSpace(file))
            .Cast<string>()
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
