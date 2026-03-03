using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using LibVLCSharp.Shared;
using PortablePlayer.Application.Interfaces;
using PlayerMediaType = PortablePlayer.Domain.Enums.MediaType;

namespace PortablePlayer.Infrastructure.Services;

public sealed class ThumbnailService : IThumbnailService
{
    private readonly ISettingsService _settingsService;
    private readonly SemaphoreSlim _videoExtractLock = new(1, 1);

    static ThumbnailService()
    {
        LibVLCSharp.Shared.Core.Initialize();
    }

    public ThumbnailService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task<BitmapSource?> GetThumbnailAsync(
        string mediaPath,
        PlayerMediaType mediaType,
        int frameIndex,
        bool useDiskCache,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(mediaPath))
        {
            return null;
        }

        if (mediaType == PlayerMediaType.Image)
        {
            return await LoadBitmapAsync(mediaPath, cancellationToken).ConfigureAwait(false);
        }

        var cacheFile = BuildCachePath(mediaPath, frameIndex);
        if (useDiskCache && File.Exists(cacheFile))
        {
            return await LoadBitmapAsync(cacheFile, cancellationToken).ConfigureAwait(false);
        }

        await _videoExtractLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (useDiskCache && File.Exists(cacheFile))
            {
                return await LoadBitmapAsync(cacheFile, cancellationToken).ConfigureAwait(false);
            }

            var targetPath = cacheFile;
            if (!useDiskCache)
            {
                targetPath = Path.Combine(Path.GetTempPath(), $"pp-thumb-{Guid.NewGuid():N}.png");
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
            }

            var extracted = await ExtractVideoThumbnailAsync(mediaPath, frameIndex, targetPath, cancellationToken).ConfigureAwait(false);
            if (!extracted)
            {
                return null;
            }

            var bitmap = await LoadBitmapAsync(targetPath, cancellationToken).ConfigureAwait(false);
            if (!useDiskCache && File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            return bitmap;
        }
        finally
        {
            _videoExtractLock.Release();
        }
    }

    private string BuildCachePath(string mediaPath, int frameIndex)
    {
        var full = Path.GetFullPath(mediaPath);
        var stamp = File.GetLastWriteTimeUtc(full).Ticks;
        var key = $"{full}|{stamp}|{frameIndex}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        var cacheRoot = _settingsService.ResolveFromAppRoot(_settingsService.Current.ThumbnailCacheDir);
        return Path.Combine(cacheRoot, $"{hash}.png");
    }

    private static async Task<BitmapSource?> LoadBitmapAsync(string path, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return (BitmapSource?)image;
        }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> ExtractVideoThumbnailAsync(
        string mediaPath,
        int frameIndex,
        string outputPath,
        CancellationToken cancellationToken)
    {
        return await Task.Run(async () =>
        {
            try
            {
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }

                using var libVlc = new LibVLC();
                using var media = new Media(libVlc, new Uri(mediaPath));
                await media.Parse(MediaParseOptions.ParseLocal).ConfigureAwait(false);
                using var player = new MediaPlayer(libVlc);
                player.Media = media;
                player.Mute = true;

                var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                void HandlePlaying(object? sender, EventArgs args) => started.TrySetResult(true);
                void HandleFailed(object? sender, EventArgs args) => started.TrySetResult(false);

                player.Playing += HandlePlaying;
                player.EncounteredError += HandleFailed;

                try
                {
                    if (!player.Play())
                    {
                        return false;
                    }

                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    linked.CancelAfter(TimeSpan.FromSeconds(6));
                    var ok = await started.Task.WaitAsync(linked.Token).ConfigureAwait(false);
                    if (!ok)
                    {
                        return false;
                    }

                    var targetMs = frameIndex * (1000.0 / 30.0);
                    var durationMs = media.Duration > 0 ? media.Duration : 0;
                    if (durationMs > 0 && targetMs >= durationMs)
                    {
                        targetMs = Math.Max(0, durationMs - 100);
                    }

                    player.Time = (long)Math.Max(0, targetMs);
                    await Task.Delay(350, cancellationToken).ConfigureAwait(false);

                    var snapped = player.TakeSnapshot(0, outputPath, 0, 0);
                    if (!snapped)
                    {
                        return false;
                    }

                    for (var i = 0; i < 25; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
                        {
                            return true;
                        }

                        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                    }

                    return false;
                }
                finally
                {
                    player.Playing -= HandlePlaying;
                    player.EncounteredError -= HandleFailed;
                    player.Stop();
                }
            }
            catch
            {
                return false;
            }
        }, cancellationToken).ConfigureAwait(false);
    }
}
