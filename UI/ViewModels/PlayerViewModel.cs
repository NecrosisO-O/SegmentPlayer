using System.Collections.ObjectModel;
using System.Windows;
using PortablePlayer.Application.Interfaces;
using PortablePlayer.Core;
using PortablePlayer.Domain.Models;
using PortablePlayer.Infrastructure.Diagnostics;

namespace PortablePlayer.UI.ViewModels;

public sealed class PlayerViewModel : ObservableObject, IDisposable
{
    private readonly IPlaybackController _playbackController;
    private readonly IThumbnailService _thumbnailService;
    private readonly ISettingsService _settingsService;
    private readonly ILocalizationService _localizationService;
    private GroupDescriptor? _descriptor;
    private FrameworkElement? _currentMediaView;
    private string _modeIndicator = "A";
    private bool _isNavigationOpen;
    private string _groupName = string.Empty;
    private string _statusText = string.Empty;
    private NavigationItemViewModel? _selectedNavigationItem;
    private bool _disposed;

    public PlayerViewModel(
        IPlaybackController playbackController,
        IThumbnailService thumbnailService,
        ISettingsService settingsService,
        ILocalizationService localizationService)
    {
        _playbackController = playbackController;
        _thumbnailService = thumbnailService;
        _settingsService = settingsService;
        _localizationService = localizationService;
        AppLog.Info("PlayerVM", "Constructed.");

        NavigationGroups = [];
        ToggleModeCommand = new AsyncRelayCommand(ExecuteToggleModeAsync, () => _descriptor is not null);
        NextCommand = new AsyncRelayCommand(ExecuteNextAsync, () => _playbackController.CanGoNext());
        PreviousCommand = new AsyncRelayCommand(ExecutePreviousAsync, () => _playbackController.CanGoPrevious());
        NextGroupCommand = new AsyncRelayCommand(ExecuteNextGroupAsync, () => _playbackController.CanGoNextGroup());
        PreviousGroupCommand = new AsyncRelayCommand(ExecutePreviousGroupAsync, () => _playbackController.CanGoPreviousGroup());
        ToggleNavigationCommand = new RelayCommand(() => IsNavigationOpen = !IsNavigationOpen);
        JumpToItemCommand = new AsyncRelayCommand(async () =>
        {
            if (SelectedNavigationItem is null)
            {
                return;
            }

            await JumpToAsync(SelectedNavigationItem).ConfigureAwait(true);
        }, () => SelectedNavigationItem is not null);
        BackCommand = new AsyncRelayCommand(async () =>
        {
            if (BackRequested is not null)
            {
                await BackRequested.Invoke();
            }
        });
        RefreshCommand = new AsyncRelayCommand(async () =>
        {
            if (RefreshRequested is not null)
            {
                await RefreshRequested.Invoke();
            }
        });
        EditCommand = new AsyncRelayCommand(async () =>
        {
            if (EditRequested is not null)
            {
                await EditRequested.Invoke();
            }
        });
        _playbackController.StateChanged += OnControllerStateChanged;

        RefreshText = _localizationService.Get("btn.refresh");
        EditText = _localizationService.Get("btn.edit");
        BackText = _localizationService.Get("btn.back");
        PrevGroupText = _localizationService.Get("btn.prevGroup");
        PrevText = _localizationService.Get("btn.prev");
        NextText = _localizationService.Get("btn.next");
        NextGroupText = _localizationService.Get("btn.nextGroup");
        NavigationText = _localizationService.Get("btn.navigation");
        ToggleModeText = _localizationService.Get("btn.toggleMode");
        JumpText = _localizationService.Get("btn.jump");
        GroupPrefix = _localizationService.Get("nav.groupPrefix");
    }

    public ObservableCollection<NavigationGroupViewModel> NavigationGroups { get; }

    public FrameworkElement? CurrentMediaView
    {
        get => _currentMediaView;
        private set => SetProperty(ref _currentMediaView, value);
    }

    public string ModeIndicator
    {
        get => _modeIndicator;
        private set => SetProperty(ref _modeIndicator, value);
    }

