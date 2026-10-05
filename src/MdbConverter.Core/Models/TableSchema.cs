namespace MdbConverter.Core.Models;

public sealed class TableSchema
{
    public required string Name { get; init; }
    public bool IsSystem { get; init; }
    public bool IsLinked { get; init; }
    public string? LinkedSource { get; init; }
    public required IReadOnlyList<ColumnSchema> Columns { get; init; }
    public required IReadOnlyList<IndexSchema> Indexes { get; init; }
    public required IReadOnlyList<string> PrimaryKeyColumns { get; init; }
}
