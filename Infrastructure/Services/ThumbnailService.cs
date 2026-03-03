using System.Security.Cryptography;
using System.Text;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using LibVLCSharp.Shared;
using PortablePlayer.Application.Interfaces;
using PortablePlayer.Infrastructure.Diagnostics;
using PlayerMediaType = PortablePlayer.Domain.Enums.MediaType;

namespace PortablePlayer.Infrastructure.Services;

public sealed class ThumbnailService : IThumbnailService
{
    private readonly ISettingsService _settingsService;
    private readonly SemaphoreSlim _videoExtractLock = new(1, 1);

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

        if (string.Equals(Path.GetExtension(mediaPath), ".gif", StringComparison.OrdinalIgnoreCase))
        {
            return await LoadBitmapAsync(mediaPath, cancellationToken).ConfigureAwait(false);
        }

        if (!LibVlcRuntime.EnsureInitialized())
        {
            AppLog.Warn("ThumbnailService", "Skip video thumbnail because LibVLC runtime is not initialized.");
            return null;
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
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var extracted = ExtractVideoThumbnailOnSta(mediaPath, frameIndex, outputPath, cancellationToken);
                tcs.TrySetResult(extracted);
            }
            catch (OperationCanceledException)
            {
                tcs.TrySetResult(false);
            }
            catch (Exception ex)
            {
                AppLog.Warn("ThumbnailService", $"ExtractVideoThumbnailAsync failed on STA thread: {ex.Message}");
                tcs.TrySetResult(false);
            }
        })
        {
            IsBackground = true,
            Name = "SegmentPlayer-ThumbExtract",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return await tcs.Task.ConfigureAwait(false);
    }

    private static bool ExtractVideoThumbnailOnSta(
        string mediaPath,
        int frameIndex,
        string outputPath,
        CancellationToken cancellationToken)
    {
        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        var windowParams = new HwndSourceParameters("SegmentPlayerThumbnailHost")
        {
            Width = 1,
            Height = 1,
            PositionX = -32000,
            PositionY = -32000,
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP
        };

        using var hiddenHost = new HwndSource(windowParams);
        using var libVlc = new LibVLC("--intf=dummy", "--no-video-title-show");
        using var media = new Media(libVlc, new Uri(mediaPath));
        _ = media.Parse(MediaParseOptions.ParseLocal);
        using var player = new MediaPlayer(libVlc)
        {
            Media = media,
            Mute = true,
            Hwnd = hiddenHost.Handle,
        };

        var started = new ManualResetEventSlim(false);
        var failed = false;
        void HandlePlaying(object? _, EventArgs __) => started.Set();
        void HandleFailed(object? _, EventArgs __)
        {
            failed = true;
            started.Set();
        }

        player.Playing += HandlePlaying;
        player.EncounteredError += HandleFailed;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!player.Play())
            {
                return false;
            }

            if (!started.Wait(TimeSpan.FromSeconds(6), cancellationToken) || failed)
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
            Thread.Sleep(350);

            if (!player.TakeSnapshot(0, outputPath, 0, 0))
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

                Thread.Sleep(100);
            }

            return false;
        }
        finally
        {
            player.Playing -= HandlePlaying;
            player.EncounteredError -= HandleFailed;
            player.Stop();
            started.Dispose();
        }
    }
}
