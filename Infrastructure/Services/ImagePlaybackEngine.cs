using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PortablePlayer.Application.Interfaces;
using PortablePlayer.Domain.Enums;
using PortablePlayer.Domain.Models;
using PortablePlayer.Infrastructure.Diagnostics;

namespace PortablePlayer.Infrastructure.Services;

public sealed class ImagePlaybackEngine : IPlaybackEngine
{
    private readonly Stopwatch _stopwatch = new();
    private readonly DispatcherTimer _timer;
    private BitmapSource? _preloaded;
    private string? _preloadedPath;
    private double _durationSeconds;
    private int _lastSecond;

    public ImagePlaybackEngine()
    {
        AppLog.Info("ImageEngine", "Constructed.");
        var image = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        View = image;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        _timer.Tick += OnTimerTick;
    }

    public MediaType SupportedType => MediaType.Image;

    public FrameworkElement View { get; }

    public event EventHandler? PlaybackCompleted;

    public event EventHandler<PlaybackEngineError>? PlaybackFailed;

    public event EventHandler<int>? IntegerProgressChanged;

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task PlayAsync(PlaybackRequest request, CancellationToken cancellationToken = default)
    {
        AppLog.Info("ImageEngine", $"PlayAsync requested. File={request.FullPath}, Duration={request.DurationSeconds}");
        if (!File.Exists(request.FullPath))
        {
            PlaybackFailed?.Invoke(this, new PlaybackEngineError
            {
                Message = $"Image file not found: {request.FullPath}",
            });
            AppLog.Warn("ImageEngine", $"PlayAsync failed: file missing ({request.FullPath}).");
            return;
        }

        try
        {
            BitmapSource source;
            if (_preloaded is not null && string.Equals(_preloadedPath, request.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                source = _preloaded;
            }
            else
            {
                source = await Task.Run(() => LoadBitmap(request.FullPath), cancellationToken).ConfigureAwait(false);
            }

            await View.Dispatcher.InvokeAsync(() =>
            {
                ((Image)View).Source = source;
            });

            _durationSeconds = request.DurationSeconds;
            _stopwatch.Restart();
            _lastSecond = 0;
            IntegerProgressChanged?.Invoke(this, 0);

            if (_durationSeconds > 0)
            {
                _timer.Start();
            }
            else
            {
                _timer.Stop();
            }
            AppLog.Info("ImageEngine", $"PlayAsync display ready. Duration={_durationSeconds}, TimerActive={_durationSeconds > 0}");
        }
        catch (Exception ex)
        {
            PlaybackFailed?.Invoke(this, new PlaybackEngineError
            {
                Message = "Failed to display image.",
                Exception = ex,
            });
            AppLog.Error("ImageEngine", "PlayAsync failed to display image.", ex);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        AppLog.Info("ImageEngine", "StopAsync requested.");
        _timer.Stop();
        _stopwatch.Reset();
        _lastSecond = 0;
        return Task.CompletedTask;
    }

    public async Task PreloadAsync(PlaybackRequest request, CancellationToken cancellationToken = default)
    {
        AppLog.Info("ImageEngine", $"PreloadAsync requested. File={request.FullPath}");
        if (!File.Exists(request.FullPath))
        {
            AppLog.Warn("ImageEngine", $"PreloadAsync skipped because file missing ({request.FullPath}).");
            return;
        }

        _preloaded = await Task.Run(() => LoadBitmap(request.FullPath), cancellationToken).ConfigureAwait(false);
        _preloadedPath = request.FullPath;
    }

    public Task PromotePreloadedAsync(CancellationToken cancellationToken = default)
    {
        if (_preloaded is null)
        {
            return Task.CompletedTask;
        }

        return View.Dispatcher.InvokeAsync(() =>
        {
            ((Image)View).Source = _preloaded;
        }).Task;
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        var elapsed = _stopwatch.Elapsed.TotalSeconds;
        var sec = (int)Math.Min(Math.Floor(elapsed), int.MaxValue);
        if (sec != _lastSecond)
        {
            _lastSecond = sec;
            IntegerProgressChanged?.Invoke(this, sec);
        }

        if (elapsed >= _durationSeconds)
        {
            _timer.Stop();
            AppLog.Info("ImageEngine", $"Timer reached duration {_durationSeconds}. PlaybackCompleted raised.");
            PlaybackCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private static BitmapSource LoadBitmap(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path);
        image.EndInit();
        image.Freeze();
        return image;
    }

    public void Dispose()
    {
        AppLog.Info("ImageEngine", "Dispose called.");
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
    }
}
