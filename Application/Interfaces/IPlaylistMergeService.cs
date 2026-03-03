using PortablePlayer.Domain.Enums;
using PortablePlayer.Domain.Models;

namespace PortablePlayer.Application.Interfaces;

public interface IPlaylistMergeService
{
    PlaylistDocument MergeWithFilesystem(
        PlaylistDocument existing,
        IReadOnlyCollection<string> mediaFiles,
        MediaType mediaType);
}
