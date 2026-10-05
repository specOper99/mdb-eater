using MdbConverter.Core.Models;

namespace MdbConverter.Core.Export;

public sealed class ObjectSelection
{
    public required IReadOnlySet<string> TableNames { get; init; }
    public required IReadOnlySet<string> QueryNames { get; init; }
    public required IReadOnlySet<UiObjectKey> UiObjects { get; init; }
}

public readonly record struct UiObjectKey(AccessObjectKind Kind, string Name);
