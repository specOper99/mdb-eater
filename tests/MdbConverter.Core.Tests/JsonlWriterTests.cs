using System.Text;
using System.Text.Json;
using MdbConverter.Core.Json;
using MdbConverter.Core.Models;

namespace MdbConverter.Core.Tests;

public class JsonlWriterTests
{
    [Fact]
    public void Writes_null_date_memo_and_base64()
    {
        var columns = new List<ColumnSchema>
        {
            Col("n", "text"),
            Col("d", "timestamp"),
            Col("m", "text"),
            Col("b", "bytea")
        };
        var row = new Dictionary<string, object?>
        {
            ["n"] = null,
            ["d"] = new DateTime(2020, 1, 2, 3, 4, 5),
            ["m"] = "hello\nmemo",
            ["b"] = new byte[] { 1, 2, 255 }
        };

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, JsonlWriter.WriterOptions))
        {
            JsonlWriter.WriteRow(writer, columns, row);
        }

        var json = Encoding.UTF8.GetString(stream.ToArray());
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(JsonValueKind.Null, root.GetProperty("n").ValueKind);
        Assert.Equal("2020-01-02T03:04:05.0000000", root.GetProperty("d").GetString());
        Assert.Equal("hello\nmemo", root.GetProperty("m").GetString());
        Assert.Equal(Convert.ToBase64String(new byte[] { 1, 2, 255 }), root.GetProperty("b").GetString());
    }

    private static ColumnSchema Col(string name, string pg) => new()
    {
        Name = name,
        AccessTypeName = name,
        OleDbType = 0,
        PostgresType = pg
    };
}
