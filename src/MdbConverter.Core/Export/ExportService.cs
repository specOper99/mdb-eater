using System.Globalization;
using System.Text;
using System.Text.Json;
using MdbConverter.Core.Access;
using MdbConverter.Core.IO;
using MdbConverter.Core.Json;
using MdbConverter.Core.Models;
using MdbConverter.Core.Postgres;

namespace MdbConverter.Core.Export;

public sealed class ExportService
{
    private const int MaxInsertBytes = 4 * 1024 * 1024;
    private const int ProgressEveryRows = 5000;
    public ExportResult Run(
        ExportRequest request,
        IMdbSession session,
        IProgress<ExportLogEntry>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!request.WriteJson && !request.WritePostgresSql)
        {
            throw new InvalidOperationException("Select JSON, postgres.sql, or both.");
        }

        Directory.CreateDirectory(request.OutputDirectory);
        var dataDir = Path.Combine(request.OutputDirectory, "data");
        var objectsRoot = Path.Combine(request.OutputDirectory, "objects");
        if (request.WriteJson)
        {
            Directory.CreateDirectory(dataDir);
        }

        Directory.CreateDirectory(Path.Combine(objectsRoot, "forms"));
        Directory.CreateDirectory(Path.Combine(objectsRoot, "reports"));
        Directory.CreateDirectory(Path.Combine(objectsRoot, "macros"));
        Directory.CreateDirectory(Path.Combine(objectsRoot, "modules"));

        var written = 0;
        var skipped = 0;
        var failed = 0;
        var manifestTables = new List<ManifestTable>();
        var exportedQueries = new List<QuerySchema>();
        var manifestUi = new List<ManifestUiObject>();
        var jsonlNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var uiFileNames = new Dictionary<AccessObjectKind, HashSet<string>>();

        StreamWriter? sqlFile = null;
        PostgresSqlWriter? sql = null;
        if (request.WritePostgresSql)
        {
            sqlFile = new StreamWriter(
                Path.Combine(request.OutputDirectory, "postgres.sql"),
                false,
                new UTF8Encoding(false),
                1 << 20);
            sql = new PostgresSqlWriter(sqlFile, request.ConflictMode);
            sql.WriteHeader();
        }

