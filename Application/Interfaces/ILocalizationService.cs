namespace PortablePlayer.Application.Interfaces;

public interface ILocalizationService
{
    string CurrentLanguage { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    void SetLanguage(string languageCode);

    string Get(string key);
}
