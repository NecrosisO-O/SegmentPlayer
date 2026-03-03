using PortablePlayer.Domain.Enums;
using PortablePlayer.Domain.Models;

namespace PortablePlayer.Application.Interfaces;

public interface IPlaylistService
{
    Task<PlaylistDocument> LoadAsync(string playlistPath, CancellationToken cancellationToken = default);

    Task SaveAsync(string playlistPath, PlaylistDocument document, CancellationToken cancellationToken = default);

    Task<PlaylistDocument> CreateDefaultAsync(string groupPath, MediaType mediaType, CancellationToken cancellationToken = default);

    IReadOnlyList<ValidationIssue> Validate(PlaylistDocument document, MediaType mediaType);
}
