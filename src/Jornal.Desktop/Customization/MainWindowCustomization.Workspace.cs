using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Jornal.Desktop.Controls;

namespace Jornal.Desktop.Customization;

public sealed partial class MainWindowCustomization
{
    private const double ZoneGap = 12;
    private Grid Board => Find<Grid>("DockBoard");
    private Control Panel(string id) => id == "result" ? Find<Grid>("ResultSlot") : _panels[id];
    private static double PanelMinimumWidth(string id) => id == "result" ? 480 : 304;
    private static double PanelMinimumHeight(string id) => id switch
        { "result" => 106, "orders" => 106, "status" => 140, _ => 174 };
    private static string PanelTitle(string id) => id switch
        { "result" => "Результат", "orders" => "Заявки", "status" => "Присвоить статус", _ => "Буфер обмена и БЛЦБ" };

    private WorkspaceLayout? CaptureWorkspace()
    {
        if (_layout.Workspace is not { } workspace) return null;
        var widths = new[] { 0, 2, 4 }.Select(index => Board.ColumnDefinitions[index].ActualWidth).ToArray();
        var top = Board.RowDefinitions[0].ActualHeight;
        var bottom = Board.RowDefinitions[2].ActualHeight;
        return workspace with
        {
            ColumnWidths = widths.All(width => width > 0) ? widths : workspace.ColumnWidths,
            TopFraction = top + bottom > 0 ? top / (top + bottom) : workspace.TopFraction
        };
    }

    private void ApplyWorkspace()
    {
        var docked = _layout.Workspace is not null;
        var result = Find<Grid>("ResultSlot");
        var resize = Find<LayoutHandle>("ResultResizeHandle");
        // Все элементы сохраняются: ввод, bindings, команды и NameScope не пересоздаются.
        foreach (var panel in _panels.Values.Cast<Control>().Append(result).Append(resize))
            if (panel.Parent is Panel parent) parent.Children.Remove(panel);

        _groups.IsVisible = !docked;
        Find<Grid>("DockArea").IsVisible = docked;
        Find<Border>("ResultPanel").Margin = docked ? new Thickness(0) : new Thickness(0, 0, 0, 4);
        ToolTip.SetTip(resize, docked ? "Высота блоков · ↑ ↓" : "Изменить высоту результата · ↑ ↓ с клавиатуры");
        AutomationProperties.SetName(resize, docked ? "Изменить высоту блоков" : "Изменить высоту результата");
        if (!docked)
        {
            foreach (var panel in _panels.Values)
            { Grid.SetRow(panel, 0); Grid.SetRowSpan(panel, 1); _groups.Children.Add(panel); }
            Grid.SetColumn(result, 0); Grid.SetRowSpan(result, 1);
            _top.Children.Add(result);
            result.Children.Add(resize);
            return;
        }
        var workspace = _layout.Workspace!;
        Board.ColumnDefinitions.Clear();
        var ids = workspace.Panels;
        var minimums = new[] { PanelMinimumWidth(ids[0]), Math.Max(PanelMinimumWidth(ids[1]), PanelMinimumWidth(ids[2])), PanelMinimumWidth(ids[3]) };
        for (var index = 0; index < 3; index++)
        {
            if (index > 0) Board.ColumnDefinitions.Add(new ColumnDefinition(ZoneGap, GridUnitType.Pixel));
            Board.ColumnDefinitions.Add(new ColumnDefinition(workspace.ColumnWidths[index], GridUnitType.Star) { MinWidth = minimums[index] });
        }
        Board.RowDefinitions.Clear();
        Board.RowDefinitions.Add(new RowDefinition(workspace.TopFraction, GridUnitType.Star) { MinHeight = PanelMinimumHeight(ids[1]) });
        Board.RowDefinitions.Add(new RowDefinition(ZoneGap, GridUnitType.Pixel));
        Board.RowDefinitions.Add(new RowDefinition(1 - workspace.TopFraction, GridUnitType.Star) { MinHeight = PanelMinimumHeight(ids[2]) });
        for (var slot = 0; slot < 4; slot++)
        {
            var panel = Panel(ids[slot]);
            Grid.SetColumn(panel, slot == 0 ? 0 : slot == 3 ? 4 : 2);
            Grid.SetRow(panel, slot == 2 ? 2 : 0);
            Grid.SetRowSpan(panel, slot is 0 or 3 ? 3 : 1);
            Board.Children.Add(panel);
        }
        Find<Grid>("DockResizeSlot").Children.Add(resize);
    }

    private void UpdateWorkspaceSize()
    {
        if (_layout.Workspace is not { } workspace) return;
        var height = WorkspaceHeight(workspace);
        _top.RowDefinitions[0].Height = new GridLength(height);
        _top.RowDefinitions[1].Height = new GridLength(0);
        _top.RowDefinitions[2].Height = new GridLength(0);
    }

