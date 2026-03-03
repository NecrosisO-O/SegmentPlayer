using System.Windows.Media.Imaging;
using PortablePlayer.Domain.Enums;

namespace PortablePlayer.Application.Interfaces;

public interface IThumbnailService
{
    Task<BitmapSource?> GetThumbnailAsync(
        string mediaPath,
        MediaType mediaType,
        int frameIndex,
        bool useDiskCache,
        CancellationToken cancellationToken = default);
}
