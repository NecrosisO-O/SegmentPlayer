using PortablePlayer.Domain.Models;

namespace PortablePlayer.Application.Interfaces;

public interface IGroupScanner
{
    Task<IReadOnlyList<GroupDescriptor>> ScanAsync(CancellationToken cancellationToken = default);

    Task<RefreshDiff> BuildRefreshDiffAsync(GroupDescriptor descriptor, CancellationToken cancellationToken = default);
}
