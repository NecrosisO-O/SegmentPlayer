using System.Windows;
using PortablePlayer.Domain.Enums;
using PortablePlayer.Domain.Models;

namespace PortablePlayer.Application.Interfaces;

public interface IPlaybackEngine : IDisposable
{
    MediaType SupportedType { get; }

    FrameworkElement View { get; }

    event EventHandler? PlaybackCompleted;

    event EventHandler<PlaybackEngineError>? PlaybackFailed;

    event EventHandler<int>? IntegerProgressChanged;

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task PlayAsync(PlaybackRequest request, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task PreloadAsync(PlaybackRequest request, CancellationToken cancellationToken = default);

    Task PromotePreloadedAsync(CancellationToken cancellationToken = default);
}
