using PortablePlayer.Domain.Models;

namespace PortablePlayer.Application.Interfaces;

public interface ISettingsService
{
    string AppRoot { get; }

    string SettingsPath { get; }

    AppSettings Current { get; }

    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);

    string ResolveFromAppRoot(string relativeOrAbsolutePath);
}
