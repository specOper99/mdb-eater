namespace MdbConverter.Core.Models;

public sealed class OpenOptions
{
    public required string MdbPath { get; init; }
    public string? DatabasePassword { get; init; }
    public string? WorkgroupPath { get; init; }
    public string? WorkgroupUser { get; init; }
    public string? WorkgroupPassword { get; init; }
}
