using System.Data;
using System.Data.OleDb;
using System.Globalization;
using MdbConverter.Core.Access;
using MdbConverter.Core.Mapping;
using MdbConverter.Core.Models;

namespace MdbConverter.Access;

public sealed class AceMdbSession : IMdbSession
{
    private readonly OpenOptions _options;
    private readonly OleDbConnection _connection;
    private AccessComClient? _com;
    private Catalog? _catalog;

    public AceMdbSession(OpenOptions options)
    {
        _options = options;
        _connection = AceConnectionFactory.Open(options);
    }

    public string? AccessUnavailableReason => _com is { IsAvailable: false } ? _com.SkipReason : null;

    public Catalog ReadCatalog()
    {
        _catalog ??= BuildCatalog();
        return _catalog;
    }

    public IEnumerable<IReadOnlyDictionary<string, object?>> ReadRows(string tableName)
    {
        var catalog = ReadCatalog();
        var table = catalog.Tables.FirstOrDefault(t => t.Name == tableName)
            ?? throw new InvalidOperationException("Unknown table: " + tableName);
        if (table.IsLinked)
        {
            throw new InvalidOperationException("Linked tables are not read so the export stays offline.");
        }

        using var command = new OleDbCommand("SELECT * FROM " + JetSql.Bracket(tableName), _connection)
        {
            CommandTimeout = 0
        };
        using var reader = command.ExecuteReader(CommandBehavior.SequentialAccess | CommandBehavior.SingleResult);
        var names = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        while (reader.Read())
        {
            var row = new Dictionary<string, object?>(names.Length, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[names[i]] = reader.IsDBNull(i) ? null : Normalize(reader.GetValue(i));
            }

            yield return row;
        }
    }

