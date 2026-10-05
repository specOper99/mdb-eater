using MdbConverter.Core.Models;

namespace MdbConverter.App;

public sealed class SelectableObject
{
    public SelectableObject(AccessObjectKind kind, string name, string? badge, string? detail, bool selected)
    {
        Kind = kind;
        Name = name;
        Badge = badge ?? string.Empty;
        Detail = detail ?? string.Empty;
        IsSelected = selected;
    }

    public AccessObjectKind Kind { get; }
    public string Name { get; }
    public string Badge { get; }
    public string Detail { get; }
    public bool IsSelected { get; set; }
    public string KindLabel => Kind.ToString();
}
