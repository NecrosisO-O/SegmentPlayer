namespace PortablePlayer.Domain.Models;

public sealed class PlaybackEngineError
{
    public string Message { get; set; } = string.Empty;

    public Exception? Exception { get; set; }
}
