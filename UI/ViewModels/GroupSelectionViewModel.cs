using System.Collections.ObjectModel;
using PortablePlayer.Application.Interfaces;
using PortablePlayer.Core;
using PortablePlayer.Domain.Enums;
using PortablePlayer.Domain.Models;
using PortablePlayer.Infrastructure.Services;

namespace PortablePlayer.UI.ViewModels;

public sealed class GroupSelectionViewModel : ObservableObject
{
    private readonly IGroupScanner _groupScanner;
    private readonly IPlaylistService _playlistService;
    private readonly IThumbnailService _thumbnailService;
    private readonly ISettingsService _settingsService;
    private readonly ILocalizationService _localization;

    private GroupItemViewModel? _selectedGroup;
    private bool _isLoading;
    private string _statusText = string.Empty;
    private CancellationTokenSource? _thumbnailLoadCts;

    public GroupSelectionViewModel(
        IGroupScanner groupScanner,
        IPlaylistService playlistService,
        IThumbnailService thumbnailService,
        ISettingsService settingsService,
        ILocalizationService localization)
    {
        _groupScanner = groupScanner;
        _playlistService = playlistService;
        _thumbnailService = thumbnailService;
        _settingsService = settingsService;
        _localization = localization;

        Groups = [];
        ReloadCommand = new AsyncRelayCommand(LoadGroupsAsync);
        PlaySelectedCommand = new AsyncRelayCommand(
            async () =>
            {
                if (SelectedGroup is null || PlayRequested is null)
                {
                    return;
                }

                await PlayRequested.Invoke(SelectedGroup.Descriptor);
            },
            () => SelectedGroup?.IsValid == true);

        EditSelectedCommand = new AsyncRelayCommand(
            async () =>
            {
                if (SelectedGroup is null || EditRequested is null)
                {
                    return;
                }

                await EditRequested.Invoke(SelectedGroup.Descriptor);
            },
            () => SelectedGroup is not null);

        OpenSettingsCommand = new RelayCommand(() => SettingsRequested?.Invoke());
    }

    public ObservableCollection<GroupItemViewModel> Groups { get; }

