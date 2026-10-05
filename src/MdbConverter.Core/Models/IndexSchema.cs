namespace MdbConverter.Core.Models;

public sealed class IndexSchema
{
    public required string Name { get; init; }
    public required IReadOnlyList<string> Columns { get; init; }
    public bool IsPrimaryKey { get; init; }
    public bool IsUnique { get; init; }
}
