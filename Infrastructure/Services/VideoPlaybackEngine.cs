using System.Windows;
using System.Windows.Controls;
using System.Collections.Concurrent;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using PortablePlayer.Application.Interfaces;
using PortablePlayer.Domain.Models;
using PortablePlayer.Infrastructure.Diagnostics;
using PlayerMediaType = PortablePlayer.Domain.Enums.MediaType;

namespace PortablePlayer.Infrastructure.Services;

public sealed class VideoPlaybackEngine : IPlaybackEngine
{
    private static readonly string[] StableLibVlcOptions =
    {
        "--no-video-title-show",
    };

    private readonly Grid _hostView;
    private readonly VideoView _videoView;
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
        _hostView.SizeChanged += OnHostViewSizeChanged;

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

    public Task PlayAsync(PlaybackRequest request, CancellationToken cancellationToken = default)
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
            return Task.CompletedTask;
        }

        if (_activeMedia is not null)
        {
            _retiredMedia.Add(_activeMedia);
        }

        _activeMedia = new Media(_libVlc!, new Uri(request.FullPath));
        if (_aspectCache.TryGetValue(request.FullPath, out var cachedAspect) &&
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
                Message = $"Unable to play video: {request.FullPath}",
            });
            AppLog.Warn("VideoEngine", $"PlayAsync failed with active player {_activePlayer.GetHashCode()}.");
        }
        else
        {
            AppLog.Info("VideoEngine", $"PlayAsync started on active player {_activePlayer.GetHashCode()}.");
            ApplyStableVideoLayout(_activePlayer);
            StartAspectProbe(_activePlayer, request.FullPath);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        AppLog.Info("VideoEngine", "StopAsync requested.");
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

    public Task PreloadAsync(PlaybackRequest request, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        // Keep preload path side-effect free in stable mode.
        return Task.CompletedTask;
    }

    public Task PromotePreloadedAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        AppLog.Info("VideoEngine", "PromotePreloadedAsync skipped (stable mode).");
        return Task.CompletedTask;
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
        if (_activePlayer is not null)
        {
            ApplyStableVideoLayout(_activePlayer);
        }
    }

    private void StartAspectProbe(MediaPlayer player, string mediaPath)
    {
        var probeVersion = Interlocked.Increment(ref _aspectProbeVersion);
        _ = Task.Run(async () =>
        {
            for (var i = 0; i < 30; i++)
            {
                if (probeVersion != _aspectProbeVersion)
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

        _videoView.Width = Math.Max(1, Math.Round(targetWidth));
        _videoView.Height = Math.Max(1, Math.Round(targetHeight));
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

    public void Dispose()
    {
        AppLog.Info("VideoEngine", "Dispose called.");
        _hostView.SizeChanged -= OnHostViewSizeChanged;
        _videoView.MediaPlayer = null;

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
}
