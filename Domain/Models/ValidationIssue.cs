namespace PortablePlayer.Domain.Models;

public sealed class ValidationIssue
{
    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}
