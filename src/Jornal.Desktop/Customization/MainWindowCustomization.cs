using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Jornal.Desktop.Controls;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;

namespace Jornal.Desktop.Customization;

/// <summary>Редактируемая копия компоновки. До «Готово» на диск попадает только прежний вариант.</summary>
public sealed class MainWindowCustomization
{
    private static readonly string[] ColumnIds = ["date", "specification", "manager", "customer", "cutting",
        "material", "thickness", "sheet-size", "sheet-count", "cut-length", "weight", "price", "delivery", "status"];
    private static readonly Dictionary<string, double> MinimumWidths = new()
        { ["orders"] = 312, ["clipboard"] = 430, ["status"] = 428 };
    private readonly MainWindow _window;
    private readonly IUiPreferencesStore _store;
    private readonly DataGrid _table;
    private readonly Grid _groups;
    private readonly Grid _top;
    private readonly Dictionary<string, Border> _panels;
    private readonly ColumnPreference[] _defaultColumns;
    private readonly DispatcherTimer _saveTimer;
    private ScreenLayout _layout = new();
    private ScreenLayout? _beforeEdit;
    private bool _initialized;
    private bool _applying;
    private bool _dirty;
    private LayoutHandle? _drag;
    private string? _dropTarget;
    private double _resizeStart;
    private MainWindowViewModel? Model => _window.DataContext as MainWindowViewModel;
    private T Find<T>(string name) where T : Control => _window.FindControl<T>(name)!;

    public MainWindowCustomization(MainWindow window, IUiPreferencesStore store)
    {
        _window = window;
        _store = store;
        _table = Find<DataGrid>("OrdersGrid");
        _groups = Find<Grid>("CommandGroups");
        _top = Find<Grid>("TopArea");
        _panels = new() { ["orders"] = Find<Border>("OrdersCommands"),
            ["clipboard"] = Find<Border>("ClipboardCommands"), ["status"] = Find<Border>("StatusCommands") };
        _defaultColumns = CaptureColumns();
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick += (_, _) => Flush();
        foreach (var column in _table.Columns.Skip(1)) column.PropertyChanged += OnColumnChanged;
        _table.ColumnReordered += (_, _) => Changed();
        _table.ColumnDisplayIndexChanged += (_, _) => Changed();
        foreach (var name in new[] { "OrdersMoveHandle", "ClipboardMoveHandle", "StatusMoveHandle", "ResultMoveHandle", "ResultResizeHandle" })
        {
            var handle = Find<LayoutHandle>(name);
            handle.MoveStarted += StartDrag;
            handle.MovePoint += MoveDrag;
            handle.MoveFinished += FinishDrag;
            handle.MoveCanceled += (_, _) => CancelDrag();
            handle.KeyDown += OnHandleKeyDown;
        }
        foreach (var name in new[] { "FirstPanelSplitter", "SecondPanelSplitter" })
        {
            var splitter = Find<GridSplitter>(name);
            splitter.DragCompleted += (_, _) => Changed();
            splitter.KeyUp += (_, _) => Changed();
        }
        Find<Grid>("BodyGrid").SizeChanged += (_, _) => UpdateResultSize();
        Find<Grid>("Workspace").SizeChanged += (_, _) => UpdateResultSize();
    }

    public void Initialize()
    {
        if (_initialized) return;
        var loaded = _store.Load();
        _applying = true;
        if (Model is { } model)
        {
            model.PreferencesError = loaded.Error;
            if (loaded.Preferences is { } profile) model.IsDarkTheme = profile.DarkTheme;
        }
        Apply(loaded.Preferences?.Layout ?? new ScreenLayout());
        _applying = false;
        _initialized = true;
    }

    public void Begin()
    {
        if (Model is not { IsCustomizing: false } model) return;
        Flush();
        _beforeEdit = Capture();
        model.IsCustomizing = true;
        _window.Classes.Add("customizing");
        Find<LayoutHandle>("OrdersMoveHandle").Focus();
        UpdateResultSize();
    }

    public bool Save()
    {
        if (_beforeEdit is null) return false;
        CancelDrag();
        var draft = Capture();
        if (!Write(draft)) return false;
        _layout = draft;
        _dirty = false;
        End();
        return true;
    }

    public void Cancel()
    {
        if (_beforeEdit is not { } snapshot) return;
        CancelDrag();
        Apply(snapshot);
        End();
    }

    public void Reset()
    {
        if (_beforeEdit is not null) { CancelDrag(); Apply(new ScreenLayout()); }
    }

    private void End()
    {
        _beforeEdit = null;
        if (Model is { } model) model.IsCustomizing = false;
        _window.Classes.Remove("customizing");
        Find<JournalButton>("PersonalizeButton").Focus();
        UpdateResultSize();
    }

