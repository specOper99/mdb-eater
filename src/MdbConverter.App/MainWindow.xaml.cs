using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MdbConverter.Access;
using MdbConverter.Core.Access;
using MdbConverter.Core.Export;
using MdbConverter.Core.Models;
using MdbConverter.Core.Settings;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace MdbConverter.App;

public sealed partial class MainWindow : Window
{
    private readonly SettingsStore _settingsStore = new();
    private readonly StaWorker _sta = new();
    private readonly ObservableCollection<SelectableObject> _objects = [];
    private readonly StringBuilder _log = new();
    private AppSettings _settings;
    private IMdbSession? _session;
    private Catalog? _catalog;
    private int _step;
    private bool _busy;
    private bool _suppressSelectionEvents;
    private bool _uiReady;
    private CancellationTokenSource? _exportCts;

    public MainWindow()
    {
        _settings = _settingsStore.Load();
        InitializeComponent();
        ExtendsContentIntoTitleBar = false;
        MdbPathBox.Text = _settings.LastMdbPath ?? string.Empty;
        MdwPathBox.Text = _settings.LastWorkgroupPath ?? string.Empty;
        WorkgroupUserBox.Text = _settings.LastWorkgroupUser ?? string.Empty;
        OutputFolderBox.Text = _settings.LastOutputFolder ?? string.Empty;
        WriteJsonBox.IsChecked = _settings.WriteJson;
        WriteSqlBox.IsChecked = _settings.WritePostgresSql;
        SetConflict(_settings.ConflictMode);
        ObjectList.ItemsSource = _objects;
        Closed += (_, _) => CloseSession();
        ShowStep(0);
        _uiReady = true;
    }

