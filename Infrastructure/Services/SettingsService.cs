using System.Text.Json;
using PortablePlayer.Application.Interfaces;
using PortablePlayer.Domain.Models;

namespace PortablePlayer.Infrastructure.Services;

public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public SettingsService()
    {
        AppRoot = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        SettingsPath = ResolveFromAppRoot(Path.Combine("config", "settings.json"));
        Current = new AppSettings();
    }

    public string AppRoot { get; }

    public string SettingsPath { get; }

    public AppSettings Current { get; private set; }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        EnsureBaseDirectories();
        if (!File.Exists(SettingsPath))
        {
            Current = new AppSettings();
            await SaveAsync(Current, cancellationToken).ConfigureAwait(false);
            return Current;
        }

        try
        {
            await using var stream = File.OpenRead(SettingsPath);
            var parsed = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            Current = Sanitize(parsed ?? new AppSettings());
        }
        catch
        {
            Current = new AppSettings();
            await SaveAsync(Current, cancellationToken).ConfigureAwait(false);
        }

        return Current;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var sanitized = Sanitize(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        await using var stream = File.Create(SettingsPath);
        await JsonSerializer.SerializeAsync(stream, sanitized, JsonOptions, cancellationToken).ConfigureAwait(false);
        Current = sanitized;
        EnsureBaseDirectories();
    }

    public string ResolveFromAppRoot(string relativeOrAbsolutePath)
    {
        if (Path.IsPathFullyQualified(relativeOrAbsolutePath))
        {
            return relativeOrAbsolutePath;
        }

        var normalized = relativeOrAbsolutePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        return Path.GetFullPath(Path.Combine(AppRoot, normalized));
    }

    private void EnsureBaseDirectories()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        Directory.CreateDirectory(ResolveFromAppRoot(Current.GroupsRoot));
        Directory.CreateDirectory(ResolveFromAppRoot(Current.ThumbnailCacheDir));
    }

    private static AppSettings Sanitize(AppSettings source)
    {
        var safe = source ?? new AppSettings();
        safe.Version = 1;
        safe.UiLanguage = safe.UiLanguage is "en-US" or "zh-CN" ? safe.UiLanguage : "zh-CN";
        safe.GroupsRoot = string.IsNullOrWhiteSpace(safe.GroupsRoot) ? "media_groups" : safe.GroupsRoot;
        safe.ThumbnailCacheDir = string.IsNullOrWhiteSpace(safe.ThumbnailCacheDir) ? "cache/thumbnails" : safe.ThumbnailCacheDir;
        safe.ThumbnailFrameIndex = Math.Max(0, safe.ThumbnailFrameIndex);
        return safe;
    }
}
