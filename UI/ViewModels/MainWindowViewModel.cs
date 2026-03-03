using System.Windows;
using PortablePlayer.Application.Interfaces;
using PortablePlayer.Core;
using PortablePlayer.Domain.Enums;
using PortablePlayer.Domain.Models;
using PortablePlayer.Infrastructure.Diagnostics;
using PortablePlayer.Infrastructure.Services;
using PortablePlayer.UI.Views;

namespace PortablePlayer.UI.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly ILocalizationService _localizationService;
    private readonly IPlaylistService _playlistService;
    private readonly IPlaylistMergeService _playlistMergeService;
    private readonly IGroupScanner _groupScanner;
    private readonly IThumbnailService _thumbnailService;
    private readonly SplashViewModel _splashViewModel;
    private readonly GroupSelectionViewModel _groupSelectionViewModel;

    private object? _currentPage;
    private string _windowTitle = "SegmentPlayer";
    private PlayerViewModel? _playerViewModel;

    public MainWindowViewModel()
    {
        AppLog.Info("MainWindowVM", "Constructing view model.");
        _settingsService = new SettingsService();
        _playlistService = new PlaylistService();
        _playlistMergeService = new PlaylistMergeService();
        _localizationService = new LocalizationService(_settingsService);
        _groupScanner = new GroupScanner(_settingsService, _playlistService);
        _thumbnailService = new ThumbnailService(_settingsService);

        _splashViewModel = new SplashViewModel();
        _groupSelectionViewModel = new GroupSelectionViewModel(
            _groupScanner,
            _playlistService,
            _thumbnailService,
            _settingsService,
            _localizationService)
        {
            PlayRequested = OpenGroupAsync,
            EditRequested = OpenEditorAsync,
            SettingsRequested = OpenSettingsWindow,
        };

        CurrentPage = _splashViewModel;
        _ = InitializeAsync();
    }

    public object? CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public string WindowTitle
    {
        get => _windowTitle;
        private set => SetProperty(ref _windowTitle, value);
    }

    private async Task InitializeAsync()
    {
        try
        {
            AppLog.Info("MainWindowVM", "InitializeAsync started.");
            await _settingsService.LoadAsync().ConfigureAwait(true);
            await _localizationService.InitializeAsync().ConfigureAwait(true);
            WindowTitle = _localizationService.Get("window.title");

            await Task.Delay(1000).ConfigureAwait(true);
            await _groupSelectionViewModel.LoadGroupsAsync().ConfigureAwait(true);
            CurrentPage = _groupSelectionViewModel;
            AppLog.Info("MainWindowVM", "InitializeAsync finished. Group selection page displayed.");
        }
        catch (Exception ex)
        {
            AppLog.Error("MainWindowVM", "InitializeAsync failed.", ex);
            MessageBox.Show(
                ex.Message,
                "Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task OpenGroupAsync(GroupDescriptor descriptor)
    {
        AppLog.Info("MainWindowVM", $"OpenGroup requested. Group={descriptor.Name}, Valid={descriptor.IsValid}, MediaType={descriptor.MediaType}, Playlist={descriptor.PlaylistPath}");
        if (!descriptor.IsValid)
        {
            AppLog.Warn("MainWindowVM", $"OpenGroup blocked because group is invalid: {descriptor.Name}");
            MessageBox.Show(
                string.Join(Environment.NewLine, descriptor.Issues.Select(issue => issue.Message)),
                _localizationService.Get("msg.invalid.title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var playlist = await _playlistService.LoadAsync(descriptor.PlaylistPath).ConfigureAwait(true);
        AppLog.Info("MainWindowVM", $"Playlist loaded. Group={descriptor.Name}, ItemCount={playlist.Items.Count}");

        _playerViewModel?.Dispose();
        var playbackController = new PlaybackController(new VideoPlaybackEngine(), new ImagePlaybackEngine());
        _playerViewModel = new PlayerViewModel(playbackController, _thumbnailService, _settingsService, _localizationService)
        {
            BackRequested = ReturnToGroupSelectionAsync,
            EditRequested = () => OpenEditorAsync(descriptor),
            RefreshRequested = () => RefreshCurrentGroupAsync(descriptor),
        };
        await _playerViewModel.InitializeAsync(descriptor, playlist).ConfigureAwait(true);
        CurrentPage = _playerViewModel;
        AppLog.Info("MainWindowVM", $"Player page displayed for group {descriptor.Name}.");
    }

    private async Task ReturnToGroupSelectionAsync()
    {
        AppLog.Info("MainWindowVM", "Returning to group selection.");
        _playerViewModel?.Dispose();
        _playerViewModel = null;
        await _groupSelectionViewModel.LoadGroupsAsync().ConfigureAwait(true);
        CurrentPage = _groupSelectionViewModel;
    }

    private void OpenSettingsWindow()
    {
        AppLog.Info("MainWindowVM", "Opening settings window.");
        var viewModel = new SettingsViewModel(_settingsService, _localizationService);
        var window = new SettingsWindow
        {
            DataContext = viewModel,
            Owner = global::System.Windows.Application.Current.MainWindow,
        };

        var saved = window.ShowDialog();
        AppLog.Info("MainWindowVM", $"Settings window closed. Saved={saved}");
        if (saved == true && CurrentPage == _groupSelectionViewModel)
        {
            _ = _groupSelectionViewModel.LoadGroupsAsync();
        }
    }

    private async Task OpenEditorAsync(GroupDescriptor descriptor)
    {
        AppLog.Info("MainWindowVM", $"Opening editor for group {descriptor.Name}.");
        if (!File.Exists(descriptor.PlaylistPath))
        {
            AppLog.Warn("MainWindowVM", $"Editor open blocked. Missing playlist: {descriptor.PlaylistPath}");
            MessageBox.Show(
                _localizationService.Get("msg.playlist.missing"),
                _localizationService.Get("msg.error.title"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        var playlist = await _playlistService.LoadAsync(descriptor.PlaylistPath).ConfigureAwait(true);
        var editorVm = new PlaylistEditorViewModel(descriptor, playlist, _playlistService, _localizationService);
        var editorWindow = new PlaylistEditorWindow
        {
            DataContext = editorVm,
            Owner = global::System.Windows.Application.Current.MainWindow,
        };
        var result = editorWindow.ShowDialog();
        AppLog.Info("MainWindowVM", $"Editor closed for group {descriptor.Name}. Saved={result}");
        if (result == true)
        {
            if (_playerViewModel is not null && CurrentPage == _playerViewModel)
            {
                await OpenGroupAsync(descriptor).ConfigureAwait(true);
            }
            else
            {
                await _groupSelectionViewModel.LoadGroupsAsync().ConfigureAwait(true);
            }
        }
    }

    private async Task RefreshCurrentGroupAsync(GroupDescriptor descriptor)
    {
        AppLog.Info("MainWindowVM", $"Refresh requested for group {descriptor.Name}.");
        RefreshDiff diff;
        try
        {
            diff = await _groupScanner.BuildRefreshDiffAsync(descriptor).ConfigureAwait(true);
            AppLog.Info("MainWindowVM", $"Refresh diff built. Added={diff.AddedFiles.Count}, Missing={diff.MissingFiles.Count}, Changed={diff.HasChanges}");
        }
        catch (Exception ex)
        {
            AppLog.Error("MainWindowVM", "Refresh failed while building diff.", ex);
            MessageBox.Show(
                ex.Message,
                _localizationService.Get("msg.error.title"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        if (!diff.HasChanges)
        {
            MessageBox.Show(
                _localizationService.Get("msg.refresh.nochanges"),
                _localizationService.Get("msg.refresh.title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var content = $"{_localizationService.Get("msg.refresh.added")}: {diff.AddedFiles.Count}\n" +
                      $"{_localizationService.Get("msg.refresh.missing")}: {diff.MissingFiles.Count}\n\n" +
                      _localizationService.Get("msg.refresh.prompt");
        var answer = MessageBox.Show(
            content,
            _localizationService.Get("msg.refresh.title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);

        if (answer != MessageBoxResult.Yes)
        {
            AppLog.Info("MainWindowVM", "Refresh canceled by user.");
            return;
        }

        var existing = await _playlistService.LoadAsync(descriptor.PlaylistPath).ConfigureAwait(true);
        var mediaFiles = Directory.EnumerateFiles(descriptor.FullPath)
            .Where(file => descriptor.MediaType switch
            {
                MediaType.Video => MediaFileRules.IsVideo(file),
                MediaType.Image => MediaFileRules.IsImage(file),
                _ => false,
            })
            .Select(Path.GetFileName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToList();

        var merged = _playlistMergeService.MergeWithFilesystem(existing, mediaFiles, descriptor.MediaType);
        await _playlistService.SaveAsync(descriptor.PlaylistPath, merged).ConfigureAwait(true);
        AppLog.Info("MainWindowVM", $"Playlist merged and saved for group {descriptor.Name}. NewItemCount={merged.Items.Count}");
        await OpenGroupAsync(descriptor).ConfigureAwait(true);
    }
}