    public void ThemeChanged()
    {
        if (!_initialized || _applying) return;
        // Тема сохраняется сразу, даже если пробную компоновку затем отменят.
        if (_beforeEdit is { } original) Write(original);
        else Changed();
    }

    public void Close()
    {
        Cancel();
        Flush();
        _saveTimer.Stop();
        foreach (var column in _table.Columns.Skip(1)) column.PropertyChanged -= OnColumnChanged;
    }

    public void Flush()
    {
        _saveTimer.Stop();
        if (!_dirty || !_initialized || _beforeEdit is not null) return;
        if (Write(Capture())) _dirty = false;
    }

    private bool Write(ScreenLayout layout)
    {
        try
        {
            _store.Save(new UiPreferences { DarkTheme = Model?.IsDarkTheme == true, Layout = layout });
            if (Model is { } model) model.PreferencesError = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Trace.TraceWarning("UI preferences save failed: {0}", exception);
            if (Model is { } model) model.PreferencesError = "Не удалось сохранить настройки экрана. Проверьте доступ к папке профиля. Изменения пока не записаны.";
            return false;
        }
    }

    private void OnColumnChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.Name == nameof(DataGridColumn.IsVisible)) Changed();
        if (e.Property.Name == nameof(DataGridColumn.Width) && e.OldValue is DataGridLength oldWidth &&
            e.NewValue is DataGridLength newWidth && (oldWidth.Value != newWidth.Value || oldWidth.UnitType != newWidth.UnitType)) Changed();
    }

    private void Changed()
    {
        if (!_initialized || _applying || _beforeEdit is not null) return;
        _dirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private ColumnPreference[] CaptureColumns() => _table.Columns.Skip(1).Select((column, index) =>
        new ColumnPreference(ColumnIds[index], column.DisplayIndex, column.IsVisible,
            column.Width.IsStar, column.Width.Value)).ToArray();

    private ScreenLayout Capture() => (_layout with
    {
        Columns = CaptureColumns(),
        PanelWidths = _panels.ToDictionary(pair => pair.Key,
            pair => Math.Max(MinimumWidths[pair.Key], _groups.ColumnDefinitions[Grid.GetColumn(pair.Value)].ActualWidth))
    }).Normalize();

    private void Apply(ScreenLayout layout)
    {
        var wasApplying = _applying;
        _applying = true;
        _layout = layout.Normalize();
        // Сначала восстанавливаем исходные определения: новые столбцы получают безопасные значения.
        var columns = _defaultColumns.ToDictionary(column => column.Id);
        foreach (var column in _layout.Columns)
            if (columns.ContainsKey(column.Id)) columns[column.Id] = column;
        foreach (var (id, index) in ColumnIds.Select((id, index) => (id, index)))
        {
            var preference = columns[id];
            var column = _table.Columns[index + 1];
            column.Width = new DataGridLength(preference.Width, preference.Star ? DataGridLengthUnitType.Star : DataGridLengthUnitType.Pixel);
            column.IsVisible = preference.Visible;
        }
        var sorted = columns.Values.OrderBy(column => column.Position)
            .ThenBy(column => Array.IndexOf(ColumnIds, column.Id)).ToArray();
        for (var index = 0; index < sorted.Length; index++)
            _table.Columns[Array.IndexOf(ColumnIds, sorted[index].Id) + 1].DisplayIndex = index + 1;
        _groups.ColumnDefinitions.Clear();
        for (var index = 0; index < 3; index++)
        {
            var id = _layout.PanelOrder[index];
            if (index > 0) _groups.ColumnDefinitions.Add(new ColumnDefinition(16, GridUnitType.Pixel));
            _groups.ColumnDefinitions.Add(new ColumnDefinition(_layout.PanelWidths[id], GridUnitType.Star) { MinWidth = MinimumWidths[id] });
            Grid.SetColumn(_panels[id], index * 2);
        }
        Grid.SetRow(Find<Grid>("ResultSlot"), _layout.ResultAboveCommands ? 0 : 2);
        Grid.SetRow(_groups, _layout.ResultAboveCommands ? 2 : 0);
        UpdateResultSize();
        _applying = wasApplying;
    }

    private void UpdateResultSize()
    {
        if (_top.RowDefinitions.Count != 3) return;
        var body = Find<Grid>("BodyGrid");
        var filtersHeight = Math.Max(36, Find<Grid>("FiltersPanel").Bounds.Height);
        var error = Find<Border>("LoadErrorPanel");
        var available = body.Bounds.Height - 120 - 24 - filtersHeight - 12 - 36 - 128 - (error.IsVisible ? error.Bounds.Height + error.Margin.Bottom : 0);
        var height = Math.Min(_layout.ResultHeight, Math.Max(120, available));
        _top.RowDefinitions[_layout.ResultAboveCommands ? 0 : 2].Height = new GridLength(height);
        _top.RowDefinitions[_layout.ResultAboveCommands ? 2 : 0].Height = new GridLength(120);
    }

    private void StartDrag(object? sender, EventArgs e)
    {
        if (sender is not LayoutHandle handle || handle.PanelId != "result-size" && _beforeEdit is null) return;
        _drag = handle;
        _resizeStart = Find<Grid>("ResultSlot").Bounds.Height;
    }

    private void MoveDrag(object? sender, Point point)
    {
        if (_drag is not { } handle) return;
        if (handle.PanelId == "result-size")
        {
            _layout = _layout with { ResultHeight = Math.Clamp(_resizeStart + point.Y - handle.StartPoint.Y, 120, 280) };
            UpdateResultSize();
            return;
        }
        if (Math.Abs(point.X - handle.StartPoint.X) + Math.Abs(point.Y - handle.StartPoint.Y) < 4) return;
        var preview = Find<Border>("DragPreview");
        Find<TextBlock>("DragPreviewText").Text = handle.Title;
        preview.IsVisible = true;
        Canvas.SetLeft(preview, Math.Clamp(point.X + 12, 0, _window.Bounds.Width - 240));
        Canvas.SetTop(preview, Math.Clamp(point.Y + 12, 0, _window.Bounds.Height - 44));
        _dropTarget = null;
        foreach (var panel in _panels.Values) panel.Classes.Remove("drop-target");
        var guide = Find<Border>("DropGuide");
        guide.IsVisible = false;
        if (handle.PanelId == "result")
        {
            var bounds = BoundsInWindow(_groups);
            if (!bounds.Inflate(new Thickness(0, 16)).Contains(point)) return;
            _dropTarget = "result";
            guide.Width = _groups.Bounds.Width;
            Canvas.SetLeft(guide, bounds.Left);
            Canvas.SetTop(guide, _layout.ResultAboveCommands ? bounds.Bottom + 4 : bounds.Top - 7);
            guide.IsVisible = true;
        }
        else
        {
            foreach (var (id, panel) in _panels)
                if (id != handle.PanelId && BoundsInWindow(panel).Contains(point))
                { _dropTarget = id; panel.Classes.Add("drop-target"); break; }
        }
    }

    private Rect BoundsInWindow(Control control) => new Rect(control.Bounds.Size)
        .TransformToAABB(control.TransformToVisual(_window)!.Value);

    private void FinishDrag(object? sender, EventArgs e)
    {
        if (_drag is not { } handle) return;
        if (handle.PanelId == "result-size") Changed();
        else if (_dropTarget == "result") Apply(Capture() with { ResultAboveCommands = !_layout.ResultAboveCommands });
        else if (_dropTarget is { } target) Swap(handle.PanelId, target);
        ClearDrag();
    }

    private void Swap(string source, string target)
    {
        var order = _layout.PanelOrder.ToArray();
        var first = Array.IndexOf(order, source);
        var second = Array.IndexOf(order, target);
        if (first < 0 || second < 0) return;
        (order[first], order[second]) = (order[second], order[first]);
        Apply(Capture() with { PanelOrder = order });
    }

    public bool CancelDrag()
    {
        if (_drag is not { } handle) return false;
        if (handle.PanelId == "result-size")
        { _layout = _layout with { ResultHeight = _resizeStart }; UpdateResultSize(); }
        handle.CancelMove();
        ClearDrag();
        return true;
    }

    private void ClearDrag()
    {
        _drag = null;
        _dropTarget = null;
        foreach (var panel in _panels.Values) panel.Classes.Remove("drop-target");
        Find<Border>("DragPreview").IsVisible = false;
        Find<Border>("DropGuide").IsVisible = false;
    }

    private void OnHandleKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not LayoutHandle handle) return;
        if (handle.PanelId == "result-size" && e.Key is Key.Up or Key.Down)
        {
            _layout = _layout with { ResultHeight = Math.Clamp(Find<Grid>("ResultSlot").Bounds.Height + (e.Key == Key.Up ? -8 : 8), 120, 280) };
            UpdateResultSize();
            Changed();
            e.Handled = true;
        }
        else if (_beforeEdit is not null && handle.PanelId == "result" && e.Key is Key.Up or Key.Down)
        { Apply(Capture() with { ResultAboveCommands = e.Key == Key.Up }); e.Handled = true; }
        else if (_beforeEdit is not null && e.Key is Key.Left or Key.Right)
        {
            var index = Array.IndexOf(_layout.PanelOrder, handle.PanelId);
            var target = index + (e.Key == Key.Left ? -1 : 1);
            if (index >= 0 && target >= 0 && target < 3) Swap(handle.PanelId, _layout.PanelOrder[target]);
            e.Handled = true;
        }
    }
}