        try
        {
            var tables = request.Catalog.Tables
                .Where(t => request.Selection.TableNames.Contains(t.Name))
                .ToList();

            foreach (var table in tables)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    sql?.WriteTableDefinition(table);

                    string? jsonlRelative = null;
                    string? skipReason = null;
                    long? rowCount = 0;

                    if (table.IsLinked)
                    {
                        skipReason = "Linked table. Rows not read so export stays offline.";
                        Log(progress, LogLevel.Warning, table.Name, skipReason);
                        skipped++;
                    }
                    else
                    {
                        FileStream? jsonlStream = null;
                        MemoryStream? jsonBuffer = null;
                        Utf8JsonWriter? jsonWriter = null;
                        var batch = new List<IReadOnlyDictionary<string, object?>>(sql?.BatchSize ?? 1);
                        var batchBytes = 0;
                        try
                        {
                            if (request.WriteJson)
                            {
                                var jsonlPath = FileNames.Unique(dataDir, FileNames.ForObject(table.Name), ".jsonl", jsonlNames);
                                jsonlRelative = Path.GetRelativePath(request.OutputDirectory, jsonlPath).Replace('\\', '/');
                                jsonlStream = new FileStream(jsonlPath, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
                                jsonBuffer = new MemoryStream(16 * 1024);
                                jsonWriter = new Utf8JsonWriter(jsonBuffer, JsonlWriter.WriterOptions);
                            }

                            Log(progress, LogLevel.Info, table.Name, "Reading rows.", transient: true);
                            foreach (var row in session.ReadRows(table.Name))
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                rowCount++;
                                if (rowCount % ProgressEveryRows == 0)
                                {
                                    Log(progress, LogLevel.Info, table.Name, rowCount.Value.ToString("N0", CultureInfo.InvariantCulture) + " rows", transient: true);
                                }

                                if (jsonWriter is not null && jsonBuffer is not null && jsonlStream is not null)
                                {
                                    jsonBuffer.SetLength(0);
                                    jsonWriter.Reset(jsonBuffer);
                                    JsonlWriter.WriteRow(jsonWriter, table.Columns, row);
                                    jsonWriter.Flush();
                                    jsonlStream.Write(jsonBuffer.GetBuffer(), 0, (int)jsonBuffer.Length);
                                    jsonlStream.WriteByte((byte)'\n');
                                }

                                if (sql is not null)
                                {
                                    batch.Add(row);
                                    batchBytes += EstimateRow(row);
                                    if (batch.Count >= sql.BatchSize || batchBytes >= MaxInsertBytes)
                                    {
                                        sql.WriteInsertBatch(table, batch);
                                        batch.Clear();
                                        batchBytes = 0;
                                    }
                                }
                            }

                            if (sql is not null && batch.Count > 0)
                            {
                                sql.WriteInsertBatch(table, batch);
                            }
                        }
                        finally
                        {
                            jsonWriter?.Dispose();
                            jsonBuffer?.Dispose();
                            jsonlStream?.Dispose();
                        }

                        Log(progress, LogLevel.Info, table.Name, $"Wrote {(rowCount ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture)} rows.");
                        written++;
                    }

                    manifestTables.Add(new ManifestTable
                    {
                        Schema = table,
                        RowCount = table.IsLinked ? null : rowCount,
                        RowsSkippedReason = skipReason,
                        JsonlFile = jsonlRelative
                    });
                }
                catch (Exception ex)
                {
                    failed++;
                    Log(progress, LogLevel.Error, table.Name, ex.Message);
                }
            }

            var queries = request.Catalog.Queries
                .Where(q => request.Selection.QueryNames.Contains(q.Name))
                .ToList();
            IReadOnlyDictionary<string, string?>? querySql = null;

            foreach (var query in queries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var sqlText = query.Sql;
                    if (string.IsNullOrWhiteSpace(sqlText))
                    {
                        querySql ??= session.ReadQuerySql();
                        if (!querySql.TryGetValue(query.Name, out sqlText) || string.IsNullOrWhiteSpace(sqlText))
                        {
                            failed++;
                            Log(progress, LogLevel.Error, query.Name, session.AccessUnavailableReason ?? "Query SQL was not available.");
                            continue;
                        }
                    }

                    var resolved = new QuerySchema { Name = query.Name, Sql = sqlText };
                    exportedQueries.Add(resolved);
                    written++;
                    Log(progress, LogLevel.Info, query.Name, "Stored Access query SQL.");
                }
                catch (Exception ex)
                {
                    failed++;
                    Log(progress, LogLevel.Error, query.Name, ex.Message);
                }
            }

            if (sql is not null && exportedQueries.Count > 0)
            {
                sql.WriteQueriesTable(exportedQueries);
            }

            var selectedTableSet = tables.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
            var fks = request.Catalog.ForeignKeys
                .Where(fk => selectedTableSet.Contains(fk.FromTable) && selectedTableSet.Contains(fk.ToTable))
                .ToList();
            sql?.WriteForeignKeys(fks, selectedTableSet);

            foreach (var ui in request.Catalog.UiObjects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!request.Selection.UiObjects.Contains(new UiObjectKey(ui.Kind, ui.Name)))
                {
                    continue;
                }

                try
                {
                    var dump = session.TryDumpUiObject(ui.Kind, ui.Name);
                    if (!dump.Success)
                    {
                        if (!string.IsNullOrWhiteSpace(dump.SkipReason))
                        {
                            skipped++;
                            manifestUi.Add(new ManifestUiObject
                            {
                                Name = ui.Name,
                                Kind = ui.Kind.ToString(),
                                SkipReason = dump.SkipReason
                            });
                            Log(progress, LogLevel.Warning, ui.Name, dump.SkipReason);
                        }
                        else
                        {
                            failed++;
                            Log(progress, LogLevel.Error, ui.Name, dump.Error ?? "UI object dump failed.");
                        }

                        continue;
                    }

                    string? relative = null;
                    if (dump.Text is not null)
                    {
                        var folder = ui.Kind switch
                        {
                            AccessObjectKind.Form => "forms",
                            AccessObjectKind.Report => "reports",
                            AccessObjectKind.Macro => "macros",
                            AccessObjectKind.Module => "modules",
                            _ => "other"
                        };
                        var dir = Path.Combine(objectsRoot, folder);
                        Directory.CreateDirectory(dir);
                        if (!uiFileNames.TryGetValue(ui.Kind, out var used))
                        {
                            used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            uiFileNames[ui.Kind] = used;
                        }

                        var path = FileNames.Unique(dir, FileNames.ForObject(ui.Name), ".txt", used);
                        File.WriteAllText(path, dump.Text, new UTF8Encoding(false));
                        relative = Path.GetRelativePath(request.OutputDirectory, path).Replace('\\', '/');
                    }

                    manifestUi.Add(new ManifestUiObject
                    {
                        Name = ui.Name,
                        Kind = ui.Kind.ToString(),
                        File = relative
                    });
                    written++;
                    Log(progress, LogLevel.Info, ui.Name, "Wrote " + ui.Kind + ".");
                }
                catch (Exception ex)
                {
                    failed++;
                    Log(progress, LogLevel.Error, ui.Name, ex.Message);
                }
            }

            if (request.WriteJson)
            {
                var manifest = new ManifestDocument
                {
                    SourcePath = request.Catalog.SourcePath,
                    ExportedAt = DateTime.UtcNow.ToString("o"),
                    ConflictMode = request.ConflictMode.ToString(),
                    Tables = manifestTables,
                    Queries = exportedQueries,
                    ForeignKeys = fks,
                    UiObjects = manifestUi
                };
                var manifestPath = Path.Combine(request.OutputDirectory, "manifest.json");
                File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, ManifestJson.Options), new UTF8Encoding(false));
            }
        }
        finally
        {
            sql?.Dispose();
            sqlFile?.Dispose();
        }

        Log(progress, LogLevel.Info, "summary", $"Written {written}, skipped {skipped}, failed {failed}.");
        return new ExportResult { Written = written, Skipped = skipped, Failed = failed };
    }

    private static int EstimateRow(IReadOnlyDictionary<string, object?> row)
    {
        var bytes = 0;
        foreach (var value in row.Values)
        {
            bytes += value switch
            {
                string text => text.Length,
                byte[] blob => blob.Length,
                null => 1,
                _ => 8
            };
        }

        return bytes;
    }

    private static void Log(IProgress<ExportLogEntry>? progress, LogLevel level, string name, string message, bool transient = false) =>
        progress?.Report(new ExportLogEntry(level, name, message) { Transient = transient });
}
