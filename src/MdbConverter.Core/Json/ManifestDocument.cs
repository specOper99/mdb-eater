using System.Text.Json;
using System.Text.Json.Serialization;
using MdbConverter.Core.Models;

namespace MdbConverter.Core.Json;

public sealed class ManifestDocument
{
    public required string SourcePath { get; init; }
    public required string ExportedAt { get; init; }
    public required string ConflictMode { get; init; }
    public required IReadOnlyList<ManifestTable> Tables { get; init; }
    public required IReadOnlyList<QuerySchema> Queries { get; init; }
    public required IReadOnlyList<ForeignKeySchema> ForeignKeys { get; init; }
    public required IReadOnlyList<ManifestUiObject> UiObjects { get; init; }
}

public sealed class ManifestTable
{
    public required TableSchema Schema { get; init; }
    public long? RowCount { get; init; }
    public string? RowsSkippedReason { get; init; }
    public string? JsonlFile { get; init; }
}

public sealed class ManifestUiObject
{
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public string? File { get; init; }
    public string? SkipReason { get; init; }
}

public static class ManifestJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
