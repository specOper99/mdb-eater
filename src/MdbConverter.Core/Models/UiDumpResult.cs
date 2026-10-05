namespace MdbConverter.Core.Models;

public sealed class UiDumpResult
{
    public bool Success { get; init; }
    public string? Text { get; init; }
    public string? SkipReason { get; init; }
    public string? Error { get; init; }
}
