namespace MdbConverter.Core.Models;

public sealed class ForeignKeySchema
{
    public required string Name { get; init; }
    public required string FromTable { get; init; }
    public required IReadOnlyList<string> FromColumns { get; init; }
    public required string ToTable { get; init; }
    public required IReadOnlyList<string> ToColumns { get; init; }
}
