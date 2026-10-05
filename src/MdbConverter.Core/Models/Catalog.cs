namespace MdbConverter.Core.Models;

public sealed class Catalog
{
    public required string SourcePath { get; init; }
    public required IReadOnlyList<TableSchema> Tables { get; init; }
    public required IReadOnlyList<QuerySchema> Queries { get; init; }
    public required IReadOnlyList<ForeignKeySchema> ForeignKeys { get; init; }
    public required IReadOnlyList<UiObjectInfo> UiObjects { get; init; }
    public string? AccessComStatus { get; init; }
}