    private async void BrowseMdb_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializePicker(picker);
        picker.FileTypeFilter.Add(".mdb");
        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            MdbPathBox.Text = file.Path;
        }
    }

    private async void BrowseMdw_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializePicker(picker);
        picker.FileTypeFilter.Add(".mdw");
        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            MdwPathBox.Text = file.Path;
        }
    }

    private async void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        InitializePicker(picker);
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            OutputFolderBox.Text = folder.Path;
        }
    }

    private void ConflictButtons_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady)
        {
            return;
        }

        _settings.ConflictMode = CurrentConflict();
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        if (_step == 0)
        {
            await ScanAsync();
            return;
        }

        if (_step == 1)
        {
            ShowStep(2);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        if (_step > 0)
        {
            ShowStep(_step - 1);
        }
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) => SetAllSelected(true);

    private void SelectNone_Click(object sender, RoutedEventArgs e) => SetAllSelected(false);

    private void SetAllSelected(bool selected)
    {
        _suppressSelectionEvents = true;
        foreach (var item in _objects)
        {
            item.IsSelected = selected;
        }

        _suppressSelectionEvents = false;
        UpdateObjectCount();
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            _exportCts?.Cancel();
            ExportStatus.Text = "Canceling…";
            return;
        }

        if (_session is null || _catalog is null)
        {
            return;
        }

        var output = OutputFolderBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(output))
        {
            ExportStatus.Text = "Choose an output folder.";
            return;
        }

        var writeJson = WriteJsonBox.IsChecked == true;
        var writeSql = WriteSqlBox.IsChecked == true;
        if (!writeJson && !writeSql)
        {
            ExportStatus.Text = "Select JSON, postgres.sql, or both.";
            return;
        }

        var tables = _objects.Where(o => o.Kind == AccessObjectKind.Table && o.IsSelected).Select(o => o.Name).ToHashSet(StringComparer.Ordinal);
        var queries = _objects.Where(o => o.Kind == AccessObjectKind.Query && o.IsSelected).Select(o => o.Name).ToHashSet(StringComparer.Ordinal);
        var ui = _objects
            .Where(o => o.Kind is AccessObjectKind.Form or AccessObjectKind.Report or AccessObjectKind.Macro or AccessObjectKind.Module && o.IsSelected)
            .Select(o => new UiObjectKey(o.Kind, o.Name))
            .ToHashSet();

        PersistSettings(output, writeJson, writeSql);

        var request = new ExportRequest
        {
            Catalog = _catalog,
            Selection = new ObjectSelection
            {
                TableNames = tables,
                QueryNames = queries,
                UiObjects = ui
            },
            OutputDirectory = output,
            WriteJson = writeJson,
            WritePostgresSql = writeSql,
            ConflictMode = CurrentConflict()
        };

        _busy = true;
        _exportCts = new CancellationTokenSource();
        ExportButton.Content = "Cancel";
        ExportProgress.Visibility = Visibility.Visible;
        ExportStatus.Text = "Exporting…";
        _log.Clear();
        LogBlock.Text = string.Empty;
        SetButtons();

        var progress = new Progress<ExportLogEntry>(entry =>
        {
            if (entry.Transient)
            {
                ExportStatus.Text = entry.ObjectName + ": " + entry.Message;
                return;
            }

            _log.Append('[').Append(entry.Level).Append("] ").Append(entry.ObjectName).Append(": ").AppendLine(entry.Message);
            LogBlock.Text = _log.ToString();
            ExportStatus.Text = entry.ObjectName + ": " + entry.Message;
        });

        try
        {
            var session = _session;
            var token = _exportCts.Token;
            var result = await _sta.RunAsync(() => new ExportService().Run(request, session, progress, token));
            ExportStatus.Text = $"Done. Written {result.Written}, skipped {result.Skipped}, failed {result.Failed}.";
        }
        catch (OperationCanceledException)
        {
            ExportStatus.Text = "Canceled. Output folder may be incomplete.";
        }
        catch (Exception ex)
        {
            ExportStatus.Text = ex.Message;
            ((IProgress<ExportLogEntry>)progress).Report(new ExportLogEntry(LogLevel.Error, "export", ex.Message));
        }
        finally
        {
            _exportCts?.Dispose();
            _exportCts = null;
            _busy = false;
            ExportButton.Content = "Export";
            ExportProgress.Visibility = Visibility.Collapsed;
            SetButtons();
        }
    }

    private async Task ScanAsync()
    {
        var path = MdbPathBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            SourceError.Text = "Choose an existing .mdb file.";
            return;
        }

        if (!path.EndsWith(".mdb", StringComparison.OrdinalIgnoreCase))
        {
            SourceError.Text = "Only .mdb files are supported.";
            return;
        }

        SourceError.Text = string.Empty;
        _busy = true;
        NextButton.Content = "Scanning…";
        ScanProgress.Visibility = Visibility.Visible;
        ScanStatus.Text = "Opening the database and reading schema. Rows stay on disk until export.";
        SetButtons();

        CloseSession();
        var options = new OpenOptions
        {
            MdbPath = path,
            DatabasePassword = EmptyToNull(DbPasswordBox.Password),
            WorkgroupPath = EmptyToNull(MdwPathBox.Text),
            WorkgroupUser = EmptyToNull(WorkgroupUserBox.Text),
            WorkgroupPassword = EmptyToNull(WorkgroupPasswordBox.Password)
        };

        try
        {
            var opened = await _sta.RunAsync(() =>
            {
                var session = MdbSessionFactory.Open(options);
                var catalog = session.ReadCatalog();
                return (session, catalog);
            });
            _session = opened.session;
            _catalog = opened.catalog;
            LoadObjects(_catalog);
            PersistSource(path);
            if (!string.IsNullOrWhiteSpace(_catalog.AccessComStatus) && _catalog.AccessComStatus != "available")
            {
                SourceError.Text = _catalog.AccessComStatus;
            }

            ShowStep(1);
        }
        catch (Exception ex)
        {
            SourceError.Text = ex.InnerException?.Message ?? ex.Message;
        }
        finally
        {
            _busy = false;
            ScanProgress.Visibility = Visibility.Collapsed;
            ScanStatus.Text = string.Empty;
            NextButton.Content = _step == 0 ? "Scan" : "Next";
            SetButtons();
        }
    }

    private void LoadObjects(Catalog catalog)
    {
        _objects.Clear();
        foreach (var table in catalog.Tables.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            var badges = new List<string>();
            if (table.IsSystem)
            {
                badges.Add("system");
            }

            if (table.IsLinked)
            {
                badges.Add("linked");
            }

            var item = new SelectableObject(AccessObjectKind.Table, table.Name, string.Join(", ", badges), table.LinkedSource, selected: !table.IsSystem);
            item.PropertyChanged += OnObjectChanged;
            _objects.Add(item);
        }

        foreach (var query in catalog.Queries.OrderBy(q => q.Name, StringComparer.OrdinalIgnoreCase))
        {
            var item = new SelectableObject(AccessObjectKind.Query, query.Name, string.Empty, query.Sql, selected: true);
            item.PropertyChanged += OnObjectChanged;
            _objects.Add(item);
        }

        foreach (var ui in catalog.UiObjects.OrderBy(o => o.Kind).ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase))
        {
            var item = new SelectableObject(ui.Kind, ui.Name, string.Empty, null, selected: true);
            item.PropertyChanged += OnObjectChanged;
            _objects.Add(item);
        }

        UpdateObjectCount();
    }

    private void OnObjectChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressSelectionEvents || e.PropertyName != nameof(SelectableObject.IsSelected))
        {
            return;
        }

        UpdateObjectCount();
    }

    private void UpdateObjectCount()
    {
        var selected = 0;
        foreach (var item in _objects)
        {
            if (item.IsSelected)
            {
                selected++;
            }
        }

        ObjectCountLabel.Text = selected + " of " + _objects.Count + " selected";
    }

    private void ShowStep(int step)
    {
        _step = step;
        SourcePanel.Visibility = step == 0 ? Visibility.Visible : Visibility.Collapsed;
        ObjectsPanel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        ExportPanel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        StepLabel.Text = step switch
        {
            0 => "1. Source",
            1 => "2. Objects",
            _ => "3. Export"
        };
        NextButton.Content = step == 0 ? "Scan" : "Next";
        NextButton.Visibility = step < 2 ? Visibility.Visible : Visibility.Collapsed;
        ExportButton.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        SetButtons();
    }

    private void SetButtons()
    {
        BackButton.IsEnabled = !_busy && _step > 0;
        NextButton.IsEnabled = !_busy;
        ExportButton.IsEnabled = !_busy || _exportCts is not null;
    }

    private void PersistSource(string mdbPath)
    {
        _settings.LastMdbPath = mdbPath;
        _settings.LastWorkgroupPath = EmptyToNull(MdwPathBox.Text);
        _settings.LastWorkgroupUser = EmptyToNull(WorkgroupUserBox.Text);
        _settingsStore.Save(_settings);
    }

    private void PersistSettings(string output, bool writeJson, bool writeSql)
    {
        _settings.LastOutputFolder = output;
        _settings.WriteJson = writeJson;
        _settings.WritePostgresSql = writeSql;
        _settings.ConflictMode = CurrentConflict();
        _settingsStore.Save(_settings);
    }

    private ConflictMode CurrentConflict() =>
        ConflictButtons.SelectedIndex == 1 ? ConflictMode.Skip : ConflictMode.Replace;

    private void SetConflict(ConflictMode mode) =>
        ConflictButtons.SelectedIndex = mode == ConflictMode.Skip ? 1 : 0;

    private void InitializePicker(object picker)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, hwnd);
    }

    private void CloseSession()
    {
        var session = _session;
        _session = null;
        _catalog = null;
        if (session is null)
        {
            return;
        }

        try
        {
            _sta.RunAsync(session.Dispose).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            try
            {
                session.Dispose();
            }
            catch (Exception)
            {
                // Session teardown is best-effort.
            }
        }
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