    public IReadOnlyDictionary<string, string?> ReadQuerySql()
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var query in Com.ReadQueries())
        {
            map[query.Name] = query.Sql;
        }

        return map;
    }

    public UiDumpResult TryDumpUiObject(AccessObjectKind kind, string name) => Com.SaveAsText(kind, name);

    public void Dispose()
    {
        _com?.Dispose();
        _connection.Dispose();
    }

    private AccessComClient Com => _com ??= AccessComClient.TryStart(_options);

    private Catalog BuildCatalog()
    {
        var msys = TryReadMsysObjects();
        var tables = ReadTables(msys);
        var queries = ReadQueries(msys);
        var foreignKeys = ReadForeignKeys();
        var uiObjects = ReadUiObjects(msys);

        return new Catalog
        {
            SourcePath = _options.MdbPath,
            Tables = tables,
            Queries = queries,
            ForeignKeys = foreignKeys,
            UiObjects = uiObjects
        };
    }

    private IReadOnlyList<TableSchema> ReadTables(IReadOnlyDictionary<string, MsysRow> msys)
    {
        var identityByTable = ReadIdentityColumns();
        using var schema = _connection.GetOleDbSchemaTable(OleDbSchemaGuid.Tables, null);
        using var columnSchema = _connection.GetOleDbSchemaTable(OleDbSchemaGuid.Columns, null);
        using var indexSchema = _connection.GetOleDbSchemaTable(OleDbSchemaGuid.Indexes, null);
        var columnsByTable = GroupByTable(columnSchema, "TABLE_NAME");
        var indexesByTable = GroupByTable(indexSchema, "TABLE_NAME");
        var tables = new List<TableSchema>();
        if (schema is null)
        {
            return tables;
        }

        foreach (DataRow row in schema.Rows)
        {
            var name = Convert.ToString(row["TABLE_NAME"], CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var type = Convert.ToString(row["TABLE_TYPE"], CultureInfo.InvariantCulture) ?? string.Empty;
            if (!IsTableType(type))
            {
                continue;
            }

            msys.TryGetValue(name, out var msysRow);
            var isLinked = type.Equals("LINK", StringComparison.OrdinalIgnoreCase)
                || (msysRow is not null && (msysRow.Type is 4 or 6));
            var isSystem = type.Equals("SYSTEM TABLE", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("MSys", StringComparison.OrdinalIgnoreCase)
                || (msysRow is not null && msysRow.Type == 1 && (msysRow.Flags & 0x80000000) != 0 && name.StartsWith("MSys", StringComparison.OrdinalIgnoreCase));

            columnsByTable.TryGetValue(name, out var columnRows);
            indexesByTable.TryGetValue(name, out var indexRows);
            var columns = ReadColumns(name, isLinked, columnRows, columnSchema, identityByTable);
            var indexes = ReadIndexes(indexRows);
            var pk = indexes.FirstOrDefault(i => i.IsPrimaryKey)?.Columns ?? [];

            tables.Add(new TableSchema
            {
                Name = name,
                IsSystem = isSystem || name.StartsWith("MSys", StringComparison.OrdinalIgnoreCase),
                IsLinked = isLinked,
                LinkedSource = isLinked ? LinkedSource(msysRow) : null,
                Columns = columns,
                Indexes = indexes,
                PrimaryKeyColumns = pk
            });
        }

        return tables;
    }

    private IReadOnlyList<ColumnSchema> ReadColumns(
        string tableName,
        bool isLinked,
        List<DataRow>? rows,
        DataTable? schema,
        IReadOnlyDictionary<string, HashSet<string>> identityByTable)
    {
        identityByTable.TryGetValue(tableName, out var autoIncrement);
        var columns = new List<ColumnSchema>();
        if (rows is null || rows.Count == 0 || schema is null)
        {
            return columns;
        }

        var ordered = rows
            .OrderBy(r => Convert.ToInt32(r["ORDINAL_POSITION"], CultureInfo.InvariantCulture))
            .ToList();

        foreach (var row in ordered)
        {
            var name = Convert.ToString(row["COLUMN_NAME"], CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var oleDbType = Convert.ToInt32(row["DATA_TYPE"], CultureInfo.InvariantCulture);
            var typeName = schema.Columns.Contains("TYPE_NAME")
                ? Convert.ToString(row["TYPE_NAME"], CultureInfo.InvariantCulture)
                : null;
            var nullable = IsNullable(row);
            int? maxLength = row["CHARACTER_MAXIMUM_LENGTH"] is DBNull ? null : Convert.ToInt32(row["CHARACTER_MAXIMUM_LENGTH"], CultureInfo.InvariantCulture);
            int? precision = row["NUMERIC_PRECISION"] is DBNull ? null : Convert.ToInt32(row["NUMERIC_PRECISION"], CultureInfo.InvariantCulture);
            int? scale = schema.Columns.Contains("NUMERIC_SCALE") && row["NUMERIC_SCALE"] is not DBNull
                ? Convert.ToInt32(row["NUMERIC_SCALE"], CultureInfo.InvariantCulture)
                : null;
            var identity = !isLinked && autoIncrement is not null && autoIncrement.Contains(name);
            var mapped = AccessTypeMapper.Map(oleDbType, typeName, identity);

            columns.Add(new ColumnSchema
            {
                Name = name,
                AccessTypeName = mapped.AccessTypeName,
                OleDbType = oleDbType,
                PostgresType = mapped.PostgresType,
                AllowNull = nullable,
                MaxLength = maxLength,
                NumericPrecision = precision,
                NumericScale = scale,
                IsAutoIncrement = mapped.IsAutoIncrement
            });
        }

        return columns;
    }

    private IReadOnlyDictionary<string, HashSet<string>> ReadIdentityColumns()
    {
        // DAO field attributes only. SELECT * + GetSchemaTable makes ACE scan every Memo/OLE page
        // to fill ColumnSize, which reads the whole file once per table.
        return IdentityColumns.TryRead(_options)
            ?? Com.ReadAutoIncrementColumns()
            ?? new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, List<DataRow>> GroupByTable(DataTable? schema, string column)
    {
        var map = new Dictionary<string, List<DataRow>>(StringComparer.OrdinalIgnoreCase);
        if (schema is null || !schema.Columns.Contains(column))
        {
            return map;
        }

        foreach (DataRow row in schema.Rows)
        {
            var name = Convert.ToString(row[column], CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!map.TryGetValue(name, out var list))
            {
                list = [];
                map[name] = list;
            }

            list.Add(row);
        }

        return map;
    }

    private static IReadOnlyList<IndexSchema> ReadIndexes(List<DataRow>? tableRows)
    {
        if (tableRows is null || tableRows.Count == 0)
        {
            return [];
        }

        var groups = new Dictionary<string, List<DataRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in tableRows)
        {
            var indexName = Convert.ToString(row["INDEX_NAME"], CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(indexName))
            {
                continue;
            }

            if (!groups.TryGetValue(indexName, out var list))
            {
                list = [];
                groups[indexName] = list;
            }

            list.Add(row);
        }

        var indexes = new List<IndexSchema>();
        foreach (var (name, group) in groups)
        {
            var ordered = group
                .OrderBy(r => Convert.ToInt32(r["ORDINAL_POSITION"], CultureInfo.InvariantCulture))
                .Select(r => Convert.ToString(r["COLUMN_NAME"], CultureInfo.InvariantCulture))
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Cast<string>()
                .ToList();

            var first = group[0];
            indexes.Add(new IndexSchema
            {
                Name = name,
                Columns = ordered,
                IsPrimaryKey = ToBool(first["PRIMARY_KEY"]),
                IsUnique = ToBool(first["UNIQUE"])
            });
        }

        return indexes;
    }

    private IReadOnlyList<ForeignKeySchema> ReadForeignKeys()
    {
        using var schema = _connection.GetOleDbSchemaTable(OleDbSchemaGuid.Foreign_Keys, null);
        if (schema is null)
        {
            return [];
        }

        var groups = new Dictionary<string, List<DataRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (DataRow row in schema.Rows)
        {
            var fkName = Convert.ToString(row["FK_NAME"], CultureInfo.InvariantCulture) ?? "";
            var from = Convert.ToString(row["FK_TABLE_NAME"], CultureInfo.InvariantCulture) ?? "";
            var to = Convert.ToString(row["PK_TABLE_NAME"], CultureInfo.InvariantCulture) ?? "";
            var key = fkName + "\n" + from + "\n" + to;
            if (!groups.TryGetValue(key, out var list))
            {
                list = [];
                groups[key] = list;
            }

            list.Add(row);
        }

        var keys = new List<ForeignKeySchema>();
        foreach (var (key, rows) in groups)
        {
            var ordered = rows
                .OrderBy(r => schema.Columns.Contains("ORDINAL")
                    ? Convert.ToInt32(r["ORDINAL"], CultureInfo.InvariantCulture)
                    : 0)
                .ToList();
            var first = ordered[0];
            keys.Add(new ForeignKeySchema
            {
                Name = Convert.ToString(first["FK_NAME"], CultureInfo.InvariantCulture) ?? key,
                FromTable = Convert.ToString(first["FK_TABLE_NAME"], CultureInfo.InvariantCulture) ?? "",
                FromColumns = ordered.Select(r => Convert.ToString(r["FK_COLUMN_NAME"], CultureInfo.InvariantCulture) ?? "").ToList(),
                ToTable = Convert.ToString(first["PK_TABLE_NAME"], CultureInfo.InvariantCulture) ?? "",
                ToColumns = ordered.Select(r => Convert.ToString(r["PK_COLUMN_NAME"], CultureInfo.InvariantCulture) ?? "").ToList()
            });
        }

        return keys;
    }

    private IReadOnlyList<QuerySchema> ReadQueries(IReadOnlyDictionary<string, MsysRow> msys)
    {
        var byName = new Dictionary<string, QuerySchema>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var views = _connection.GetOleDbSchemaTable(OleDbSchemaGuid.Views, null);
            if (views is not null)
            {
                foreach (DataRow row in views.Rows)
                {
                    var name = Convert.ToString(row["TABLE_NAME"], CultureInfo.InvariantCulture);
                    if (string.IsNullOrWhiteSpace(name) || byName.ContainsKey(name))
                    {
                        continue;
                    }

                    var sql = views.Columns.Contains("VIEW_DEFINITION")
                        ? Convert.ToString(row["VIEW_DEFINITION"], CultureInfo.InvariantCulture)
                        : null;
                    byName[name] = new QuerySchema { Name = name, Sql = sql };
                }
            }
        }
        catch (Exception)
        {
            // Views schema is optional.
        }

        foreach (var row in msys.Values.Where(r => r.Type == 5 && !r.Name.StartsWith('~')))
        {
            if (!byName.ContainsKey(row.Name))
            {
                byName[row.Name] = new QuerySchema { Name = row.Name, Sql = null };
            }
        }

        return byName.Values.OrderBy(q => q.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private IReadOnlyList<UiObjectInfo> ReadUiObjects(IReadOnlyDictionary<string, MsysRow> msys)
    {
        var list = new List<UiObjectInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in msys.Values)
        {
            var kind = row.Type switch
            {
                -32768 => AccessObjectKind.Form,
                -32764 => AccessObjectKind.Report,
                -32766 => AccessObjectKind.Macro,
                -32761 => AccessObjectKind.Module,
                _ => (AccessObjectKind?)null
            };
            if (kind is null)
            {
                continue;
            }

            var key = kind.Value + ":" + row.Name;
            if (seen.Add(key))
            {
                list.Add(new UiObjectInfo { Name = row.Name, Kind = kind.Value });
            }
        }

        return list;
    }

    private IReadOnlyDictionary<string, MsysRow> TryReadMsysObjects()
    {
        var map = new Dictionary<string, MsysRow>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var command = new OleDbCommand(
                "SELECT [Name], [Type], [Flags], [Connect], [Database], [ForeignName] FROM [MSysObjects]",
                _connection);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.IsDBNull(0) ? null : reader.GetValue(0)?.ToString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                map[name] = new MsysRow(
                    name,
                    ToInt(reader.GetValue(1)),
                    ToLong(reader.GetValue(2)),
                    reader.IsDBNull(3) ? null : Convert.ToString(reader.GetValue(3), CultureInfo.InvariantCulture),
                    reader.IsDBNull(4) ? null : Convert.ToString(reader.GetValue(4), CultureInfo.InvariantCulture),
                    reader.IsDBNull(5) ? null : Convert.ToString(reader.GetValue(5), CultureInfo.InvariantCulture));
            }
        }
        catch (Exception)
        {
            // MSysObjects is often locked; OleDb schema still works.
        }

        return map;
    }

    private static string? LinkedSource(MsysRow? row)
    {
        if (row is null)
        {
            return null;
        }

        var parts = new[] { row.Connect, row.Database, row.ForeignName }
            .Where(s => !string.IsNullOrWhiteSpace(s));
        var joined = string.Join(" | ", parts);
        return string.IsNullOrWhiteSpace(joined) ? "linked" : joined;
    }

    private static bool IsTableType(string type) =>
        type.Equals("TABLE", StringComparison.OrdinalIgnoreCase)
        || type.Equals("ACCESS TABLE", StringComparison.OrdinalIgnoreCase)
        || type.Equals("SYSTEM TABLE", StringComparison.OrdinalIgnoreCase)
        || type.Equals("LINK", StringComparison.OrdinalIgnoreCase)
        || type.Equals("PASS-THROUGH", StringComparison.OrdinalIgnoreCase);

    private static bool IsNullable(DataRow row)
    {
        var value = row["IS_NULLABLE"];
        if (value is bool b)
        {
            return b;
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        return text is not ("NO" or "FALSE" or "0");
    }

    private static bool ToBool(object? value) =>
        value switch
        {
            bool b => b,
            IConvertible c => Convert.ToBoolean(c, CultureInfo.InvariantCulture),
            _ => false
        };

    private static int ToInt(object? value) =>
        value is null or DBNull ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);

    private static long ToLong(object? value) =>
        value is null or DBNull ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);

    private static object? Normalize(object value) =>
        value is byte[] or Guid or DateTime or DateTimeOffset or bool or string or decimal or double or float or byte or sbyte or short or ushort or int or uint or long or ulong
            ? value
            : value;

    private sealed record MsysRow(string Name, int Type, long Flags, string? Connect, string? Database, string? ForeignName);
}
