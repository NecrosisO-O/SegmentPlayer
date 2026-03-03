using System.Windows;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using PortablePlayer.Application.Interfaces;
using PortablePlayer.Domain.Models;
using PortablePlayer.Infrastructure.Diagnostics;
using PlayerMediaType = PortablePlayer.Domain.Enums.MediaType;

namespace PortablePlayer.Infrastructure.Services;

public sealed class VideoPlaybackEngine : IPlaybackEngine
{
    private LibVLC? _libVlc;
    private MediaPlayer? _activePlayer;
    private MediaPlayer? _standbyPlayer;
    private Media? _activeMedia;
    private readonly List<Media> _retiredMedia = [];

    public VideoPlaybackEngine()
    {
        LibVLCSharp.Shared.Core.Initialize();
        AppLog.Info("VideoEngine", "Constructed and LibVLC core initialized.");
        View = new VideoView
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
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

        _libVlc = new LibVLC();
        _activePlayer = new MediaPlayer(_libVlc);
        _standbyPlayer = new MediaPlayer(_libVlc)
        {
            Mute = true,
            EnableHardwareDecoding = true,
        };
        _activePlayer.EnableHardwareDecoding = true;
        WireActiveEvents(_activePlayer);
        AttachPlayerToView(_activePlayer);
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
        // Stable mode: keep playback on a single player to avoid deadlocks/pop-up windows.
        AppLog.Info("VideoEngine", $"PreloadAsync skipped (stable mode). File={request.FullPath}");
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
            ((VideoView)View).MediaPlayer = player;
            AppLog.Info("VideoEngine", $"AttachPlayerToView (UI thread). Player={hash}");
            return;
        }

        View.Dispatcher.Invoke(() => ((VideoView)View).MediaPlayer = player);
        AppLog.Info("VideoEngine", $"AttachPlayerToView (dispatcher invoke). Player={hash}");
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
