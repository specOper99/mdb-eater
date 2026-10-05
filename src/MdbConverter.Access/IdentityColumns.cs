using System.Globalization;
using System.Reflection;
using MdbConverter.Core.Models;

namespace MdbConverter.Access;

internal static class IdentityColumns
{
    private const int DbAttachedTable = 0x40000000;
    private const int DbAttachedOdbc = 0x20000000;
    private const int DbAutoIncrField = 0x10;

    public static Dictionary<string, HashSet<string>> FromDatabase(object database)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var defs = Get(database, "TableDefs");
        if (defs is null)
        {
            return map;
        }

        var count = Convert.ToInt32(Get(defs, "Count"), CultureInfo.InvariantCulture);
        for (var i = 0; i < count; i++)
        {
            try
            {
                var table = Invoke(defs, "Item", i);
                if (table is null)
                {
                    continue;
                }

                var name = Convert.ToString(Get(table, "Name"), CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(name) || name.StartsWith('~'))
                {
                    continue;
                }

                var attributes = ToInt(Get(table, "Attributes"));
                var connect = Convert.ToString(Get(table, "Connect"), CultureInfo.InvariantCulture);
                if (IsLinked(attributes, connect))
                {
                    continue;
                }

                var fields = Get(table, "Fields");
                if (fields is null)
                {
                    continue;
                }

                var fieldCount = Convert.ToInt32(Get(fields, "Count"), CultureInfo.InvariantCulture);
                HashSet<string>? names = null;
                for (var f = 0; f < fieldCount; f++)
                {
                    var field = Invoke(fields, "Item", f);
                    if (field is null)
                    {
                        continue;
                    }

                    if ((ToInt(Get(field, "Attributes")) & DbAutoIncrField) == 0)
                    {
                        continue;
                    }

                    var fieldName = Convert.ToString(Get(field, "Name"), CultureInfo.InvariantCulture);
                    if (string.IsNullOrWhiteSpace(fieldName))
                    {
                        continue;
                    }

                    names ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    names.Add(fieldName);
                }

                if (names is not null)
                {
                    map[name] = names;
                }
            }
            catch (Exception)
            {
                // One bad table definition must not block the catalog.
            }
        }

        return map;
    }

    public static IReadOnlyDictionary<string, HashSet<string>>? TryRead(OpenOptions options)
    {
        object? engine = null;
        try
        {
            var type = Type.GetTypeFromProgID("DAO.DBEngine.120")
                ?? Type.GetTypeFromProgID("DAO.DBEngine.36");
            if (type is null)
            {
                return null;
            }

            engine = Activator.CreateInstance(type);
            if (engine is null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(options.WorkgroupPath))
            {
                Set(engine, "SystemDB", options.WorkgroupPath);
                if (!string.IsNullOrWhiteSpace(options.WorkgroupUser))
                {
                    Set(engine, "DefaultUser", options.WorkgroupUser);
                }

                Set(engine, "DefaultPassword", options.WorkgroupPassword ?? string.Empty);
            }

            var connect = string.IsNullOrEmpty(options.DatabasePassword)
                ? string.Empty
                : ";PWD=" + options.DatabasePassword;
            var database = Invoke(engine, "OpenDatabase", options.MdbPath, false, true, connect);
            if (database is null)
            {
                return null;
            }

            try
            {
                return FromDatabase(database);
            }
            finally
            {
                try
                {
                    Invoke(database, "Close");
                }
                catch (Exception)
                {
                    // Close is best-effort.
                }
            }
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (engine is not null && OperatingSystem.IsWindows())
            {
                try
                {
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(engine);
                }
                catch (Exception)
                {
                    // RCW release is best-effort.
                }
            }
        }
    }

    private static bool IsLinked(int attributes, string? connect) =>
        (attributes & DbAttachedTable) != 0
        || (attributes & DbAttachedOdbc) != 0
        || !string.IsNullOrWhiteSpace(connect);

    private static int ToInt(object? value) =>
        value is null or DBNull ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);

    private static object? Get(object target, string name) =>
        target.GetType().InvokeMember(name, BindingFlags.GetProperty | BindingFlags.Public | BindingFlags.Instance, null, target, null);

    private static void Set(object target, string name, object? value) =>
        target.GetType().InvokeMember(name, BindingFlags.SetProperty | BindingFlags.Public | BindingFlags.Instance, null, target, [value]);

    private static object? Invoke(object target, string name, params object?[] args) =>
        target.GetType().InvokeMember(name, BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance, null, target, args);
}
