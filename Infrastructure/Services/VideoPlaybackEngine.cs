using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using PortablePlayer.Application.Interfaces;
using PortablePlayer.Domain.Models;
using PortablePlayer.Infrastructure.Diagnostics;
using PlayerMediaType = PortablePlayer.Domain.Enums.MediaType;
using WpfImage = System.Windows.Controls.Image;

namespace PortablePlayer.Infrastructure.Services;

public sealed class VideoPlaybackEngine : IPlaybackEngine
{
    private static readonly string[] StableLibVlcOptions =
    {
        "--no-video-title-show",
    };

    private readonly Grid _hostView;
    private readonly VideoView _videoView;
    private readonly WpfImage _gifView;
    private readonly DispatcherTimer _gifTimer;
    private readonly object _gifCacheSync = new();
    private GifClip? _gifCachedClip;
    private string? _gifCachedPath;
    private GifClip? _activeGifClip;
    private int _gifFrameIndex;
    private bool _isGifPlayback;

    private LibVLC? _libVlc;
    private MediaPlayer? _activePlayer;
    private MediaPlayer? _standbyPlayer;
    private Media? _activeMedia;
    private readonly List<Media> _retiredMedia = [];
    private readonly ConcurrentDictionary<string, double> _aspectCache = new(StringComparer.OrdinalIgnoreCase);
    private double _sourceAspectRatio = 16d / 9d;
    private int _aspectProbeVersion;

    public VideoPlaybackEngine()
    {
        var initialized = LibVlcRuntime.EnsureInitialized();
        AppLog.Info("VideoEngine", $"Constructed. LibVlcInitialized={initialized}");

        _videoView = new VideoView
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = System.Windows.Media.Brushes.Black,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true,
        };

        _gifView = new WpfImage
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Stretch = System.Windows.Media.Stretch.Fill,
            Visibility = Visibility.Collapsed,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true,
        };

        _hostView = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = System.Windows.Media.Brushes.Black,
            ClipToBounds = true,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true,
        };
        _hostView.Children.Add(_videoView);
        _hostView.Children.Add(_gifView);
        _hostView.SizeChanged += OnHostViewSizeChanged;

        _gifTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        _gifTimer.Tick += OnGifTimerTick;

        View = _hostView;
    }

    public PlayerMediaType SupportedType => PlayerMediaType.Video;

    public FrameworkElement View { get; }

    public event EventHandler? PlaybackCompleted;

    public event EventHandler<PlaybackEngineError>? PlaybackFailed;

#pragma warning disable CS0067
    public event EventHandler<int>? IntegerProgressChanged;
