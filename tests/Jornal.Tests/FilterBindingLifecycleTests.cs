using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Logging;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Desktop.Controls;
using Jornal.Desktop.Customization;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Xunit;

namespace Jornal.Tests;

public sealed class FilterBindingLifecycleTests
{
    [AvaloniaTheory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public void FilterPopupsDoNotEvaluateEditorBindingsAgainstTheHeaderModel(bool dark, bool status)
    {
        // Привязка может сообщить об ошибке через журнал, не выбрасывая исключение наружу.
        using var bindingLog = new BindingLog();
        MainWindow? window = null;
        try
        {
            var preferences = new MemoryUiPreferencesStore();
            preferences.Save(new() { DarkTheme = dark });
            var model = new MainWindowViewModel(new LinkedColumnFilterTests.Reader { Items = LinkedColumnFilterTests.Rows() });
            window = new MainWindow(preferences) { DataContext = model, Width = 1920, Height = 768 };
            window.Show(); Pump();
            var grid = window.FindControl<DataGrid>("OrdersGrid")!;
            grid.SelectedItems.Add(model.Orders[0]);
            model.ResultText = "Письмо";
            var originalIds = model.Orders.Select(row => row.Id).ToArray();

            for (var cycle = 0; cycle < 3; cycle++)
            {
                var header = status
                    ? (Control)Assert.Single(grid.GetVisualDescendants().OfType<StatusFilterHeader>())
                    : Assert.Single(grid.GetVisualDescendants().OfType<ColumnFilterHeader>(), item => item.DataContext == model.ManagerFilter);
                var trigger = header.FindControl<JournalButton>(status ? "StatusFilterButton" : "ColumnFilterButton")!;
                var flyout = Assert.IsType<Flyout>(trigger.Flyout);
                var point = trigger.TranslatePoint(new Point(trigger.Bounds.Width / 2, trigger.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
                Pump(); Assert.True(flyout.IsOpen);
                var content = Assert.IsAssignableFrom<Control>(flyout.Content);
                if (status)
                {
                    Assert.True(model.StatusFilterEditor.AllChecked);
                    model.StatusFilterEditor.ToggleAllCommand.Execute(null);
                }
                else
                {
                    var search = header.FindControl<TextBox>("FilterSearch")!;
                    Assert.Equal("", search.Text);
                    search.Text = "Анна"; Pump();
                    Assert.Equal("Анна", Assert.Single(model.ManagerFilter.Editor.VisibleOptions).Label);
                    model.ManagerFilter.Editor.ToggleAllCommand.Execute(null);
                }
                Assert.False(header.FindControl<JournalButton>(status ? "ApplyStatusFilterButton" : "ApplyColumnFilterButton")!.IsEnabled);
                TopLevel.GetTopLevel(content)!.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                Pump(); Assert.False(flyout.IsOpen);
                Assert.True(trigger.IsFocused);
                Assert.Equal(originalIds, model.Orders.Select(row => row.Id));
                Assert.False(model.HasAnyColumnFilter); Assert.False(model.HasStatusFilter);
                Assert.Equal("Письмо", model.ResultText); Assert.Single(grid.SelectedItems);

                if (cycle == 0) { model.IsDarkTheme = !model.IsDarkTheme; Pump(); }
                if (cycle == 1)
                {
                    var template = grid.Template;
                    grid.Template = null; Pump(); grid.Template = template; Pump();
                }
            }

            var reboundHeader = status
                ? (Control)Assert.Single(grid.GetVisualDescendants().OfType<StatusFilterHeader>())
                : Assert.Single(grid.GetVisualDescendants().OfType<ColumnFilterHeader>(), item => item.DataContext == model.ManagerFilter);
            var reboundTrigger = reboundHeader.FindControl<JournalButton>(status ? "StatusFilterButton" : "ColumnFilterButton")!;
            var reboundFlyout = Assert.IsType<Flyout>(reboundTrigger.Flyout);
            Show(reboundHeader); Assert.True(reboundFlyout.IsOpen);
            reboundHeader.DataContext = null; Pump(); Assert.False(reboundFlyout.IsOpen);
            Assert.Null(reboundHeader.FindControl<Control>("PopupRoot")!.DataContext);
            Assert.Null(reboundHeader.FindControl<Control>("PopupPanel")!.DataContext);

            var replacement = new MainWindowViewModel(new LinkedColumnFilterTests.Reader
            {
                Items = [LinkedColumnFilterTests.Rows()[0] with { ManagerName = "Другой менеджер" }]
            });
            replacement.ReloadCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            reboundHeader.DataContext = status ? (object)replacement : replacement.ManagerFilter;
            Show(reboundHeader); Assert.True(reboundFlyout.IsOpen);
            if (status) Assert.Equal(1, replacement.StatusFilterEditor.Options.Sum(option => option.Count));
            else
            {
                var search = reboundHeader.FindControl<TextBox>("FilterSearch")!;
                Assert.Equal("", search.Text);
                Assert.Equal("Другой менеджер", Assert.Single(replacement.ManagerFilter.Editor.VisibleOptions).Label);
                search.Text = "Другой"; Pump();
                Assert.Equal("Другой", replacement.ManagerFilter.Editor.SearchText);
                Assert.Equal("", model.ManagerFilter.Editor.SearchText);
            }
            model.IsUpdatingOrders = true; Pump(); Assert.True(reboundFlyout.IsOpen);
            model.IsUpdatingOrders = false;
            model.IsDarkTheme = !model.IsDarkTheme; Pump(); Assert.True(reboundFlyout.IsOpen);
            if (!status) Assert.Equal("Другой", replacement.ManagerFilter.Editor.SearchText);
            var currentTemplate = grid.Template;
            grid.Template = null; Pump(); Assert.False(reboundFlyout.IsOpen);
            Assert.Null(reboundHeader.FindControl<Control>("PopupPanel")!.DataContext);
            grid.Template = currentTemplate; Pump();
            var attachedHeader = status
                ? (Control)Assert.Single(grid.GetVisualDescendants().OfType<StatusFilterHeader>())
                : Assert.Single(grid.GetVisualDescendants().OfType<ColumnFilterHeader>(), item => item.DataContext == model.ManagerFilter);
            Show(attachedHeader);
            Assert.True(status ? ((StatusFilterHeader)attachedHeader).IsFilterOpen : ((ColumnFilterHeader)attachedHeader).IsFilterOpen);
        }
        finally
        {
            window?.Close(); Pump();
        }
        if (bindingLog.Errors.Count > 0) Assert.Fail(string.Join(Environment.NewLine, bindingLog.Errors));
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Show(Control header)
    {
        if (header is StatusFilterHeader statusHeader) statusHeader.ShowFilter();
        else ((ColumnFilterHeader)header).ShowFilter();
        Pump();
    }

    private sealed class BindingLog : ILogSink, IDisposable
    {
        private readonly ILogSink? _previous = Logger.Sink;
        public BindingLog() => Logger.Sink = this;
        public void Dispose() => Logger.Sink = _previous;
        public List<string> Errors { get; } = [];
        public bool IsEnabled(LogEventLevel level, string area) => area == LogArea.Binding && level >= LogEventLevel.Warning;
        public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
            Log(level, area, source, messageTemplate, []);
        public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
        {
            if (source is Control control && (control.Name == "PopupRoot"
                || control.GetLogicalAncestors().OfType<Control>().Any(parent => parent.Name == "PopupRoot")))
                Errors.Add(messageTemplate + " " + string.Join("; ", propertyValues));
        }
    }
}
