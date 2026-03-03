namespace PortablePlayer.Domain.Models;

public sealed class RefreshDiff
{
    public List<string> AddedFiles { get; set; } = [];

    public List<string> MissingFiles { get; set; } = [];

    public bool HasChanges => AddedFiles.Count > 0 || MissingFiles.Count > 0;
}
