using MdbConverter.Core.Access;
using MdbConverter.Core.Export;
using MdbConverter.Core.Models;

namespace MdbConverter.Core.Tests;

public class ExportServiceTests
{
    [Fact]
    public void Linked_table_writes_metadata_without_reading_rows()
    {
        var local = new TableSchema
        {
            Name = "Local",
            Columns =
            [
                new ColumnSchema { Name = "Id", AccessTypeName = "Long", OleDbType = 3, PostgresType = "bigint", AllowNull = false }
            ],
            Indexes = [],
            PrimaryKeyColumns = ["Id"]
        };
        var linked = new TableSchema
        {
            Name = "Remote",
            IsLinked = true,
            LinkedSource = "C:\\other.mdb | OtherTable",
            Columns =
            [
                new ColumnSchema { Name = "Id", AccessTypeName = "Long", OleDbType = 3, PostgresType = "bigint" }
            ],
            Indexes = [],
            PrimaryKeyColumns = []
        };
        var catalog = new Catalog
        {
            SourcePath = "/tmp/sample.mdb",
            Tables = [local, linked],
            Queries = [],
            ForeignKeys =
            [
                new ForeignKeySchema
                {
                    Name = "FK_Local_Remote",
                    FromTable = "Local",
                    FromColumns = ["Id"],
                    ToTable = "Remote",
                    ToColumns = ["Id"]
                }
            ],
            UiObjects = []
        };
        var session = new FakeSession
        {
            Catalog = catalog,
            Rows =
            {
                ["Local"] = [new Dictionary<string, object?> { ["Id"] = 7L }]
            }
        };

        var output = Path.Combine(Path.GetTempPath(), "mdb-converter-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        try
        {
            var result = new ExportService().Run(
                new ExportRequest
                {
                    Catalog = catalog,
                    Selection = new ObjectSelection
                    {
                        TableNames = new HashSet<string> { "Local", "Remote" },
                        QueryNames = new HashSet<string>(),
                        UiObjects = new HashSet<UiObjectKey>()
                    },
                    OutputDirectory = output,
                    WriteJson = true,
                    WritePostgresSql = true,
                    ConflictMode = ConflictMode.Replace
                },
                session);

            Assert.Equal(1, result.Written);
            Assert.Equal(1, result.Skipped);
            Assert.Equal(0, result.Failed);
            Assert.DoesNotContain("Remote", session.RowReads);
            Assert.Contains("Local", session.RowReads);

            var sql = File.ReadAllText(Path.Combine(output, "postgres.sql"));
            Assert.Contains("CREATE TABLE \"Remote\"", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("INSERT INTO \"Remote\"", sql, StringComparison.Ordinal);
            Assert.Contains("INSERT INTO \"Local\"", sql, StringComparison.Ordinal);
            Assert.Contains("FOREIGN KEY", sql, StringComparison.Ordinal);
            Assert.True(sql.LastIndexOf("INSERT INTO \"Local\"", StringComparison.Ordinal) <
                        sql.IndexOf("FOREIGN KEY", StringComparison.Ordinal));

            var jsonl = File.ReadAllText(Path.Combine(output, "data", "Local.jsonl"));
            Assert.Contains("7", jsonl, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(output, "data", "Remote.jsonl")));
            Assert.True(File.Exists(Path.Combine(output, "manifest.json")));
        }
        finally
        {
            Directory.Delete(output, true);
        }
    }

    [Fact]
    public void Missing_query_sql_fails_that_object_only()
    {
        var catalog = new Catalog
        {
            SourcePath = "x.mdb",
            Tables = [],
            Queries = [new QuerySchema { Name = "Q1", Sql = null }],
            ForeignKeys = [],
            UiObjects = []
        };
        var output = Path.Combine(Path.GetTempPath(), "mdb-converter-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        try
        {
            var result = new ExportService().Run(
                new ExportRequest
                {
                    Catalog = catalog,
                    Selection = new ObjectSelection
                    {
                        TableNames = new HashSet<string>(),
                        QueryNames = new HashSet<string> { "Q1" },
                        UiObjects = new HashSet<UiObjectKey>()
                    },
                    OutputDirectory = output,
                    WriteJson = true,
                    WritePostgresSql = true,
                    ConflictMode = ConflictMode.Skip
                },
                new FakeSession { Catalog = catalog });

            Assert.Equal(0, result.Written);
            Assert.Equal(1, result.Failed);
        }
        finally
        {
            Directory.Delete(output, true);
        }
    }

    private sealed class FakeSession : IMdbSession
    {
        public required Catalog Catalog { get; init; }
        public Dictionary<string, List<Dictionary<string, object?>>> Rows { get; } = new(StringComparer.Ordinal);
        public List<string> RowReads { get; } = [];

        public Catalog ReadCatalog() => Catalog;

        public IEnumerable<IReadOnlyDictionary<string, object?>> ReadRows(string tableName)
        {
            RowReads.Add(tableName);
            return Rows.TryGetValue(tableName, out var rows) ? rows : [];
        }

        public UiDumpResult TryDumpUiObject(AccessObjectKind kind, string name) =>
            new() { Success = false, SkipReason = "Microsoft Access is not installed." };

        public void Dispose()
        {
        }
    }
}
