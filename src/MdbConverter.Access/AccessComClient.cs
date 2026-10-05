using System.Globalization;
using System.Reflection;
using MdbConverter.Core.Models;

namespace MdbConverter.Access;

internal sealed class AccessComClient : IDisposable
{
    public const int AcForm = 2;
    public const int AcReport = 3;
    public const int AcMacro = 4;
    public const int AcModule = 5;

    private object? _app;
    private bool _databaseOpen;

    public string? SkipReason { get; private set; }
    public bool IsAvailable => _app is not null;

    public static AccessComClient TryStart(OpenOptions options)
    {
        var client = new AccessComClient();
        try
        {
            var type = Type.GetTypeFromProgID("Access.Application");
            if (type is null)
            {
                client.SkipReason = "Microsoft Access is not installed. Forms, reports, macros, modules, and some query SQL were skipped.";
                return client;
            }

            client._app = Activator.CreateInstance(type)
                ?? throw new InvalidOperationException("Access.Application could not be created.");
            Set(client._app, "Visible", false);
            try
            {
                // msoAutomationSecurityForceDisable. Stops AutoExec from scanning or rewriting on open.
                Set(client._app, "AutomationSecurity", 3);
            }
            catch (Exception)
            {
                // Older Access builds may not expose AutomationSecurity.
            }

            if (!string.IsNullOrWhiteSpace(options.WorkgroupPath))
            {
                var engine = Get(client._app, "DBEngine");
                if (engine is not null)
                {
                    Set(engine, "SystemDB", options.WorkgroupPath);
                    if (!string.IsNullOrWhiteSpace(options.WorkgroupUser))
                    {
                        Set(engine, "DefaultUser", options.WorkgroupUser);
                    }

                    Set(engine, "DefaultPassword", options.WorkgroupPassword ?? string.Empty);
                }
            }

            if (string.IsNullOrEmpty(options.DatabasePassword))
            {
                Invoke(client._app, "OpenCurrentDatabase", options.MdbPath);
            }
            else
            {
                Invoke(client._app, "OpenCurrentDatabase", options.MdbPath, false, options.DatabasePassword);
            }

            client._databaseOpen = true;
        }
        catch (Exception ex)
        {
            client.SkipReason = "Microsoft Access could not open this database: " + Unwind(ex);
            client.Release();
        }

        return client;
    }

    public IReadOnlyDictionary<string, HashSet<string>>? ReadAutoIncrementColumns()
    {
        if (_app is null)
        {
            return null;
        }

        try
        {
            var db = Invoke(_app, "CurrentDb");
            if (db is null)
            {
                return null;
            }

            return IdentityColumns.FromDatabase(db);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public IReadOnlyList<QuerySchema> ReadQueries()
    {
        if (_app is null)
        {
            return [];
        }

        var queries = new List<QuerySchema>();
        try
        {
            var db = Invoke(_app, "CurrentDb");
            if (db is null)
            {
                return queries;
            }

            var defs = Get(db, "QueryDefs");
            if (defs is null)
            {
                return queries;
            }

            var count = Convert.ToInt32(Get(defs, "Count"), CultureInfo.InvariantCulture);
            for (var i = 0; i < count; i++)
            {
                var query = Invoke(defs, "Item", i) ?? Invoke(defs, "Item", i + 1);
                if (query is null)
                {
                    continue;
                }

                var name = Convert.ToString(Get(query, "Name"), CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(name) || name.StartsWith('~'))
                {
                    continue;
                }

                var sql = Convert.ToString(Get(query, "SQL"), CultureInfo.InvariantCulture);
                queries.Add(new QuerySchema { Name = name, Sql = sql });
            }
        }
        catch (Exception)
        {
            // Query SQL still attempted from OleDb views.
        }

        return queries;
    }

    public IReadOnlyList<UiObjectInfo> ListUiObjects()
    {
        if (_app is null)
        {
            return [];
        }

        var list = new List<UiObjectInfo>();
        AddCollection(list, AccessObjectKind.Form, "AllForms");
        AddCollection(list, AccessObjectKind.Report, "AllReports");
        AddCollection(list, AccessObjectKind.Macro, "AllMacros");
        AddCollection(list, AccessObjectKind.Module, "AllModules");
        return list;
    }

    public UiDumpResult SaveAsText(AccessObjectKind kind, string name)
    {
        if (_app is null)
        {
            return new UiDumpResult
            {
                Success = false,
                SkipReason = SkipReason ?? "Microsoft Access is not installed. Forms, reports, macros, and modules were skipped."
            };
        }

        var acType = kind switch
        {
            AccessObjectKind.Form => AcForm,
            AccessObjectKind.Report => AcReport,
            AccessObjectKind.Macro => AcMacro,
            AccessObjectKind.Module => AcModule,
            _ => -1
        };

        if (acType < 0)
        {
            return new UiDumpResult { Success = false, Error = "Unsupported Access object kind." };
        }

        var path = Path.Combine(Path.GetTempPath(), "mdb-converter-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            Invoke(_app, "SaveAsText", acType, name, path);
            var text = File.ReadAllText(path);
            return new UiDumpResult { Success = true, Text = text };
        }
        catch (Exception ex)
        {
            return new UiDumpResult { Success = false, Error = Unwind(ex) };
        }
        finally
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // Temp file cleanup is best-effort.
            }
        }
    }

    public void Dispose() => Release();

    private void AddCollection(List<UiObjectInfo> list, AccessObjectKind kind, string collectionName)
    {
        try
        {
            var project = Get(_app!, "CurrentProject");
            if (project is null)
            {
                return;
            }

            var collection = Get(project, collectionName);
            if (collection is null)
            {
                return;
            }

            var count = Convert.ToInt32(Get(collection, "Count"), CultureInfo.InvariantCulture);
            for (var i = 0; i < count; i++)
            {
                var item = Invoke(collection, "Item", i) ?? Invoke(collection, "Item", i + 1);
                var name = Convert.ToString(item is null ? null : Get(item, "Name"), CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    list.Add(new UiObjectInfo { Name = name, Kind = kind });
                }
            }
        }
        catch (Exception)
        {
            // Collection listing is best-effort.
        }
    }

    private void Release()
    {
        if (_app is not null)
        {
            try
            {
                if (_databaseOpen)
                {
                    Invoke(_app, "CloseCurrentDatabase");
                }
            }
            catch (Exception)
            {
                // Closing is best-effort.
            }

            try
            {
                Invoke(_app, "Quit", 2);
            }
            catch (Exception)
            {
                try
                {
                    Invoke(_app, "Quit");
                }
                catch (Exception)
                {
                    // Process may already have exited.
                }
            }
        }

        _databaseOpen = false;
        if (_app is not null)
        {
            try
            {
                MarshalRelease(_app);
            }
            catch (Exception)
            {
                // RCW release is best-effort.
            }
        }

        _app = null;
    }

    private static void MarshalRelease(object com)
    {
        if (OperatingSystem.IsWindows())
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(com);
        }
    }

    private static object? Get(object target, string name) =>
        target.GetType().InvokeMember(name, BindingFlags.GetProperty | BindingFlags.Public | BindingFlags.Instance, null, target, null);

    private static void Set(object target, string name, object? value) =>
        target.GetType().InvokeMember(name, BindingFlags.SetProperty | BindingFlags.Public | BindingFlags.Instance, null, target, [value]);

    private static object? Invoke(object target, string name, params object?[] args) =>
        target.GetType().InvokeMember(name, BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance, null, target, args);

    private static string Unwind(Exception ex) =>
        ex.InnerException?.Message ?? ex.Message;
}
