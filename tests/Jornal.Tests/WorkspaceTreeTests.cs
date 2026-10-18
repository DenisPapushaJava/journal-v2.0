using System.Text.Json;
using Avalonia;
using Jornal.Desktop.Customization;
using Xunit;

namespace Jornal.Tests;

public sealed class WorkspaceTreeTests
{
    [Fact]
    public void PreviousWorkspaceMigratesWithItsOrderAndProportions()
    {
        var workspace = new WorkspaceLayout { Panels = ["status", "result", "orders", "clipboard"],
            ColumnWidths = [1, 3, 2], TopFraction = .6, Height = 360 }.Normalize();
        var root = workspace.Root!;
        Assert.Equal(WorkspaceAxis.Columns, root.Axis); Assert.Equal(new double[] { 3, 2 }, root.Weights);
        Assert.Equal(2, root.Children.Length);
        Assert.Equal(WorkspaceAxis.Rows, root.Children[0].Axis);
        Assert.Equal(new[] { "result", "orders" }, root.Children[0].PanelIds());
        Assert.Equal(new double[] { .6, .4 }, root.Children[0].Weights);
        Assert.Equal("clipboard", root.Children[1].PanelId);
        Assert.Equal(360, workspace.Height);
        Assert.Equal(JsonSerializer.Serialize(workspace), JsonSerializer.Serialize(JsonSerializer.Deserialize<WorkspaceLayout>(JsonSerializer.Serialize(workspace))!.Normalize()));
    }

    [Fact]
    public void SplittingBelowResultThenBesideOrderCreatesTwoIndependentPanels()
    {
        var initial = new WorkspaceLayout().Normalize().Root!;
        var before = JsonSerializer.Serialize(initial);
        var below = WorkspaceNode.Move(initial, "orders", "result", WorkspacePlacement.Bottom)!;
        var root = WorkspaceNode.Move(below, "clipboard", "orders", WorkspacePlacement.Right)!;
        var bounds = WorkspaceGeometry.Arrange(root, new Rect(0, 0, 1232, 300));
        Assert.Equal(bounds["result"].Left, bounds["orders"].Left);
        Assert.Equal(bounds["result"].Right, bounds["clipboard"].Right);
        Assert.True(bounds["orders"].Top > bounds["result"].Bottom);
        Assert.Equal(bounds["orders"].Top, bounds["clipboard"].Top);
        Assert.True(bounds["clipboard"].Left > bounds["orders"].Right);
        Assert.Equal(before, JsonSerializer.Serialize(initial));
        Assert.Equal(new Size(620, 292), WorkspaceGeometry.Minimum(root));
        AssertPanels(root); AssertNoOverlap(bounds);
    }

    [Fact]
    public void PreviousTreeRemovesOnlyStatusAndCollapsesItsEmptyArea()
    {
        var remaining = WorkspaceNode.Split(WorkspaceAxis.Columns, [WorkspaceNode.Panel("result"),
            WorkspaceNode.Split(WorkspaceAxis.Rows, [WorkspaceNode.Panel("orders"), WorkspaceNode.Panel("clipboard")], 2, 3)], 3, 2);
        var legacy = WorkspaceNode.Split(WorkspaceAxis.Rows, [WorkspaceNode.Panel("status"), remaining], 1, 4);
        var migrated = new WorkspaceLayout { Root = legacy, Height = 420 }.Normalize();
        Assert.Equal(JsonSerializer.Serialize(remaining), JsonSerializer.Serialize(migrated.Root));
        Assert.Equal(420, migrated.Height); AssertPanels(migrated.Root!);
        // Остальные части профиля не теряются при чтении прежнего дерева через JSON.
        var previousJson = JsonSerializer.Serialize(new
            { DarkTheme = true, ShowTodayOnStartup = false, Layout = new { Workspace = new { Root = legacy }, RowHeight = 24 } });
        var profile = JsonSerializer.Deserialize<UiPreferences>(previousJson)!;
        var layout = profile.Layout.Normalize();
        Assert.True(profile.DarkTheme); Assert.False(profile.ShowTodayOnStartup); Assert.Equal(24, layout.RowHeight);
        Assert.Equal(JsonSerializer.Serialize(remaining), JsonSerializer.Serialize(layout.Workspace!.Root));
    }