#pragma warning restore CS0067

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_libVlc is not null)
        {
            AppLog.Info("VideoEngine", "InitializeAsync skipped because engine is already initialized.");
            return Task.CompletedTask;
        }

        _libVlc = new LibVLC(StableLibVlcOptions);
        _activePlayer = new MediaPlayer(_libVlc)
        {
            EnableHardwareDecoding = true,
        };
        _standbyPlayer = new MediaPlayer(_libVlc)
        {
            Mute = true,
            EnableHardwareDecoding = true,
        };

        WireActiveEvents(_activePlayer);
        AttachPlayerToView(_activePlayer);
        ApplyStableVideoLayout(_activePlayer);
        ApplyStableVideoLayout(_standbyPlayer);
        UpdateVideoViewBounds();
        AppLog.Info("VideoEngine", $"Initialized. ActivePlayer={_activePlayer.GetHashCode()}, StandbyPlayer={_standbyPlayer.GetHashCode()}");
        return Task.CompletedTask;
    }

    public async Task PlayAsync(PlaybackRequest request, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        AppLog.Info("VideoEngine", $"PlayAsync requested. File={request.FullPath}");
        if (!File.Exists(request.FullPath))
        {
            PlaybackFailed?.Invoke(this, new PlaybackEngineError
            {
                Message = $"Media file not found: {request.FullPath}",
            });
            AppLog.Warn("VideoEngine", $"PlayAsync failed: file missing ({request.FullPath}).");
            return;
        }

        if (IsGifPath(request.FullPath))
        {
            await PlayGifAsync(request.FullPath, cancellationToken).ConfigureAwait(false);
            return;
        }

        PlayVideo(request.FullPath);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        AppLog.Info("VideoEngine", "StopAsync requested.");
        StopGifPlayback();

        if (_activePlayer is not null)
        {
            SafeStopActivePlayer();
        }

        if (_standbyPlayer is not null)
        {
            _standbyPlayer.Stop();
        }

        DisposeRetiredMedia();

        return Task.CompletedTask;
    }

    public async Task PreloadAsync(PlaybackRequest request, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        if (!IsGifPath(request.FullPath) || !File.Exists(request.FullPath))
        {
            // Keep video preload path side-effect free in stable mode.
            return;
        }

        try
        {
            _ = await GetOrLoadGifClipAsync(request.FullPath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Ignore canceled preloads.
        }
        catch (Exception ex)
        {
            AppLog.Warn("VideoEngine", $"PreloadAsync GIF decode ignored failure: {ex.Message}");
        }
    }

    public Task PromotePreloadedAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        AppLog.Info("VideoEngine", "PromotePreloadedAsync skipped (stable mode).");
        return Task.CompletedTask;
    }

    private async Task PlayGifAsync(string fullPath, CancellationToken cancellationToken)
    {
        StopGifPlayback();
        if (_activePlayer is not null)
        {
            SafeStopActivePlayer();
        }

        if (_activeMedia is not null)
        {
            _retiredMedia.Add(_activeMedia);
            _activeMedia = null;
        }

        GifClip clip;
        try
        {
            clip = await GetOrLoadGifClipAsync(fullPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            PlaybackFailed?.Invoke(this, new PlaybackEngineError
            {
                Message = $"Unable to decode GIF: {fullPath}",
                Exception = ex,
            });
            AppLog.Error("VideoEngine", $"PlayGifAsync failed for {fullPath}.", ex);
            return;
        }

        _aspectCache[fullPath] = clip.AspectRatio;
        _sourceAspectRatio = clip.AspectRatio;

        await _hostView.Dispatcher.InvokeAsync(() =>
        {
            _isGifPlayback = true;
            _activeGifClip = clip;
            _gifFrameIndex = 0;

            _videoView.Visibility = Visibility.Collapsed;
            _gifView.Visibility = Visibility.Visible;
            _gifView.Source = clip.Frames[0];
            UpdateVideoViewBounds();

            _gifTimer.Stop();
            _gifTimer.Interval = TimeSpan.FromMilliseconds(clip.DelaysMs[0]);
            _gifTimer.Start();
        });

        AppLog.Info("VideoEngine", $"PlayGifAsync started. File={Path.GetFileName(fullPath)}, Frames={clip.Frames.Count}, Aspect={clip.AspectRatio:F4}");
    }

    private void PlayVideo(string fullPath)
    {
        StopGifPlayback();
        EnterVideoMode();

        if (_activeMedia is not null)
        {
            _retiredMedia.Add(_activeMedia);
        }

        _activeMedia = new Media(_libVlc!, new Uri(fullPath));
        if (_aspectCache.TryGetValue(fullPath, out var cachedAspect) &&
            cachedAspect > 0.05 &&
            double.IsFinite(cachedAspect))
        {
            _sourceAspectRatio = cachedAspect;
            UpdateVideoViewBounds();
        }

        if (!_activePlayer!.Play(_activeMedia))
        {
            PlaybackFailed?.Invoke(this, new PlaybackEngineError
            {
                Message = $"Unable to play video: {fullPath}",
            });
            AppLog.Warn("VideoEngine", $"PlayAsync failed with active player {_activePlayer.GetHashCode()}.");
            return;
        }

        AppLog.Info("VideoEngine", $"PlayAsync started on active player {_activePlayer.GetHashCode()}.");
        ApplyStableVideoLayout(_activePlayer);
        StartAspectProbe(_activePlayer, fullPath);
    }

    private async Task<GifClip> GetOrLoadGifClipAsync(string fullPath, CancellationToken cancellationToken)
    {
        lock (_gifCacheSync)
        {
            if (string.Equals(_gifCachedPath, fullPath, StringComparison.OrdinalIgnoreCase) &&
                _gifCachedClip is not null)
            {
                return _gifCachedClip;
            }
        }

        var decoded = await Task.Run(() => DecodeGifClip(fullPath), cancellationToken).ConfigureAwait(false);
        lock (_gifCacheSync)
        {
            _gifCachedPath = fullPath;
            _gifCachedClip = decoded;
        }

        return decoded;
    }

    private static GifClip DecodeGifClip(string fullPath)
    {
        using var gif = System.Drawing.Image.FromFile(fullPath);
        if (gif.FrameDimensionsList.Length == 0)
        {
            throw new InvalidOperationException("GIF has no frame dimensions.");
        }

        var dimension = new System.Drawing.Imaging.FrameDimension(gif.FrameDimensionsList[0]);
        var frameCount = gif.GetFrameCount(dimension);
        if (frameCount <= 0)
        {
            throw new InvalidOperationException("GIF has no frames.");
        }

        var frames = new List<BitmapSource>(frameCount);
        var delays = ReadGifDelayMs(gif, frameCount);
        for (var i = 0; i < frameCount; i++)
        {
            gif.SelectActiveFrame(dimension, i);
            using var frameBitmap = new System.Drawing.Bitmap(gif);
            frames.Add(ToBitmapSource(frameBitmap));
        }

        var width = Math.Max(1, gif.Width);
        var height = Math.Max(1, gif.Height);
        var aspect = (double)width / height;
        if (!double.IsFinite(aspect) || aspect <= 0.05)
        {
            aspect = 16d / 9d;
        }

        return new GifClip(frames, delays, aspect);
    }

    private static List<int> ReadGifDelayMs(System.Drawing.Image gif, int frameCount)
    {
        const int defaultDelayMs = 100;
        const int delayPropertyId = 0x5100;
        var delays = new List<int>(frameCount);
        try
        {
            var property = gif.GetPropertyItem(delayPropertyId);
            if (property?.Value is null || property.Value.Length == 0)
            {
                for (var i = 0; i < frameCount; i++)
                {
                    delays.Add(defaultDelayMs);
                }

                return delays;
            }

            var bytes = property.Value;
            for (var i = 0; i < frameCount; i++)
            {
                var offset = i * 4;
                if (offset + 4 > bytes.Length)
                {
                    delays.Add(defaultDelayMs);
                    continue;
                }

                var delayCentiseconds = BitConverter.ToInt32(bytes, offset);
                if (delayCentiseconds <= 0)
                {
                    delays.Add(defaultDelayMs);
                    continue;
                }

                delays.Add(Math.Max(20, delayCentiseconds * 10));
            }
        }
        catch
        {
            for (var i = 0; i < frameCount; i++)
            {
                delays.Add(defaultDelayMs);
            }
        }

        while (delays.Count < frameCount)
        {
            delays.Add(defaultDelayMs);
        }

        return delays;
    }

    private static BitmapSource ToBitmapSource(System.Drawing.Bitmap bitmap)
    {
        var hBitmap = bitmap.GetHbitmap();
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            _ = DeleteObject(hBitmap);
        }
    }

    private void AttachPlayerToView(MediaPlayer player)
    {
        var hash = player.GetHashCode();
        if (View.Dispatcher.CheckAccess())
        {
            _videoView.MediaPlayer = player;
            ApplyStableVideoLayout(player);
            AppLog.Info("VideoEngine", $"AttachPlayerToView (UI thread). Player={hash}");
            return;
        }

        View.Dispatcher.Invoke(() =>
        {
            _videoView.MediaPlayer = player;
            ApplyStableVideoLayout(player);
        });
        AppLog.Info("VideoEngine", $"AttachPlayerToView (dispatcher invoke). Player={hash}");
    }

    private void OnHostViewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateVideoViewBounds();
        if (!_isGifPlayback && _activePlayer is not null)
        {
            ApplyStableVideoLayout(_activePlayer);
        }
    }

    private void OnGifTimerTick(object? sender, EventArgs e)
    {
        if (_activeGifClip is null || _activeGifClip.Frames.Count == 0)
        {
            _gifTimer.Stop();
            return;
        }

        var next = _gifFrameIndex + 1;
        if (next >= _activeGifClip.Frames.Count)
        {
            _gifTimer.Stop();
            _gifFrameIndex = 0;
            RaisePlaybackCompletedAsync();
            return;
        }

        _gifFrameIndex = next;
        _gifView.Source = _activeGifClip.Frames[_gifFrameIndex];
        _gifTimer.Interval = TimeSpan.FromMilliseconds(_activeGifClip.DelaysMs[_gifFrameIndex]);
    }

    private void StartAspectProbe(MediaPlayer player, string mediaPath)
    {
        var probeVersion = Interlocked.Increment(ref _aspectProbeVersion);
        _ = Task.Run(async () =>
        {
            for (var i = 0; i < 30; i++)
            {
                if (probeVersion != _aspectProbeVersion || _isGifPlayback)
                {
                    return;
                }

                try
                {
                    if (TryReadVideoAspect(player, out var aspect))
                    {
                        _aspectCache[mediaPath] = aspect;
                        if (Math.Abs(_sourceAspectRatio - aspect) > 0.01)
                        {
                            _sourceAspectRatio = aspect;
                            AppLog.Info("VideoEngine", $"Aspect probe success. Aspect={aspect:F4}");
                            UpdateVideoViewBounds();
                        }
                        return;
                    }
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    AppLog.Warn("VideoEngine", $"Aspect probe failed: {ex.Message}");
                    return;
                }

                await Task.Delay(60).ConfigureAwait(false);
            }

            AppLog.Warn("VideoEngine", "Aspect probe timeout; keep fallback aspect ratio.");
        });
    }

    private static bool TryReadVideoAspect(MediaPlayer player, out double aspect)
    {
        aspect = 0;
        uint width = 0;
        uint height = 0;
        if (!player.Size(0, ref width, ref height))
        {
            return false;
        }

        if (width == 0 || height == 0)
        {
            return false;
        }

        aspect = (double)width / height;
        return double.IsFinite(aspect) && aspect > 0.05;
    }

    private void EnterVideoMode()
    {
        if (!_hostView.Dispatcher.CheckAccess())
        {
            _hostView.Dispatcher.Invoke(EnterVideoMode);
            return;
        }

        _videoView.Visibility = Visibility.Visible;
        _gifView.Visibility = Visibility.Collapsed;
        _gifView.Source = null;
        _activeGifClip = null;
        _gifFrameIndex = 0;
        _isGifPlayback = false;
    }

    private void StopGifPlayback()
    {
        if (!_hostView.Dispatcher.CheckAccess())
        {
            _hostView.Dispatcher.Invoke(StopGifPlayback);
            return;
        }

        _gifTimer.Stop();
        _gifFrameIndex = 0;
        _activeGifClip = null;
        _gifView.Source = null;
        _gifView.Visibility = Visibility.Collapsed;
        _isGifPlayback = false;
    }

    private void UpdateVideoViewBounds()
    {
        if (!_hostView.Dispatcher.CheckAccess())
        {
            _hostView.Dispatcher.Invoke(UpdateVideoViewBounds);
            return;
        }

        var hostWidth = _hostView.ActualWidth;
        var hostHeight = _hostView.ActualHeight;
        if (hostWidth < 2 || hostHeight < 2)
        {
            _videoView.Width = double.NaN;
            _videoView.Height = double.NaN;
            _gifView.Width = double.NaN;
            _gifView.Height = double.NaN;
            return;
        }

        var sourceAspect = _sourceAspectRatio;
        if (!double.IsFinite(sourceAspect) || sourceAspect <= 0.05)
        {
            sourceAspect = hostWidth / hostHeight;
        }

        var hostAspect = hostWidth / hostHeight;
        double targetWidth;
        double targetHeight;
        if (hostAspect > sourceAspect)
        {
            targetHeight = hostHeight;
            targetWidth = targetHeight * sourceAspect;
        }
        else
        {
            targetWidth = hostWidth;
            targetHeight = targetWidth / sourceAspect;
        }

        var width = Math.Max(1, Math.Round(targetWidth));
        var height = Math.Max(1, Math.Round(targetHeight));
        _videoView.Width = width;
        _videoView.Height = height;
        _gifView.Width = width;
        _gifView.Height = height;
    }

    private static void ApplyStableVideoLayout(MediaPlayer player)
    {
        try
        {
            player.Scale = 0f;
            player.AspectRatio = null;
        }
        catch
        {
            // Keep playback alive even if output-specific scale/aspect controls are unavailable.
        }
    }

    private void SafeStopActivePlayer()
    {
        if (_activePlayer is null)
        {
            return;
        }

        UnwireActiveEvents(_activePlayer);
        _activePlayer.Stop();
        WireActiveEvents(_activePlayer);
        AppLog.Info("VideoEngine", $"SafeStopActivePlayer executed on player {_activePlayer.GetHashCode()}.");
    }

    private void EnsureInitialized()
    {
        if (_libVlc is null || _activePlayer is null || _standbyPlayer is null)
        {
            throw new InvalidOperationException("Video engine has not been initialized.");
        }
    }

    private void WireActiveEvents(MediaPlayer player)
    {
        player.EndReached += OnEndReached;
        player.EncounteredError += OnEncounteredError;
    }

    private void UnwireActiveEvents(MediaPlayer player)
    {
        player.EndReached -= OnEndReached;
        player.EncounteredError -= OnEncounteredError;
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        AppLog.Info("VideoEngine", $"EndReached event from player {(sender as MediaPlayer)?.GetHashCode() ?? 0}.");
        RaisePlaybackCompletedAsync();
    }

    private void RaisePlaybackCompletedAsync()
    {
        _ = Task.Run(() =>
        {
            try
            {
                PlaybackCompleted?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                AppLog.Error("VideoEngine", "PlaybackCompleted callback threw an exception.", ex);
            }
        });
    }

    private void OnEncounteredError(object? sender, EventArgs e)
    {
        AppLog.Warn("VideoEngine", $"EncounteredError from player {(sender as MediaPlayer)?.GetHashCode() ?? 0}.");
        PlaybackFailed?.Invoke(this, new PlaybackEngineError
        {
            Message = "Video engine encountered an error.",
        });
    }

    private static bool IsGifPath(string path) =>
        string.Equals(Path.GetExtension(path), ".gif", StringComparison.OrdinalIgnoreCase);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    public void Dispose()
    {
        AppLog.Info("VideoEngine", "Dispose called.");
        _hostView.SizeChanged -= OnHostViewSizeChanged;
        _gifTimer.Stop();
        _gifTimer.Tick -= OnGifTimerTick;
        _videoView.MediaPlayer = null;
        _gifView.Source = null;

        if (_activePlayer is not null)
        {
            UnwireActiveEvents(_activePlayer);
            _activePlayer.Dispose();
        }

        _standbyPlayer?.Dispose();
        _activeMedia?.Dispose();
        DisposeRetiredMedia();
        _libVlc?.Dispose();
    }

    private void DisposeRetiredMedia()
    {
        if (_retiredMedia.Count == 0)
        {
            return;
        }

        foreach (var media in _retiredMedia)
        {
            try
            {
                media.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Warn("VideoEngine", $"DisposeRetiredMedia ignored dispose failure: {ex.Message}");
            }
        }

        _retiredMedia.Clear();
    }

    private sealed class GifClip
    {
        public GifClip(IReadOnlyList<BitmapSource> frames, IReadOnlyList<int> delaysMs, double aspectRatio)
        {
            Frames = frames;
            DelaysMs = delaysMs;
            AspectRatio = aspectRatio;
        }

        public IReadOnlyList<BitmapSource> Frames { get; }

        public IReadOnlyList<int> DelaysMs { get; }

        public double AspectRatio { get; }
    }
}
