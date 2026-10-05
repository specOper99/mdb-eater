using System.Globalization;
using System.Text.Json;
using MdbConverter.Core.Models;

namespace MdbConverter.Core.Json;

public static class JsonlWriter
{
    public static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = false,
        SkipValidation = false
    };

    public static void WriteRow(Utf8JsonWriter writer, IReadOnlyList<ColumnSchema> columns, IReadOnlyDictionary<string, object?> values)
    {
        writer.WriteStartObject();
        foreach (var column in columns)
        {
            writer.WritePropertyName(column.Name);
            values.TryGetValue(column.Name, out var value);
            WriteValue(writer, value);
        }

        writer.WriteEndObject();
    }

    public static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
            case DBNull:
                writer.WriteNullValue();
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case byte or sbyte or short or ushort or int or uint:
                writer.WriteNumberValue(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case ulong ul when ul <= long.MaxValue:
                writer.WriteNumberValue((long)ul);
                break;
            case float f when float.IsFinite(f):
                writer.WriteNumberValue(f);
                break;
            case double d when double.IsFinite(d):
                writer.WriteNumberValue(d);
                break;
            case decimal m:
                writer.WriteNumberValue(m);
                break;
            case DateTime dt:
                writer.WriteStringValue(dt.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture));
                break;
            case DateTimeOffset dto:
                writer.WriteStringValue(dto.ToString("o", CultureInfo.InvariantCulture));
                break;
            case Guid g:
                writer.WriteStringValue(g.ToString());
                break;
            case byte[] bytes:
                writer.WriteBase64StringValue(bytes);
                break;
            case TimeSpan ts:
                writer.WriteStringValue(ts.ToString("c", CultureInfo.InvariantCulture));
                break;
            default:
                writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }
}