    [Fact]
    public void ManyMovesAndRootPlacementsCannotLoseOrDuplicatePanelsOrCreateEmptyGroups()
    {
        var root = new WorkspaceLayout().Normalize().Root!;
        var random = new Random(13);
        for (var index = 0; index < 500; index++)
        {
            var source = WorkspaceLayout.PanelIds[random.Next(3)];
            var target = index % 5 == 0 ? null : WorkspaceLayout.PanelIds.Where(id => id != source).ElementAt(random.Next(2));
            var move = (WorkspacePlacement)random.Next(5);
            if (WorkspaceNode.Move(root, source, target, move) is { } next) root = next;
            AssertPanels(root);
            var minimum = WorkspaceGeometry.Minimum(root);
            var bounds = WorkspaceGeometry.Arrange(root, new Rect(0, 0, minimum.Width + 400, minimum.Height + 80));
            AssertNoOverlap(bounds);
        }
        Assert.Null(WorkspaceNode.Move(root, "result", "result", WorkspacePlacement.Bottom));
        Assert.Null(WorkspaceNode.Move(root, "missing", "orders", WorkspacePlacement.Bottom));
    }

    [Fact]
    public void CorruptCyclesDuplicatesMissingPanelsAndFutureVersionsUseSafePreviousLayout()
    {
        var defaults = new WorkspaceLayout().Normalize();
        var cyclicChildren = new WorkspaceNode[2];
        var cycle = WorkspaceNode.Split(WorkspaceAxis.Columns, cyclicChildren, 1, 1);
        cyclicChildren[0] = cycle; cyclicChildren[1] = WorkspaceNode.Panel("result");
        Assert.Null(WorkspaceNode.Validate(cycle));
        Assert.Null(WorkspaceNode.Move(cycle, "result", "orders", WorkspacePlacement.Left));
        var duplicate = WorkspaceNode.Split(WorkspaceAxis.Rows,
            [WorkspaceNode.Panel("result"), WorkspaceNode.Panel("result"), WorkspaceNode.Panel("status"), WorkspaceNode.Panel("clipboard")], 1, 1, 1, 1);
        foreach (var root in new[] { cycle, duplicate, WorkspaceNode.Panel("result"), WorkspaceNode.Panel("unknown") })
            Assert.Equal(JsonSerializer.Serialize(defaults.Root), JsonSerializer.Serialize(new WorkspaceLayout { Root = root }.Normalize().Root));
        var future = defaults with { TreeVersion = 999, Root = WorkspaceNode.Move(defaults.Root!, "result", "orders", WorkspacePlacement.Swap) };
        Assert.Equal(JsonSerializer.Serialize(defaults.Root), JsonSerializer.Serialize(future.Normalize().Root));
        var badWeights = defaults.Root! with { Weights = [double.NaN, double.NegativeInfinity, -3] };
        Assert.All(WorkspaceNode.Validate(badWeights)!.Weights, weight => Assert.Equal(1, weight));
    }

    private static void AssertPanels(WorkspaceNode root)
    {
        Assert.NotNull(WorkspaceNode.Validate(root));
        Assert.Equal(WorkspaceLayout.PanelIds.Order(), root.PanelIds().Order());
        Assert.Equal(3, root.PanelIds().Count());
    }

    private static void AssertNoOverlap(Dictionary<string, Rect> bounds)
    {
        foreach (var (id, rectangle) in bounds)
        {
            Assert.True(rectangle.Width > 0 && rectangle.Height > 0);
            foreach (var (other, candidate) in bounds.Where(pair => pair.Key != id))
                Assert.False(rectangle.Intersects(candidate), $"{id} / {other}");
        }
    }
}
