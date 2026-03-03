using System.Text.Json;
using PortablePlayer.Application.Interfaces;

namespace PortablePlayer.Infrastructure.Services;

public sealed class LocalizationService : ILocalizationService
{
    private readonly ISettingsService _settingsService;
    private readonly Dictionary<string, string> _texts = new(StringComparer.OrdinalIgnoreCase);

    public LocalizationService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public string CurrentLanguage { get; private set; } = "zh-CN";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        CurrentLanguage = _settingsService.Current.UiLanguage;
        await LoadLanguageAsync(CurrentLanguage, cancellationToken).ConfigureAwait(false);
    }

    public void SetLanguage(string languageCode)
    {
        if (languageCode is not ("zh-CN" or "en-US"))
        {
            languageCode = "zh-CN";
        }

        CurrentLanguage = languageCode;
        LoadLanguageAsync(CurrentLanguage).GetAwaiter().GetResult();
    }

    public string Get(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        if (_texts.TryGetValue(key, out var value))
        {
            return value;
        }

        return key;
    }

    private async Task LoadLanguageAsync(string languageCode, CancellationToken cancellationToken = default)
    {
        var localizationRoot = Path.Combine(_settingsService.AppRoot, "Resources", "Localization");
        var selectedFile = Path.Combine(localizationRoot, $"strings.{languageCode}.json");
        var fallbackFile = Path.Combine(localizationRoot, "strings.en-US.json");
        var fileToUse = File.Exists(selectedFile) ? selectedFile : fallbackFile;

        if (!File.Exists(fileToUse))
        {
            _texts.Clear();
            return;
        }

        await using var stream = File.OpenRead(fileToUse);
        var content = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        _texts.Clear();
        if (content is null)
        {
            return;
        }

        foreach (var pair in content)
        {
            _texts[pair.Key] = pair.Value;
        }
    }
}
