using MdbConverter.Core.Models;

namespace MdbConverter.Core.Export;

public sealed class ExportRequest
{
    public required Catalog Catalog { get; init; }
    public required ObjectSelection Selection { get; init; }
    public required string OutputDirectory { get; init; }
    public required bool WriteJson { get; init; }
    public required bool WritePostgresSql { get; init; }
    public required ConflictMode ConflictMode { get; init; }
}