    private double WorkspaceHeight(WorkspaceLayout workspace)
    {
        var ids = workspace.Panels;
        var minimum = Math.Max(Math.Max(PanelMinimumHeight(ids[0]), PanelMinimumHeight(ids[3])),
            PanelMinimumHeight(ids[1]) + ZoneGap + PanelMinimumHeight(ids[2])) + 8;
        var error = Find<Border>("LoadErrorPanel");
        var available = Find<Grid>("BodyGrid").Bounds.Height - 24 - Math.Max(36, Find<Grid>("FiltersPanel").Bounds.Height)
            - 36 - 160 - (error.IsVisible ? error.Bounds.Height + error.Margin.Bottom : 0);
        return Math.Max(minimum, Math.Min(workspace.Height, available));
    }

    private void ResizeHeight(double height) => _layout = _layout.Workspace is { } workspace
        ? _layout with { Workspace = workspace with { Height = Math.Clamp(height, 106, 480) } }
        : _layout with { ResultHeight = Math.Clamp(height, ScreenLayout.MinimumResultHeight, ScreenLayout.MaximumResultHeight) };

    private void SwapWorkspace(string source, string target)
    {
        if (Capture() is not { Workspace: { } workspace } captured) return;
        var panels = workspace.Panels.ToArray();
        var first = Array.IndexOf(panels, source);
        var second = Array.IndexOf(panels, target);
        if (first < 0 || second < 0 || first == second) return;
        (panels[first], panels[second]) = (panels[second], panels[first]);
        Apply(captured with { Workspace = workspace with { Panels = panels } });
        Find<LayoutHandle>(source switch { "result" => "ResultMoveHandle", "orders" => "OrdersMoveHandle",
            "status" => "StatusMoveHandle", _ => "ClipboardMoveHandle" }).Focus();
    }

    private void MoveWorkspaceByKey(string source, Key key)
    {
        var panels = _layout.Workspace!.Panels;
        var slot = Array.IndexOf(panels, source);
        var target = (slot, key) switch
        {
            (0, Key.Right) => 1, (1, Key.Left) or (2, Key.Left) => 0,
            (1, Key.Right) or (2, Key.Right) => 3, (3, Key.Left) => 1,
            (1, Key.Down) => 2, (2, Key.Up) => 1, _ => -1
        };
        if (target >= 0) SwapWorkspace(source, panels[target]);
    }

    private void FindWorkspaceTarget(LayoutHandle handle, Point point)
    {
        foreach (var id in _layout.Workspace!.Panels)
        {
            var panel = Panel(id);
            if (id == handle.PanelId || !BoundsInWindow(panel).Contains(point)) continue;
            _dropTarget = id;
            if (id == "result") Find<Border>("ResultPanel").Classes.Add("drop-target");
            else _panels[id].Classes.Add("drop-target");
            ShowDropGuide(ProjectedDestination(handle.PanelId, id));
            break;
        }
    }

    private void ShowDropGuide(Control panel)
        => ShowDropGuide(BoundsInWindow(panel));

    private void ShowDropGuide(Rect bounds)
    {
        var guide = Find<Border>("DropGuide");
        guide.Width = bounds.Width;
        guide.Height = bounds.Height;
        Canvas.SetLeft(guide, bounds.Left);
        Canvas.SetTop(guide, bounds.Top);
        Find<TextBlock>("DropGuideText").Text = "Поменять местами";
        guide.IsVisible = true;
    }

    private Rect ProjectedDestination(string source, string target)
    {
        var workspace = CaptureWorkspace()!;
        var panels = workspace.Panels.ToArray();
        var slot = Array.IndexOf(panels, target);
        var from = Array.IndexOf(panels, source);
        (panels[from], panels[slot]) = (panels[slot], panels[from]);
        workspace = workspace with { Panels = panels };
        var widths = Distribute(Board.Bounds.Width - ZoneGap * 2, workspace.ColumnWidths,
            [PanelMinimumWidth(panels[0]), Math.Max(PanelMinimumWidth(panels[1]), PanelMinimumWidth(panels[2])), PanelMinimumWidth(panels[3])]);
        var height = WorkspaceHeight(workspace) - 8;
        var heights = Distribute(height - ZoneGap, [workspace.TopFraction, 1 - workspace.TopFraction],
            [PanelMinimumHeight(panels[1]), PanelMinimumHeight(panels[2])]);
        var origin = BoundsInWindow(Board).TopLeft;
        return slot switch
        {
            0 => new Rect(origin, new Size(widths[0], height)),
            3 => new Rect(origin + new Vector(widths[0] + widths[1] + ZoneGap * 2, 0), new Size(widths[2], height)),
            _ => new Rect(origin + new Vector(widths[0] + ZoneGap, slot == 2 ? heights[0] + ZoneGap : 0),
                new Size(widths[1], heights[slot == 2 ? 1 : 0]))
        };
    }

    // Та же раскладка звёздных размеров с минимальными границами, что у связанных Grid-зон.
    private static double[] Distribute(double available, double[] weights, double[] minimums)
    {
        var result = new double[weights.Length];
        var pending = Enumerable.Range(0, weights.Length).ToList();
        while (pending.Count > 0)
        {
            var total = pending.Sum(index => weights[index]);
            var limited = pending.Where(index => available * weights[index] / total < minimums[index]).ToArray();
            if (limited.Length == 0)
            {
                foreach (var index in pending) result[index] = available * weights[index] / total;
                break;
            }
            foreach (var index in limited)
            { result[index] = minimums[index]; available -= result[index]; pending.Remove(index); }
        }
        return result;
    }
}
