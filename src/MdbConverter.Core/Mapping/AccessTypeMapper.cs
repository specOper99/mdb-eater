namespace MdbConverter.Core.Mapping;

public sealed record TypeMapping(string PostgresType, string AccessTypeName, bool IsAutoIncrement);

public static class AccessTypeMapper
{
    public static TypeMapping Map(int oleDbType, string? typeName, bool isAutoIncrement)
    {
        var normalized = (typeName ?? string.Empty).Trim();
        if (normalized.Equals("COUNTER", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("AUTOINCREMENT", StringComparison.OrdinalIgnoreCase))
        {
            return new TypeMapping("bigint", normalized, true);
        }

        var accessName = string.IsNullOrWhiteSpace(normalized)
            ? NameForOleDb(oleDbType)
            : normalized;

        var postgres = oleDbType switch
        {
            11 => "boolean",
            16 or 17 => "smallint",
            2 or 18 => "integer",
            3 or 19 => "bigint",
            20 or 21 => "bigint",
            4 or 5 => "double precision",
            6 or 14 or 131 or 139 => "numeric",
            7 or 64 or 133 or 134 or 135 => "timestamp",
            72 => "uuid",
            128 or 204 or 205 => "bytea",
            8 or 129 or 130 or 200 or 201 or 202 or 203 => "text",
            _ => "text"
        };

        if (isAutoIncrement && postgres is "integer" or "bigint" or "smallint")
        {
            return new TypeMapping(postgres, accessName, true);
        }

        return new TypeMapping(postgres, accessName, isAutoIncrement);
    }

    public static string NameForOleDb(int oleDbType) => oleDbType switch
    {
        2 => "Integer",
        3 => "Long",
        4 => "Single",
        5 => "Double",
        6 => "Currency",
        7 => "DateTime",
        11 => "Boolean",
        14 => "Decimal",
        16 => "TinyInt",
        17 => "Byte",
        20 => "BigInt",
        72 => "GUID",
        128 => "Binary",
        129 => "Char",
        130 => "WChar",
        131 => "Numeric",
        133 => "DBDate",
        134 => "DBTime",
        135 => "DBTimeStamp",
        200 => "VarChar",
        201 => "LongVarChar",
        202 => "VarWChar",
        203 => "LongVarWChar",
        204 => "VarBinary",
        205 => "LongVarBinary",
        _ => $"OleDb({oleDbType})"
    };
}
