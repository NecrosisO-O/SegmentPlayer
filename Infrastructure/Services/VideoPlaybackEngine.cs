using System.Windows;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using PortablePlayer.Application.Interfaces;
using PortablePlayer.Domain.Models;
using PlayerMediaType = PortablePlayer.Domain.Enums.MediaType;

namespace PortablePlayer.Infrastructure.Services;

public sealed class VideoPlaybackEngine : IPlaybackEngine
{
    private readonly object _sync = new();
    private LibVLC? _libVlc;
    private MediaPlayer? _activePlayer;
    private MediaPlayer? _standbyPlayer;
    private Media? _activeMedia;
    private Media? _standbyMedia;
    private PlaybackRequest? _preloadedRequest;

    public VideoPlaybackEngine()
    {
        LibVLCSharp.Shared.Core.Initialize();
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
        ((VideoView)View).MediaPlayer = _activePlayer;
        return Task.CompletedTask;
    }

    public Task PlayAsync(PlaybackRequest request, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        if (!File.Exists(request.FullPath))
        {
            PlaybackFailed?.Invoke(this, new PlaybackEngineError
            {
                Message = $"Media file not found: {request.FullPath}",
            });
            return Task.CompletedTask;
        }

        lock (_sync)
        {
            if (_preloadedRequest is not null &&
                string.Equals(_preloadedRequest.FullPath, request.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                PromotePreloadedInternal();
                _activePlayer!.Time = 0;
                _activePlayer.Play();
                _preloadedRequest = null;
                return Task.CompletedTask;
            }
        }

        _activeMedia?.Dispose();
        _activeMedia = new Media(_libVlc!, new Uri(request.FullPath));
        _activePlayer!.Stop();
        if (!_activePlayer.Play(_activeMedia))
        {
            PlaybackFailed?.Invoke(this, new PlaybackEngineError
            {
                Message = $"Unable to play video: {request.FullPath}",
            });
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_activePlayer is not null)
        {
            _activePlayer.Stop();
        }

        if (_standbyPlayer is not null)
        {
            _standbyPlayer.Stop();
        }

        _preloadedRequest = null;
        return Task.CompletedTask;
    }

    public async Task PreloadAsync(PlaybackRequest request, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        if (!File.Exists(request.FullPath))
        {
            return;
        }

        lock (_sync)
        {
            _preloadedRequest = request;
        }

        _standbyMedia?.Dispose();
        _standbyMedia = new Media(_libVlc!, new Uri(request.FullPath));
        _standbyPlayer!.Stop();
        var ok = _standbyPlayer.Play(_standbyMedia);
        if (!ok)
        {
            lock (_sync)
            {
                _preloadedRequest = null;
            }

            return;
        }

        try
        {
            await Task.Delay(130, cancellationToken).ConfigureAwait(false);
            _standbyPlayer.Pause();
            _standbyPlayer.Time = 0;
        }
        catch
        {
            lock (_sync)
            {
                _preloadedRequest = null;
            }
        }
    }

    public Task PromotePreloadedAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        lock (_sync)
        {
            PromotePreloadedInternal();
            _preloadedRequest = null;
        }

        _activePlayer!.Play();
        return Task.CompletedTask;
    }

    private void PromotePreloadedInternal()
    {
        if (_activePlayer is null || _standbyPlayer is null)
        {
            return;
        }

        _activePlayer.Stop();
        UnwireActiveEvents(_activePlayer);
        (_activePlayer, _standbyPlayer) = (_standbyPlayer, _activePlayer);
        (_activeMedia, _standbyMedia) = (_standbyMedia, _activeMedia);
        WireActiveEvents(_activePlayer);
        ((VideoView)View).MediaPlayer = _activePlayer;
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
        PlaybackCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void OnEncounteredError(object? sender, EventArgs e)
    {
        PlaybackFailed?.Invoke(this, new PlaybackEngineError
        {
            Message = "Video engine encountered an error.",
        });
    }

    public void Dispose()
    {
        if (_activePlayer is not null)
        {
            UnwireActiveEvents(_activePlayer);
            _activePlayer.Dispose();
        }

        _standbyPlayer?.Dispose();
        _activeMedia?.Dispose();
        _standbyMedia?.Dispose();
        _libVlc?.Dispose();
    }
}
