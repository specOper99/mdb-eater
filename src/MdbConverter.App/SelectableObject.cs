using System.ComponentModel;
using MdbConverter.Core.Models;

namespace MdbConverter.App;

public sealed class SelectableObject : INotifyPropertyChanged
{
    private bool _isSelected;

    public SelectableObject(AccessObjectKind kind, string name, string? badge, string? detail, bool selected)
    {
        Kind = kind;
        Name = name;
        Badge = badge ?? string.Empty;
        Detail = detail ?? string.Empty;
        _isSelected = selected;
    }

    public AccessObjectKind Kind { get; }
    public string Name { get; }
    public string Badge { get; }
    public string Detail { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public string KindLabel => Kind.ToString();

    public event PropertyChangedEventHandler? PropertyChanged;
}
