using MdbConverter.Core.Models;

namespace MdbConverter.Core.Access;

public interface IMdbSession : IDisposable
{
    Catalog ReadCatalog();
    IEnumerable<IReadOnlyDictionary<string, object?>> ReadRows(string tableName);
    IReadOnlyDictionary<string, string?> ReadQuerySql();
    string? AccessUnavailableReason { get; }
    UiDumpResult TryDumpUiObject(AccessObjectKind kind, string name);
}
