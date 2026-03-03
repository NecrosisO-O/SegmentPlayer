using PortablePlayer.Application.Interfaces;
using PortablePlayer.Core;
using PortablePlayer.Domain.Models;

namespace PortablePlayer.UI.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly ILocalizationService _localizationService;
    private string _uiLanguage;
    private string _groupsRoot;
    private int _thumbnailFrameIndex;
    private bool _thumbnailCacheEnabled;
    private string _thumbnailCacheDir;
    private string _statusText = string.Empty;

    public SettingsViewModel(ISettingsService settingsService, ILocalizationService localizationService)
    {
        _settingsService = settingsService;
        _localizationService = localizationService;
        var current = settingsService.Current;
        _uiLanguage = current.UiLanguage;
        _groupsRoot = current.GroupsRoot;
        _thumbnailFrameIndex = current.ThumbnailFrameIndex;
        _thumbnailCacheEnabled = current.ThumbnailCacheEnabled;
        _thumbnailCacheDir = current.ThumbnailCacheDir;

        SaveCommand = new AsyncRelayCommand(SaveAsync);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));

        WindowTitle = _localizationService.Get("settings.title");
        LanguageLabel = _localizationService.Get("settings.language");
        GroupsRootLabel = _localizationService.Get("settings.groupsRoot");
        ThumbnailFrameLabel = _localizationService.Get("settings.thumbnailFrame");
        CacheEnabledLabel = _localizationService.Get("settings.cacheEnabled");
        CacheDirLabel = _localizationService.Get("settings.cacheDir");
        CancelText = _localizationService.Get("btn.cancel");
        SaveText = _localizationService.Get("btn.save");
    }

    public string UiLanguage
    {
        get => _uiLanguage;
        set => SetProperty(ref _uiLanguage, value);
    }

    public string GroupsRoot
    {
        get => _groupsRoot;
        set => SetProperty(ref _groupsRoot, value);
    }

    public int ThumbnailFrameIndex
    {
        get => _thumbnailFrameIndex;
        set => SetProperty(ref _thumbnailFrameIndex, value);
    }

    public bool ThumbnailCacheEnabled
    {
        get => _thumbnailCacheEnabled;
        set => SetProperty(ref _thumbnailCacheEnabled, value);
    }

    public string ThumbnailCacheDir
    {
        get => _thumbnailCacheDir;
        set => SetProperty(ref _thumbnailCacheDir, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string WindowTitle { get; }

    public string LanguageLabel { get; }

    public string GroupsRootLabel { get; }

    public string ThumbnailFrameLabel { get; }

    public string CacheEnabledLabel { get; }

    public string CacheDirLabel { get; }

    public string CancelText { get; }

    public string SaveText { get; }

    public AsyncRelayCommand SaveCommand { get; }

    public RelayCommand CancelCommand { get; }

    public Action<bool>? RequestClose { get; set; }

    private async Task SaveAsync()
    {
        var settings = new AppSettings
        {
            Version = 1,
            UiLanguage = UiLanguage is "en-US" or "zh-CN" ? UiLanguage : "zh-CN",
            GroupsRoot = string.IsNullOrWhiteSpace(GroupsRoot) ? "media_groups" : GroupsRoot.Trim(),
            ThumbnailFrameIndex = Math.Max(0, ThumbnailFrameIndex),
            ThumbnailCacheEnabled = ThumbnailCacheEnabled,
            ThumbnailCacheDir = string.IsNullOrWhiteSpace(ThumbnailCacheDir) ? "cache/thumbnails" : ThumbnailCacheDir.Trim(),
        };

        await _settingsService.SaveAsync(settings).ConfigureAwait(true);
        StatusText = _localizationService.Get("settings.saved.restart");
        RequestClose?.Invoke(true);
    }
}
