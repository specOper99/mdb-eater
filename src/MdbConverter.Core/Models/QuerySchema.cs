namespace MdbConverter.Core.Models;

public sealed class QuerySchema
{
    public required string Name { get; init; }
    public string? Sql { get; init; }
}
