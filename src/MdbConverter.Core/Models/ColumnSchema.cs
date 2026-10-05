namespace MdbConverter.Core.Models;

public sealed class ColumnSchema
{
    public required string Name { get; init; }
    public required string AccessTypeName { get; init; }
    public required int OleDbType { get; init; }
    public required string PostgresType { get; init; }
    public bool AllowNull { get; init; } = true;
    public int? MaxLength { get; init; }
    public int? NumericPrecision { get; init; }
    public int? NumericScale { get; init; }
    public bool IsAutoIncrement { get; init; }
}