    public GroupItemViewModel? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (SetProperty(ref _selectedGroup, value))
            {
                NotifyCommandStates();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string TitleText => _localization.Get("group.title");

    public string RefreshText => _localization.Get("btn.refresh");

    public string EditText => _localization.Get("btn.edit");

    public string SettingsText => _localization.Get("btn.settings");

    public string PlayText => _localization.Get("btn.play");

    public AsyncRelayCommand ReloadCommand { get; }

    public AsyncRelayCommand PlaySelectedCommand { get; }

    public AsyncRelayCommand EditSelectedCommand { get; }

    public RelayCommand OpenSettingsCommand { get; }

    public Func<GroupDescriptor, Task>? PlayRequested { get; set; }

    public Func<GroupDescriptor, Task>? EditRequested { get; set; }

    public Action? SettingsRequested { get; set; }

    public void PlayGroup(GroupItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedGroup = item;
        if (PlaySelectedCommand.CanExecute(null))
        {
            PlaySelectedCommand.Execute(null);
        }
    }

    public void EditGroup(GroupItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedGroup = item;
        if (EditSelectedCommand.CanExecute(null))
        {
            EditSelectedCommand.Execute(null);
        }
    }

    public async Task LoadGroupsAsync()
    {
        IsLoading = true;
        var loadCts = ResetThumbnailLoading();

        try
        {
            var token = loadCts.Token;
            var scanned = await _groupScanner.ScanAsync(token).ConfigureAwait(true);
            var groupItems = new List<GroupItemViewModel>(scanned.Count);
            foreach (var group in scanned)
            {
                token.ThrowIfCancellationRequested();
                groupItems.Add(await BuildGroupItemAsync(group, token).ConfigureAwait(true));
            }

            Groups.Clear();
            foreach (var group in groupItems)
            {
                Groups.Add(group);
            }

            SelectedGroup = Groups.FirstOrDefault();
            StatusText = string.Format(_localization.Get("group.status.count"), Groups.Count);
            _ = LoadThumbnailsSafeAsync(groupItems, token);
        }
        catch (OperationCanceledException)
        {
            // Ignore canceled stale loads triggered by a newer refresh.
        }
        finally
        {
            if (ReferenceEquals(_thumbnailLoadCts, loadCts))
            {
                IsLoading = false;
                NotifyCommandStates();
            }
        }
    }

    private async Task<GroupItemViewModel> BuildGroupItemAsync(GroupDescriptor group, CancellationToken cancellationToken)
    {
        var playlist = await TryLoadPlaylistAsync(group.PlaylistPath, cancellationToken).ConfigureAwait(true);
        var mediaFiles = GetMediaFiles(group);

        var itemCount = playlist?.Items.Count ?? mediaFiles.Count;
        var previewPath = ResolvePreviewPath(group, playlist, mediaFiles);
        var statusText = group.IsValid
            ? _localization.Get("group.status.valid")
            : string.Join(" | ", group.Issues.Select(issue => issue.Message));

        return new GroupItemViewModel
        {
            Descriptor = group,
            Status = statusText,
            MediaTypeText = GetMediaTypeText(group.MediaType),
            ItemCount = itemCount,
            ItemCountText = string.Format(_localization.Get("group.items"), itemCount),
            NoPreviewText = _localization.Get("group.preview.none"),
            PreviewSourcePath = previewPath,
        };
    }

    private async Task LoadThumbnailsAsync(IReadOnlyList<GroupItemViewModel> groups, CancellationToken cancellationToken)
    {
        foreach (var group in groups)
        {
            if (string.IsNullOrWhiteSpace(group.PreviewSourcePath))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var mediaType = GetThumbnailMediaType(group);
            if (mediaType == MediaType.Unknown)
            {
                continue;
            }

            try
            {
                var image = await _thumbnailService.GetThumbnailAsync(
                        group.PreviewSourcePath,
                        mediaType,
                        _settingsService.Current.ThumbnailFrameIndex,
                        _settingsService.Current.ThumbnailCacheEnabled,
                        cancellationToken)
                    .ConfigureAwait(true);

                if (image is not null)
                {
                    group.PreviewImage = image;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Ignore per-item thumbnail failures and keep placeholder.
            }
        }
    }

    private async Task LoadThumbnailsSafeAsync(IReadOnlyList<GroupItemViewModel> groups, CancellationToken cancellationToken)
    {
        try
        {
            await LoadThumbnailsAsync(groups, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Ignore canceled stale thumbnail loading jobs.
        }
    }

    private CancellationTokenSource ResetThumbnailLoading()
    {
        _thumbnailLoadCts?.Cancel();
        _thumbnailLoadCts?.Dispose();
        _thumbnailLoadCts = new CancellationTokenSource();
        return _thumbnailLoadCts;
    }

    private async Task<PlaylistDocument?> TryLoadPlaylistAsync(string playlistPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(playlistPath))
        {
            return null;
        }

        try
        {
            return await _playlistService.LoadAsync(playlistPath, cancellationToken).ConfigureAwait(true);
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string> GetMediaFiles(GroupDescriptor group)
    {
        if (!Directory.Exists(group.FullPath))
        {
            return [];
        }

        return Directory.EnumerateFiles(group.FullPath)
            .Where(file => group.MediaType switch
            {
                MediaType.Video => MediaFileRules.IsVideo(file),
                MediaType.Image => MediaFileRules.IsImage(file),
                _ => MediaFileRules.IsVideo(file) || MediaFileRules.IsImage(file),
            })
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? ResolvePreviewPath(
        GroupDescriptor group,
        PlaylistDocument? playlist,
        IReadOnlyList<string> mediaFiles)
    {
        if (playlist is not null)
        {
            foreach (var item in playlist.Items)
            {
                if (string.IsNullOrWhiteSpace(item.File))
                {
                    continue;
                }

                if (Math.Abs(item.Loops) < 0.0001)
                {
                    continue;
                }

                var fullPath = Path.Combine(group.FullPath, item.File);
                if (!File.Exists(fullPath))
                {
                    continue;
                }

                if (group.MediaType == MediaType.Unknown &&
                    !MediaFileRules.IsVideo(fullPath) &&
                    !MediaFileRules.IsImage(fullPath))
                {
                    continue;
                }

                if (group.MediaType == MediaType.Video && !MediaFileRules.IsVideo(fullPath))
                {
                    continue;
                }

                if (group.MediaType == MediaType.Image && !MediaFileRules.IsImage(fullPath))
                {
                    continue;
                }

                return fullPath;
            }
        }

        return mediaFiles.FirstOrDefault();
    }

    private static MediaType GetThumbnailMediaType(GroupItemViewModel group)
    {
        if (group.MediaType is MediaType.Video or MediaType.Image)
        {
            return group.MediaType;
        }

        if (string.IsNullOrWhiteSpace(group.PreviewSourcePath))
        {
            return MediaType.Unknown;
        }

        return MediaFileRules.DetectMediaType(group.PreviewSourcePath);
    }

    private string GetMediaTypeText(MediaType mediaType)
    {
        return mediaType switch
        {
            MediaType.Video => _localization.Get("group.type.video"),
            MediaType.Image => _localization.Get("group.type.image"),
            _ => _localization.Get("group.type.unknown"),
        };
    }

    private void NotifyCommandStates()
    {
        PlaySelectedCommand.NotifyCanExecuteChanged();
        EditSelectedCommand.NotifyCanExecuteChanged();
    }
}
