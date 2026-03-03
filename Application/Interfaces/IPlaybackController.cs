using System.Collections.ObjectModel;
using System.Windows;
using PortablePlayer.Domain.Enums;
using PortablePlayer.Domain.Models;

namespace PortablePlayer.Application.Interfaces;

public interface IPlaybackController : IDisposable
{
    PlaybackMode Mode { get; }

    PlaybackStatus Status { get; }

    int CurrentIndex { get; }

    ReadOnlyCollection<ResolvedPlaylistItem> Items { get; }

    FrameworkElement? CurrentView { get; }

    string ModeIndicator { get; }

    event EventHandler? StateChanged;

    Task LoadAsync(
        GroupDescriptor descriptor,
        PlaylistDocument document,
        CancellationToken cancellationToken = default);

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task ToggleModeAsync(CancellationToken cancellationToken = default);

    Task NextAsync(CancellationToken cancellationToken = default);

    Task PreviousAsync(CancellationToken cancellationToken = default);

    Task NextGroupAsync(CancellationToken cancellationToken = default);

    Task PreviousGroupAsync(CancellationToken cancellationToken = default);

    Task JumpToIndexAsync(int index, CancellationToken cancellationToken = default);

    bool CanGoNext();

    bool CanGoPrevious();

    bool CanGoNextGroup();

    bool CanGoPreviousGroup();
}
