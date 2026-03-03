using System.Collections.ObjectModel;
using System.Windows;
using PortablePlayer.Application.Interfaces;
using PortablePlayer.Domain.Enums;
using PortablePlayer.Domain.Models;

namespace PortablePlayer.Infrastructure.Services;

public sealed class PlaybackController : IPlaybackController
{
    private readonly IPlaybackEngine _videoEngine;
    private readonly IPlaybackEngine _imageEngine;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<ResolvedPlaylistItem> _items = [];
    private readonly List<GroupSegment> _groupSegments = [];

    private PlaybackMode _mode = PlaybackMode.Auto;
    private PlaybackStatus _status = PlaybackStatus.Stopped;
    private int _currentIndex = -1;
    private FrameworkElement? _currentView;
    private int _videoCurrentLoop;
    private int _videoTotalLoop;
    private int _imageCurrentSecond;
    private int _imageTotalSecond;
    private MediaType _groupMediaType;

    public PlaybackController(IPlaybackEngine videoEngine, IPlaybackEngine imageEngine)
    {
        _videoEngine = videoEngine;
        _imageEngine = imageEngine;

        _videoEngine.PlaybackCompleted += OnPlaybackCompleted;
        _videoEngine.PlaybackFailed += OnPlaybackFailed;

        _imageEngine.PlaybackCompleted += OnPlaybackCompleted;
        _imageEngine.PlaybackFailed += OnPlaybackFailed;
        _imageEngine.IntegerProgressChanged += OnImageIntegerProgressChanged;
    }

    public PlaybackMode Mode => _mode;

    public PlaybackStatus Status => _status;

    public int CurrentIndex => _currentIndex;

    public ReadOnlyCollection<ResolvedPlaylistItem> Items => _items.AsReadOnly();

    public FrameworkElement? CurrentView => _currentView;

    public string ModeIndicator
    {
        get
        {
            if (_mode == PlaybackMode.Manual)
            {
                return "M";
            }

            if (_currentIndex < 0 || _currentIndex >= _items.Count)
            {
                return "A";
            }

            var item = _items[_currentIndex];
            if (item.Loops < 0)
            {
                return "A X";
            }

            if (_groupMediaType == MediaType.Video)
            {
                var current = Math.Max(_videoCurrentLoop, 1);
                var total = Math.Max(_videoTotalLoop, 1);
                return $"A {current}/{total}";
            }

            var imageCurrent = Math.Max(_imageCurrentSecond, 0);
            var imageTotal = Math.Max(_imageTotalSecond, 0);
            return $"A {imageCurrent}/{imageTotal}";
        }
    }

    public event EventHandler? StateChanged;

