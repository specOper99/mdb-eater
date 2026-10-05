namespace MdbConverter.Core.Models;

public sealed class ExportResult
{
    public int Written { get; init; }
    public int Skipped { get; init; }
    public int Failed { get; init; }
}