    public bool IsNavigationOpen
    {
        get => _isNavigationOpen;
        set => SetProperty(ref _isNavigationOpen, value);
    }

    public string GroupName
    {
        get => _groupName;
        private set => SetProperty(ref _groupName, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string RefreshText { get; }

    public string EditText { get; }

    public string BackText { get; }

    public string PrevGroupText { get; }

    public string PrevText { get; }

    public string NextText { get; }

    public string NextGroupText { get; }

    public string NavigationText { get; }

    public string ToggleModeText { get; }

    public string JumpText { get; }

    public string GroupPrefix { get; }

    public NavigationItemViewModel? SelectedNavigationItem
    {
        get => _selectedNavigationItem;
        set
        {
            if (SetProperty(ref _selectedNavigationItem, value))
            {
                JumpToItemCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public AsyncRelayCommand ToggleModeCommand { get; }

    public AsyncRelayCommand NextCommand { get; }

    public AsyncRelayCommand PreviousCommand { get; }

    public AsyncRelayCommand NextGroupCommand { get; }

    public AsyncRelayCommand PreviousGroupCommand { get; }

    public RelayCommand ToggleNavigationCommand { get; }

    public AsyncRelayCommand BackCommand { get; }

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand EditCommand { get; }

    public AsyncRelayCommand JumpToItemCommand { get; }

    public Func<Task>? BackRequested { get; set; }

    public Func<Task>? RefreshRequested { get; set; }

    public Func<Task>? EditRequested { get; set; }

    public async Task InitializeAsync(GroupDescriptor descriptor, PlaylistDocument playlist, CancellationToken cancellationToken = default)
    {
        _descriptor = descriptor;
        GroupName = descriptor.Name;
        AppLog.Info("PlayerVM", $"InitializeAsync. Group={descriptor.Name}, MediaType={descriptor.MediaType}, Items={playlist.Items.Count}");
        await _playbackController.LoadAsync(descriptor, playlist, cancellationToken).ConfigureAwait(true);
        BuildNavigationSkeleton();
        _ = LoadThumbnailsAsync(cancellationToken);
        UpdateStateFromController();
    }

    public async Task StartPlaybackAsync(CancellationToken cancellationToken = default)
    {
        AppLog.Info("PlayerVM", "StartPlaybackAsync called.");
        await _playbackController.StartAsync(cancellationToken).ConfigureAwait(true);
        UpdateStateFromController();
    }

    public async Task JumpToAsync(NavigationItemViewModel item)
    {
        AppLog.Info("PlayerVM", $"JumpToAsync requested. TargetIndex={item.Index}, File={item.FileName}");
        await _playbackController.JumpToIndexAsync(item.Index).ConfigureAwait(true);
        UpdateStateFromController();
    }

    private async Task LoadThumbnailsAsync(CancellationToken cancellationToken)
    {
        if (_descriptor is null)
        {
            return;
        }

        var mediaType = _descriptor.MediaType;
        var frame = _settingsService.Current.ThumbnailFrameIndex;
        var cache = _settingsService.Current.ThumbnailCacheEnabled;

        foreach (var navItem in NavigationGroups.SelectMany(group => group.Items))
        {
            if (navItem.IsMissing)
            {
                continue;
            }

            var fullPath = Path.Combine(_descriptor.FullPath, navItem.FileName);
            var thumbnail = await _thumbnailService.GetThumbnailAsync(
                fullPath,
                mediaType,
                frame,
                cache,
                cancellationToken).ConfigureAwait(true);
            navItem.Thumbnail = thumbnail;
        }
    }

    private void BuildNavigationSkeleton()
    {
        NavigationGroups.Clear();
        var items = _playbackController.Items;
        var cursor = 0;
        var displayGroup = 1;
        while (cursor < items.Count)
        {
            var group = new NavigationGroupViewModel
            {
                Header = $"{GroupPrefix} {displayGroup}",
            };

            var currentGroup = items[cursor].Group;
            if (currentGroup is null)
            {
                group.Items.Add(ToNavigationItem(items[cursor]));
                cursor++;
            }
            else
            {
                var end = cursor;
                while (end + 1 < items.Count && items[end + 1].Group == currentGroup)
                {
                    end++;
                }

                for (var i = cursor; i <= end; i++)
                {
                    group.Items.Add(ToNavigationItem(items[i]));
                }

                cursor = end + 1;
            }

            NavigationGroups.Add(group);
            displayGroup++;
        }
    }

    private static NavigationItemViewModel ToNavigationItem(ResolvedPlaylistItem item)
    {
        return new NavigationItemViewModel
        {
            Index = item.Index,
            FileName = item.FileName,
            IsMissing = item.Missing,
            Loops = item.Loops,
        };
    }

    private void OnControllerStateChanged(object? sender, EventArgs e)
    {
        var dispatcher = global::System.Windows.Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            UpdateStateFromController();
            return;
        }

        _ = dispatcher.InvokeAsync(UpdateStateFromController);
    }

    private void UpdateStateFromController()
    {
        CurrentMediaView = _playbackController.CurrentView;
        ModeIndicator = _playbackController.ModeIndicator;
        StatusText = _playbackController.Status.ToString();
        AppLog.Info(
            "PlayerVM",
            $"StateChanged. Mode={_playbackController.Mode}, Status={_playbackController.Status}, Index={_playbackController.CurrentIndex}, Indicator={_playbackController.ModeIndicator}, CanNext={_playbackController.CanGoNext()}, CanPrev={_playbackController.CanGoPrevious()}, CanNextGroup={_playbackController.CanGoNextGroup()}, CanPrevGroup={_playbackController.CanGoPreviousGroup()}");
        NotifyCommands();
    }

    private async Task ExecuteToggleModeAsync()
    {
        AppLog.Info("PlayerVM", $"ToggleMode command. CurrentMode={_playbackController.Mode}");
        await _playbackController.ToggleModeAsync().ConfigureAwait(true);
        AppLog.Info("PlayerVM", $"ToggleMode completed. NewMode={_playbackController.Mode}");
    }

    private async Task ExecuteNextAsync()
    {
        AppLog.Info("PlayerVM", $"Next command. CurrentIndex={_playbackController.CurrentIndex}, CanNext={_playbackController.CanGoNext()}");
        await _playbackController.NextAsync().ConfigureAwait(true);
        AppLog.Info("PlayerVM", $"Next completed. CurrentIndex={_playbackController.CurrentIndex}");
    }

    private async Task ExecutePreviousAsync()
    {
        AppLog.Info("PlayerVM", $"Previous command. CurrentIndex={_playbackController.CurrentIndex}, CanPrev={_playbackController.CanGoPrevious()}");
        await _playbackController.PreviousAsync().ConfigureAwait(true);
        AppLog.Info("PlayerVM", $"Previous completed. CurrentIndex={_playbackController.CurrentIndex}");
    }

    private async Task ExecuteNextGroupAsync()
    {
        AppLog.Info("PlayerVM", $"NextGroup command. CurrentIndex={_playbackController.CurrentIndex}, CanNextGroup={_playbackController.CanGoNextGroup()}");
        await _playbackController.NextGroupAsync().ConfigureAwait(true);
        AppLog.Info("PlayerVM", $"NextGroup completed. CurrentIndex={_playbackController.CurrentIndex}");
    }

    private async Task ExecutePreviousGroupAsync()
    {
        AppLog.Info("PlayerVM", $"PreviousGroup command. CurrentIndex={_playbackController.CurrentIndex}, CanPrevGroup={_playbackController.CanGoPreviousGroup()}");
        await _playbackController.PreviousGroupAsync().ConfigureAwait(true);
        AppLog.Info("PlayerVM", $"PreviousGroup completed. CurrentIndex={_playbackController.CurrentIndex}");
    }

    private void NotifyCommands()
    {
        ToggleModeCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
        PreviousCommand.NotifyCanExecuteChanged();
        NextGroupCommand.NotifyCanExecuteChanged();
        PreviousGroupCommand.NotifyCanExecuteChanged();
        JumpToItemCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        AppLog.Info("PlayerVM", "Dispose called.");
        _playbackController.StateChanged -= OnControllerStateChanged;
        _playbackController.Dispose();
    }
}
