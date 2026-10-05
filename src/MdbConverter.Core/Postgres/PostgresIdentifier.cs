using System.Globalization;
using System.Text;

namespace MdbConverter.Core.Postgres;

public static class PostgresIdentifier
{
    public static string Quote(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    public static string Literal(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }
}

public static class PostgresLiteral
{
    public static string Format(object? value, string postgresType)
    {
        if (value is null || value is DBNull)
        {
            return "NULL";
        }

        return postgresType switch
        {
            "boolean" => FormatBoolean(value),
            "smallint" or "integer" or "bigint" => FormatInteger(value),
            "double precision" => FormatDouble(value),
            "numeric" => FormatNumeric(value),
            "timestamp" => FormatTimestamp(value),
            "uuid" => FormatUuid(value),
            "bytea" => FormatBytea(value),
            _ => PostgresIdentifier.Literal(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)
        };
    }

    private static string FormatBoolean(object value) =>
        value switch
        {
            bool b => b ? "TRUE" : "FALSE",
            sbyte or byte or short or ushort or int or uint or long or ulong =>
                Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0 ? "TRUE" : "FALSE",
            string s when bool.TryParse(s, out var parsed) => parsed ? "TRUE" : "FALSE",
            _ => Convert.ToInt32(value, CultureInfo.InvariantCulture) != 0 ? "TRUE" : "FALSE"
        };

    private static string FormatInteger(object value) =>
        Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);

    private static string FormatDouble(object value)
    {
        var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        if (double.IsNaN(number) || double.IsInfinity(number))
        {
            return "NULL";
        }

        return number.ToString("G17", CultureInfo.InvariantCulture);
    }

    private static string FormatNumeric(object value) =>
        Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);

    private static string FormatTimestamp(object value)
    {
        var dt = value switch
        {
            DateTimeOffset dto => dto.UtcDateTime,
            DateTime d => DateTime.SpecifyKind(d, DateTimeKind.Unspecified),
            _ => Convert.ToDateTime(value, CultureInfo.InvariantCulture)
        };

        return "TIMESTAMP " + PostgresIdentifier.Literal(dt.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture));
    }

    private static string FormatUuid(object value)
    {
        var guid = value switch
        {
            Guid g => g,
            string s => Guid.Parse(s),
            byte[] bytes when bytes.Length == 16 => new Guid(bytes),
            _ => Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)
        };

        return PostgresIdentifier.Literal(guid.ToString()) + "::uuid";
    }

    private static string FormatBytea(object value)
    {
        var bytes = value switch
        {
            byte[] b => b,
            ReadOnlyMemory<byte> m => m.ToArray(),
            _ => throw new InvalidOperationException($"Cannot store {value.GetType().Name} as bytea.")
        };

        var sb = new StringBuilder(bytes.Length * 2 + 8);
        sb.Append("'\\x");
        foreach (var b in bytes)
        {
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        sb.Append("'::bytea");
        return sb.ToString();
    }
}
