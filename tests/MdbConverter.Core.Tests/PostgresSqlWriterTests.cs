using MdbConverter.Core.Models;
using MdbConverter.Core.Postgres;

namespace MdbConverter.Core.Tests;

public class PostgresSqlWriterTests
{
    [Fact]
    public void Replace_drops_creates_inserts_then_foreign_keys()
    {
        var parent = Table("Parent", Col("Id", "bigint", identity: true, nullable: false), Col("Name", "text"));
        parent = WithPk(parent, "Id");
        var child = Table("Child", Col("Id", "bigint", identity: true, nullable: false), Col("ParentId", "bigint", nullable: false));
        child = WithPk(child, "Id");
        var fk = new ForeignKeySchema
        {
            Name = "FK_Child_Parent",
            FromTable = "Child",
            FromColumns = ["ParentId"],
            ToTable = "Parent",
            ToColumns = ["Id"]
        };

        using var writer = new StringWriter();
        using (var sql = new PostgresSqlWriter(writer, ConflictMode.Replace, batchSize: 10))
        {
            sql.WriteHeader();
            sql.WriteTableDefinition(parent);
            sql.WriteInsertBatch(parent, [Row(("Id", 1L), ("Name", "a"))]);
            sql.WriteTableDefinition(child);
            sql.WriteInsertBatch(child, [Row(("Id", 1L), ("ParentId", 1L))]);
            sql.WriteForeignKeys([fk], new HashSet<string> { "Parent", "Child" });
        }

        var text = writer.ToString();
        Assert.Contains("DROP TABLE IF EXISTS \"Parent\" CASCADE;", text, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE \"Parent\"", text, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO \"Parent\"", text, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE \"Child\" ADD CONSTRAINT \"FK_Child_Parent\"", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("INSERT INTO \"Child\"", StringComparison.Ordinal) <
                    text.IndexOf("FOREIGN KEY", StringComparison.Ordinal));
        Assert.DoesNotContain("_mdb_created", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Skip_guards_create_and_insert()
    {
        var table = WithPk(Table("T", Col("Id", "bigint", nullable: false), Col("Name", "text")), "Id");
        using var writer = new StringWriter();
        using (var sql = new PostgresSqlWriter(writer, ConflictMode.Skip))
        {
            sql.WriteHeader();
            sql.WriteTableDefinition(table);
            sql.WriteInsertBatch(table, [Row(("Id", 1L), ("Name", "x"))]);
        }

        var text = writer.ToString();
        Assert.Contains("CREATE TEMP TABLE IF NOT EXISTS _mdb_created", text, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS \"T\"", text, StringComparison.Ordinal);
        Assert.Contains("WHERE EXISTS (SELECT 1 FROM _mdb_created WHERE name = 'T')", text, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Linked_table_definition_has_no_insert()
    {
        var table = Table("Remote", Col("Id", "bigint"));
        using var writer = new StringWriter();
        using (var sql = new PostgresSqlWriter(writer, ConflictMode.Replace))
        {
            sql.WriteTableDefinition(table);
        }

        var text = writer.ToString();
        Assert.Contains("CREATE TABLE \"Remote\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO \"Remote\"", text, StringComparison.Ordinal);
    }

    private static TableSchema Table(string name, params ColumnSchema[] columns) => new()
    {
        Name = name,
        Columns = columns,
        Indexes = [],
        PrimaryKeyColumns = []
    };

    private static TableSchema WithPk(TableSchema table, params string[] columns) => new()
    {
        Name = table.Name,
        IsSystem = table.IsSystem,
        IsLinked = table.IsLinked,
        LinkedSource = table.LinkedSource,
        Columns = table.Columns,
        Indexes = table.Indexes,
        PrimaryKeyColumns = columns
    };

    private static ColumnSchema Col(string name, string pg, bool identity = false, bool nullable = true) => new()
    {
        Name = name,
        AccessTypeName = name,
        OleDbType = 3,
        PostgresType = pg,
        AllowNull = nullable,
        IsAutoIncrement = identity
    };

    private static Dictionary<string, object?> Row(params (string Name, object? Value)[] cells) =>
        cells.ToDictionary(c => c.Name, c => c.Value, StringComparer.Ordinal);
}