    public async Task LoadAsync(
        GroupDescriptor descriptor,
        PlaylistDocument document,
        CancellationToken cancellationToken = default)
    {
        _groupMediaType = descriptor.MediaType;
        _mode = PlaybackMode.Auto;
        _status = PlaybackStatus.Stopped;
        _items.Clear();

        for (var i = 0; i < document.Items.Count; i++)
        {
            var item = document.Items[i];
            var fullPath = Path.Combine(descriptor.FullPath, item.File);
            _items.Add(new ResolvedPlaylistItem
            {
                Index = i,
                FileName = item.File,
                FullPath = fullPath,
                Group = item.Group,
                Loops = item.Loops,
                Missing = item.Missing || !File.Exists(fullPath),
            });
        }

        BuildGroupSegments();

        await _videoEngine.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _imageEngine.InitializeAsync(cancellationToken).ConfigureAwait(false);

        _currentView = descriptor.MediaType == MediaType.Video ? _videoEngine.View : _imageEngine.View;
        _currentIndex = -1;
        _videoCurrentLoop = 0;
        _videoTotalLoop = 0;
        _imageCurrentSecond = 0;
        _imageTotalSecond = 0;
        NotifyStateChanged();
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _status = PlaybackStatus.Playing;
            _currentIndex = FindPlayableForward(0);
            if (_currentIndex < 0)
            {
                _status = PlaybackStatus.Completed;
                NotifyStateChanged();
                return;
            }

            await PlayCurrentAsync(resetProgress: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _status = PlaybackStatus.Stopped;
            await _videoEngine.StopAsync(cancellationToken).ConfigureAwait(false);
            await _imageEngine.StopAsync(cancellationToken).ConfigureAwait(false);
            NotifyStateChanged();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ToggleModeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _mode = _mode == PlaybackMode.Auto ? PlaybackMode.Manual : PlaybackMode.Auto;
            if (_currentIndex >= 0)
            {
                await PlayCurrentAsync(resetProgress: true, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                NotifyStateChanged();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task NextAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var next = FindPlayableForward(_currentIndex + 1);
            if (next < 0)
            {
                return;
            }

            _currentIndex = next;
            _status = PlaybackStatus.Playing;
            await PlayCurrentAsync(resetProgress: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task PreviousAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var prev = FindPlayableBackward(_currentIndex - 1);
            if (prev < 0)
            {
                return;
            }

            _currentIndex = prev;
            _status = PlaybackStatus.Playing;
            await PlayCurrentAsync(resetProgress: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task NextGroupAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var currentGroup = ResolveCurrentGroupSegmentIndex();
            var nextSegment = FindNextPlayableSegment(currentGroup + 1);
            if (nextSegment < 0)
            {
                return;
            }

            _currentIndex = _groupSegments[nextSegment].StartIndex;
            _status = PlaybackStatus.Playing;
            await PlayCurrentAsync(resetProgress: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task PreviousGroupAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var currentGroup = ResolveCurrentGroupSegmentIndex();
            var prevSegment = FindPreviousPlayableSegment(currentGroup - 1);
            if (prevSegment < 0)
            {
                return;
            }

            _currentIndex = _groupSegments[prevSegment].StartIndex;
            _status = PlaybackStatus.Playing;
            await PlayCurrentAsync(resetProgress: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task JumpToIndexAsync(int index, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_items.Count == 0)
            {
                return;
            }

            var target = Math.Clamp(index, 0, _items.Count - 1);
            if (!IsPlayable(_items[target]))
            {
                target = FindPlayableForward(target);
            }

            if (target < 0)
            {
                _status = PlaybackStatus.Completed;
                NotifyStateChanged();
                return;
            }

            _currentIndex = target;
            _status = PlaybackStatus.Playing;
            await PlayCurrentAsync(resetProgress: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool CanGoNext() => FindPlayableForward(_currentIndex + 1) >= 0;

    public bool CanGoPrevious() => FindPlayableBackward(_currentIndex - 1) >= 0;

    public bool CanGoNextGroup()
    {
        var current = ResolveCurrentGroupSegmentIndex();
        return FindNextPlayableSegment(current + 1) >= 0;
    }

    public bool CanGoPreviousGroup()
    {
        var current = ResolveCurrentGroupSegmentIndex();
        return FindPreviousPlayableSegment(current - 1) >= 0;
    }

    private async Task PlayCurrentAsync(bool resetProgress, CancellationToken cancellationToken)
    {
        if (_currentIndex < 0 || _currentIndex >= _items.Count)
        {
            _status = PlaybackStatus.Completed;
            NotifyStateChanged();
            return;
        }

        var item = _items[_currentIndex];
        if (!IsPlayable(item))
        {
            if (_mode == PlaybackMode.Auto)
            {
                var next = FindPlayableForward(_currentIndex + 1);
                if (next < 0)
                {
                    _status = PlaybackStatus.Completed;
                    NotifyStateChanged();
                    return;
                }

                _currentIndex = next;
                await PlayCurrentAsync(resetProgress: true, cancellationToken).ConfigureAwait(false);
                return;
            }

            _status = PlaybackStatus.Error;
            NotifyStateChanged();
            return;
        }

        var activeEngine = _groupMediaType == MediaType.Video ? _videoEngine : _imageEngine;
        var inactiveEngine = _groupMediaType == MediaType.Video ? _imageEngine : _videoEngine;

        await inactiveEngine.StopAsync(cancellationToken).ConfigureAwait(false);
        _currentView = activeEngine.View;

        if (_mode == PlaybackMode.Auto)
        {
            if (_groupMediaType == MediaType.Video)
            {
                if (resetProgress)
                {
                    _videoTotalLoop = item.Loops < 0 ? 0 : (int)Math.Round(item.Loops);
                    _videoCurrentLoop = item.Loops < 0 ? 0 : 1;
                }
            }
            else
            {
                if (resetProgress)
                {
                    _imageTotalSecond = item.Loops < 0 ? 0 : (int)Math.Floor(item.Loops);
                    _imageCurrentSecond = 0;
                }
            }
        }
        else if (resetProgress)
        {
            _videoTotalLoop = 0;
            _videoCurrentLoop = 0;
            _imageCurrentSecond = 0;
            _imageTotalSecond = 0;
        }

        var request = new PlaybackRequest
        {
            FullPath = item.FullPath,
            MediaType = _groupMediaType,
            DurationSeconds = ResolveDuration(item),
        };

        await activeEngine.PlayAsync(request, cancellationToken).ConfigureAwait(false);
        _status = PlaybackStatus.Playing;
        NotifyStateChanged();

        _ = Task.Run(async () =>
        {
            var nextIndex = FindPlayableForward(_currentIndex + 1);
            if (nextIndex < 0)
            {
                return;
            }

            var next = _items[nextIndex];
            var preloadRequest = new PlaybackRequest
            {
                FullPath = next.FullPath,
                MediaType = _groupMediaType,
                DurationSeconds = ResolveDuration(next),
            };

            try
            {
                await activeEngine.PreloadAsync(preloadRequest).ConfigureAwait(false);
            }
            catch
            {
                // Preload failure should never break playback.
            }
        });
    }

    private double ResolveDuration(ResolvedPlaylistItem item)
    {
        if (_groupMediaType == MediaType.Video)
        {
            return 0;
        }

        if (_mode == PlaybackMode.Manual)
        {
            return -1;
        }

        if (item.Loops < 0)
        {
            return -1;
        }

        return item.Loops;
    }

    private async void OnPlaybackCompleted(object? sender, EventArgs e)
    {
        try
        {
            await _gate.WaitAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (_currentIndex < 0 || _currentIndex >= _items.Count)
            {
                return;
            }

            var item = _items[_currentIndex];
            if (_mode == PlaybackMode.Manual)
            {
                if (_groupMediaType == MediaType.Video)
                {
                    await PlayCurrentAsync(resetProgress: false, CancellationToken.None).ConfigureAwait(false);
                }

                return;
            }

            if (_groupMediaType == MediaType.Video)
            {
                if (item.Loops < 0)
                {
                    await PlayCurrentAsync(resetProgress: false, CancellationToken.None).ConfigureAwait(false);
                    return;
                }

                if (_videoCurrentLoop < _videoTotalLoop)
                {
                    _videoCurrentLoop++;
                    NotifyStateChanged();
                    await PlayCurrentAsync(resetProgress: false, CancellationToken.None).ConfigureAwait(false);
                    return;
                }

                var next = FindPlayableForward(_currentIndex + 1);
                if (next < 0)
                {
                    _status = PlaybackStatus.Completed;
                    NotifyStateChanged();
                    return;
                }

                _currentIndex = next;
                await PlayCurrentAsync(resetProgress: true, CancellationToken.None).ConfigureAwait(false);
                return;
            }

            if (item.Loops < 0)
            {
                return;
            }

            var imageNext = FindPlayableForward(_currentIndex + 1);
            if (imageNext < 0)
            {
                _status = PlaybackStatus.Completed;
                NotifyStateChanged();
                return;
            }

            _currentIndex = imageNext;
            await PlayCurrentAsync(resetProgress: true, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async void OnPlaybackFailed(object? sender, PlaybackEngineError e)
    {
        try
        {
            await _gate.WaitAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (_mode == PlaybackMode.Auto)
            {
                var next = FindPlayableForward(_currentIndex + 1);
                if (next >= 0)
                {
                    _currentIndex = next;
                    await PlayCurrentAsync(resetProgress: true, CancellationToken.None).ConfigureAwait(false);
                    return;
                }
            }

            _status = PlaybackStatus.Error;
            NotifyStateChanged();
        }
        finally
        {
            _gate.Release();
        }
    }

    private void OnImageIntegerProgressChanged(object? sender, int currentSecond)
    {
        if (_groupMediaType != MediaType.Image || _mode != PlaybackMode.Auto)
        {
            return;
        }

        _imageCurrentSecond = Math.Max(0, currentSecond);
        NotifyStateChanged();
    }

    private int FindPlayableForward(int startInclusive)
    {
        for (var i = Math.Max(0, startInclusive); i < _items.Count; i++)
        {
            if (IsPlayable(_items[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private int FindPlayableBackward(int startInclusive)
    {
        for (var i = Math.Min(startInclusive, _items.Count - 1); i >= 0; i--)
        {
            if (IsPlayable(_items[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private bool IsPlayable(ResolvedPlaylistItem item)
    {
        if (item.Missing)
        {
            return false;
        }

        if (item.Loops == 0)
        {
            return false;
        }

        return File.Exists(item.FullPath);
    }

    private void BuildGroupSegments()
    {
        _groupSegments.Clear();
        if (_items.Count == 0)
        {
            return;
        }

        var cursor = 0;
        while (cursor < _items.Count)
        {
            var groupNumber = _items[cursor].Group;
            if (groupNumber is null)
            {
                _groupSegments.Add(new GroupSegment
                {
                    GroupNumber = null,
                    StartIndex = cursor,
                    EndIndex = cursor,
                });
                cursor++;
                continue;
            }

            var end = cursor;
            while (end + 1 < _items.Count && _items[end + 1].Group == groupNumber)
            {
                end++;
            }

            _groupSegments.Add(new GroupSegment
            {
                GroupNumber = groupNumber,
                StartIndex = cursor,
                EndIndex = end,
            });
            cursor = end + 1;
        }
    }

    private int ResolveCurrentGroupSegmentIndex()
    {
        if (_groupSegments.Count == 0)
        {
            return -1;
        }

        if (_currentIndex < 0)
        {
            return 0;
        }

        for (var i = 0; i < _groupSegments.Count; i++)
        {
            var segment = _groupSegments[i];
            if (_currentIndex >= segment.StartIndex && _currentIndex <= segment.EndIndex)
            {
                return i;
            }
        }

        return 0;
    }

    private int FindNextPlayableSegment(int startSegment)
    {
        if (_groupSegments.Count == 0)
        {
            return -1;
        }

        for (var i = Math.Max(0, startSegment); i < _groupSegments.Count; i++)
        {
            var seg = _groupSegments[i];
            for (var idx = seg.StartIndex; idx <= seg.EndIndex; idx++)
            {
                if (IsPlayable(_items[idx]))
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private int FindPreviousPlayableSegment(int startSegment)
    {
        if (_groupSegments.Count == 0)
        {
            return -1;
        }

        for (var i = Math.Min(startSegment, _groupSegments.Count - 1); i >= 0; i--)
        {
            var seg = _groupSegments[i];
            for (var idx = seg.StartIndex; idx <= seg.EndIndex; idx++)
            {
                if (IsPlayable(_items[idx]))
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private void NotifyStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        _videoEngine.PlaybackCompleted -= OnPlaybackCompleted;
        _videoEngine.PlaybackFailed -= OnPlaybackFailed;
        _imageEngine.PlaybackCompleted -= OnPlaybackCompleted;
        _imageEngine.PlaybackFailed -= OnPlaybackFailed;
        _imageEngine.IntegerProgressChanged -= OnImageIntegerProgressChanged;

        _videoEngine.Dispose();
        _imageEngine.Dispose();
        _gate.Dispose();
    }

    private sealed class GroupSegment
    {
        public int? GroupNumber { get; init; }

        public int StartIndex { get; init; }

        public int EndIndex { get; init; }
    }
}
