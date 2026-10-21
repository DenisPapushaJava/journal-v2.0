using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Desktop.Controls;
using Jornal.Desktop.Import;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Jornal.Parsing.Specifications;
using Xunit;

namespace Jornal.Tests;

public sealed class EntrySuggestionComboBoxTests
{
    [AvaloniaTheory]
    [InlineData(false, "Менеджер")] [InlineData(true, "Менеджер")]
    [InlineData(false, "Заказчик")] [InlineData(true, "Заказчик")]
    [InlineData(false, "Материал")] [InlineData(true, "Материал")]
    public void InlineCompletionKeepsTypedPrefixSeparateFromTheSelectedSuffix(bool dark, string label)
    {
        var field = new OrderEntryField(label, "Р")
        { UseEditableSuggestions = label == "Материал", Suggestions = ["Роман", "Руслан", "Пётр"], NeedsReview = true };
        var entry = new OrderEntryControl { DataContext = field };
        var next = new TextBox();
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto"), Children = { entry, next } };
        Grid.SetRow(next, 1);
        var window = new Window { Content = grid, Width = 640, Height = 200, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show(); Pump(); entry.FocusInput(); Pump();
            var box = entry.FindControl<EntrySuggestionComboBox>(label == "Материал" ? "EditableSuggestionsInput" : "SuggestionsInput")!;
            var input = box.GetVisualDescendants().OfType<TextBox>().Single();
            Assert.Equal("Р", input.Text); Assert.True(field.NeedsReview);
            input.SelectAll(); window.KeyTextInput("Р"); Pump();
            Assert.Equal("Роман", input.Text); Assert.Equal("Р", field.Value); Assert.True(field.NeedsReview);
            Assert.Equal(1, input.SelectionStart); Assert.Equal(5, input.SelectionEnd);
            Screenshot(window, "inline-" + label);
            Press(window, Key.Down); Assert.Equal("Руслан", input.Text); Assert.Equal("Р", field.Value); Assert.True(field.NeedsReview);
            Press(window, Key.Escape); Assert.Equal("Р", input.Text); Assert.True(field.NeedsReview);
            input.SelectAll(); window.KeyTextInput("Р"); Pump(); window.KeyTextInput("оман"); Pump();
            Assert.Equal("Роман", field.Value); Assert.False(field.NeedsReview);
            input.SelectAll(); foreach (var character in "рО") { window.KeyTextInput(character.ToString()); Pump(); }
            Assert.Equal("рОман", input.Text); Assert.Equal("рО", field.Value);
            Assert.Equal(2, input.SelectionStart); Assert.Equal(5, input.SelectionEnd);
            window.KeyTextInput("к"); Pump(); window.KeyTextInput("ет"); Pump();
            Assert.Equal("рОкет", input.Text); Assert.Equal("рОкет", field.Value); Assert.False(box.IsDropDownOpen);
            input.SelectAll(); window.KeyTextInput("Р"); Pump(); Assert.True(input.CanUndo);
            input.Undo(); Pump(); Assert.Equal("Р", input.Text); Assert.Equal("Р", field.Value);
            input.SelectAll(); window.KeyTextInput("Р"); Pump(); Press(window, Key.Back);
            Assert.Equal("Р", input.Text); Assert.Equal("Р", field.Value);
            Press(window, Key.Back); Assert.Empty(input.Text!); Assert.Empty(field.Value);
            window.KeyTextInput("Р"); Pump(); Press(window, Key.Delete);
            Assert.Equal("Р", input.Text); Assert.Equal("Р", field.Value);
            input.SelectAll(); window.KeyTextInput("Р"); Pump(); Press(window, Key.Tab);
            Assert.Equal("Роман", field.Value); Assert.True(next.IsFocused);
            entry.FocusInput(); Pump(); input.SelectAll(); window.KeyTextInput("Ру"); Pump(); next.Focus(); Pump();
            Assert.Equal("Руслан", field.Value);
            entry.FocusInput(); Pump(); input.SelectAll(); window.KeyTextInput("Ро"); Pump(); Press(window, Key.Left);
            Assert.Equal("Ро", input.Text); Assert.Equal(2, input.CaretIndex);
            input.SelectAll(); window.KeyTextInput("Р"); Pump(); Press(window, Key.Right);
            Assert.Equal("Роман", field.Value);
            input.CaretIndex = input.SelectionStart = input.SelectionEnd = 1;
            window.KeyTextInput("X"); Pump(); Assert.Equal("РXоман", field.Value); Assert.Equal("РXоман", input.Text);
            input.SelectAll(); window.KeyTextInput("Р"); Pump(); Click(box.ContainerFromIndex(1)!); Press(window, Key.Tab);
            Assert.Equal("Руслан", field.Value);
            entry.FocusInput(); Pump(); input.SelectAll(); window.KeyTextInput("Р"); Pump(); Click(box.ContainerFromIndex(0)!);
            Assert.Equal("Роман", field.Value);
            input.SelectAll(); window.KeyTextInput("Р"); Pump(); box.IsDropDownOpen = false; Pump(); Press(window, Key.Enter);
            Assert.Equal("Роман", field.Value);
            entry.FocusInput(); Pump(); input.SelectAll(); window.KeyTextInput("Р"); Pump();
            field.Suggestions = ["Другой"]; Pump(); Assert.Equal("Р", input.Text); Assert.Equal("Р", field.Value);
            var replacement = new OrderEntryField(label, "Ро")
            { UseEditableSuggestions = label == "Материал", Suggestions = ["Роман"], NeedsReview = true };
            entry.DataContext = replacement; Pump();
            Assert.Equal("Ро", input.Text); Assert.True(replacement.NeedsReview); Assert.Equal("Р", field.Value);
        }
        finally { window.Close(); Pump(); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task ClickingSaveStoresTheCompletedNameRatherThanThePrefix(bool dark)
    {
        var store = new DemoOrderStore(); var model = ManualOrderTests.ValidForm(store);
        model.Manager.Value = ""; model.Manager.Suggestions = ["Роман", "Руслан"];
        await model.WaitForThicknessOptionsAsync(TestContext.Current.CancellationToken);
        var window = new NewOrderWindow { DataContext = model, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show(); Pump(); window.KeyTextInput("Р"); Pump();
            Assert.Equal("Р", model.Manager.Value);
            var save = window.FindControl<JournalButton>("SaveButton")!;
            for (var attempt = 0; attempt < 3 && !model.IsSaved; attempt++)
            {
                Click(save);
                if (model.SaveCommand.ExecutionTask is { } task) await task;
                Pump();
            }
            Assert.True(model.IsSaved); Assert.Equal("Роман", model.CreatedOrder!.ManagerName);
            Assert.Equal(4, (await store.ReadAsync(TestContext.Current.CancellationToken)).Count);
        }
        finally { window.Close(); Pump(); }
    }

    [AvaloniaTheory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public void MaterialPreviewNeverConfirmsImportedGradeInEitherForm(bool edit, bool dark)
    {
        var created = ManualOrderTests.ValidForm(new DemoOrderStore());
        var store = new EditSpecificationImportTests.Store(); var edited = EditSpecificationImportTests.Form(store);
        var field = edit ? edited.Material : created.Material;
        field.Value = "Ст3"; field.Suggestions = ["Ст3", "Ст20", "09Г2С"];
        field.NeedsReview = true;
        Window window = edit ? new EditOrderWindow { DataContext = edited } : new NewOrderWindow { DataContext = created };
        window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        bool CanSave() => edit ? edited.SaveCommand.CanExecute(null) : created.SaveCommand.CanExecute(null);
        try
        {
            window.Show(); Pump();
            var entry = window.GetVisualDescendants().OfType<OrderEntryControl>()
                .Single(control => control.IsEffectivelyVisible && control.DataContext == field);
            var box = entry.FindControl<EntrySuggestionComboBox>("EditableSuggestionsInput")!;
            entry.FocusInput(); Pump();
            Assert.True(field.NeedsReview); Assert.False(CanSave());
            Screenshot(window, edit ? "material-edit-review" : "material-new-review");
            box.IsDropDownOpen = true; Pump(); Press(window, Key.Down);
            Assert.Equal("Ст3", field.Value); Assert.True(field.NeedsReview); Assert.False(CanSave());
            Press(window, Key.Escape); Assert.True(field.NeedsReview);
            box.IsDropDownOpen = true; Pump(); Press(window, Key.Enter);
            Assert.Equal("Ст3", field.Value); Assert.True(field.NeedsReview); Assert.False(CanSave());
            box.IsDropDownOpen = true; Pump(); Click(box.ContainerFromIndex(0)!);
            Assert.True(field.NeedsReview); Assert.False(CanSave());
            window.RequestedThemeVariant = dark ? ThemeVariant.Light : ThemeVariant.Dark; Pump();
            Assert.Equal("Ст3", field.Value); Assert.True(field.NeedsReview);
            var confirm = box.GetVisualDescendants().OfType<JournalButton>()
                .Single(button => button.Label == "Подтвердить материал");
            Assert.True(confirm.IsEffectivelyVisible); confirm.Focus(); Pump(); Press(window, Key.Enter);
            Assert.False(field.NeedsReview); Assert.True(CanSave());
            Assert.False(box.IsDropDownOpen);
            field.NeedsReview = true;
            box.GetVisualDescendants().OfType<TextBox>().Single().Focus(); Pump();
            box.IsDropDownOpen = true; Pump();
            Assert.True(box.IsDropDownOpen);
            Assert.Contains("suggestion-preview", box.ContainerFromIndex(0)!.Classes);
            Press(window, Key.Down);
            Assert.True(box.IsDropDownOpen);
            Assert.Contains("suggestion-preview", box.ContainerFromIndex(1)!.Classes);
            Press(window, Key.Enter);
            Assert.Equal("Ст20", field.Value); Assert.False(field.NeedsReview);
            field.ApplyImport("Неизвестный сплав", "Материал из ГАР"); field.NeedsReview = true; Pump();
            box.IsDropDownOpen = true; Pump(); Press(window, Key.Down); Press(window, Key.Escape);
            Assert.Equal("Неизвестный сплав", field.Value); Assert.True(field.NeedsReview);
            field.Suggestions = ["Aisi-304"]; Pump();
            Assert.Equal("Неизвестный сплав", field.Value); Assert.True(field.NeedsReview);
            field.Suggestions = []; Pump();
            Assert.True(entry.FindControl<TextBox>("Input")!.IsEffectivelyVisible);
            Assert.Equal("Неизвестный сплав", field.Value); Assert.True(field.NeedsReview); Assert.False(CanSave());
            field.Suggestions = ["Ст3", "Ст20", "09Г2С"]; Pump();
            Assert.Equal("Неизвестный сплав", field.Value); Assert.True(field.NeedsReview);
            box.Focus(); Pump();
            var input = box.GetVisualDescendants().OfType<TextBox>().Single(); input.SelectAll();
            window.KeyTextInput("сТ"); Pump();
            Assert.Equal(new[] { "Ст3", "Ст20" }, box.ItemsView.Cast<string>());
            Assert.Equal("сТ", field.Value); Assert.False(field.NeedsReview);
            Press(window, Key.Enter); Assert.Equal("Ст3", field.Value);
            Assert.True(window.IsVisible); Assert.False(created.IsSaved); Assert.Equal(0, store.Updates);
        }
        finally { window.Close(); Pump(); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task AWJCBatchKeepsEachReviewUntilExplicitConfirmationOrCorrection(bool dark)
    {
        var token = TestContext.Current.CancellationToken;
        var store = new DemoOrderStore(); var machine = MachineCatalogTests.Machine();
        var creation = MachineCatalogTests.Creation(machine);
        await store.Prices.CreateMachineAsync(creation with { Rates = [.. creation.Rates, new(5m, "5", 170)] }, token);
        var preview = SpecificationImportPreview.Create("ГАР.pdf",
            new WaterjetPdfSpecificationParser().Parse(WaterjetPdfParserTests.Document(), token));
        var model = new NewOrderViewModel(store) { ImportReader = new PreviewReader(preview) };
        await model.LoadMachinesAsync(token); model.ApplyImport(preview);
        model.Material.Suggestions = ["Сталь 20/09Г2С", "Ст3"];
        await model.AddBatchFilesAsync(["Второй.pdf"], token);
        await model.WaitForThicknessOptionsAsync(token);
        var window = new NewOrderWindow { DataContext = model, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show(); Pump(); Assert.False(model.SaveCommand.CanExecute(null));
            foreach (var item in model.BatchItems)
            {
                var field = item.Form.Material;
                var entry = window.GetVisualDescendants().OfType<OrderEntryControl>()
                    .Single(control => control.IsEffectivelyVisible && control.DataContext == field);
                var box = entry.FindControl<EntrySuggestionComboBox>("EditableSuggestionsInput")!;
                entry.FocusInput(); Pump(); box.IsDropDownOpen = true; Pump();
                Press(window, Key.Enter);
                Assert.True(field.NeedsReview); Assert.False(model.SaveCommand.CanExecute(null));
                if (item == model.BatchItems[0])
                    Click(box.GetVisualDescendants().OfType<JournalButton>().Single(button => button.Label == "Подтвердить материал"));
                else
                {
                    box.IsDropDownOpen = true; Pump(); Click(box.ContainerFromIndex(1)!);
                    Assert.Equal("Ст3", field.Value);
                }
                Assert.False(field.NeedsReview);
            }
            Assert.True(model.SaveCommand.CanExecute(null)); Assert.False(model.IsSaved);
            Assert.Equal(3, (await store.ReadAsync(token)).Count);
        }
        finally { window.Close(); Pump(); }
    }

    private sealed class PreviewReader(SpecificationImportPreview preview) : ISpecificationImportService
    {
        public Task<SpecificationImportPreview> ReadAsync(string path, CancellationToken cancellationToken) => Task.FromResult(preview with { FileName = path });
    }

    [AvaloniaTheory]
    [InlineData(false, "Р", false, "Роман")] [InlineData(true, "Р", false, "Роман")]
    [InlineData(false, "рО", false, "Роман")] [InlineData(true, "рО", false, "Роман")]
    [InlineData(false, "Р", true, "Руслан")] [InlineData(true, "Р", true, "Руслан")]
    public async Task EnterAcceptsThePreviewWithoutSavingTheForm(bool dark, string prefix, bool down, string expected)
    {
        var store = new DemoOrderStore(); var model = ManualOrderTests.ValidForm(store);
        model.Manager.Value = ""; model.Manager.Suggestions = ["Роман", "Руслан", "Пётр"];
        var window = new NewOrderWindow { DataContext = model, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show(); Pump();
            var box = Find(window, model.Manager);
            foreach (var character in prefix) { window.KeyTextInput(character.ToString()); Pump(); }
            Assert.True(box.IsDropDownOpen); Assert.Equal(prefix, model.Manager.Value);
            if (down) Press(window, Key.Down);
            Assert.Equal(prefix, model.Manager.Value);
            Press(window, Key.Enter);
            Assert.Equal(expected, model.Manager.Value); Assert.False(box.IsDropDownOpen);
            Assert.True(window.IsVisible); Assert.False(model.IsSaved);
            Assert.Equal(3, (await store.ReadAsync()).Count);
        }
        finally { window.Close(); Pump(); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void ArrowShowsAllChoicesEscapeKeepsTypedTextAndTypingContinuesAfterNoMatch(bool dark)
    {
        var field = new OrderEntryField("Менеджер") { Suggestions = ["Роман", "Руслан", "Пётр"] };
        var entry = new OrderEntryControl { DataContext = field };
        var window = new Window { Content = entry, Width = 640, Height = 140, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show(); Pump(); entry.FocusInput(); Pump();
            var box = entry.FindControl<EntrySuggestionComboBox>("SuggestionsInput")!;
            var input = box.GetVisualDescendants().OfType<TextBox>().Single();
            window.KeyTextInput("Р"); Pump(); Assert.Equal(new[] { "Роман", "Руслан" }, box.ItemsView.Cast<string>());
            input.SelectAll(); Press(window, Key.Down);
            Assert.Equal("Р", field.Value); Assert.Contains("suggestion-preview", box.ContainerFromIndex(1)!.Classes);
            Press(window, Key.Escape); Assert.Equal("Р", field.Value); Assert.True(window.IsVisible);
            box.IsDropDownOpen = true; Pump(); Assert.Equal(field.Suggestions, box.ItemsView.Cast<string>());
            Press(window, Key.Escape);
            input.SelectAll(); window.KeyTextInput("Роман"); Pump();
            input.CaretIndex = input.Text!.Length; input.SelectionStart = input.SelectionEnd = input.CaretIndex;
            window.KeyTextInput(" Н"); Pump(); window.KeyTextInput("овый"); Pump();
            Assert.Equal("Роман Новый", field.Value); Assert.False(box.IsDropDownOpen);
            input.SelectAll(); Press(window, Key.Back); Assert.Empty(field.Value);
            field.Value = "Пётр"; Pump(); Assert.Equal("Пётр", input.Text);
            field.Suggestions = ["Другой"]; Pump(); Assert.Equal("Пётр", field.Value);
            field.Suggestions = []; Pump(); Assert.True(entry.FindControl<TextBox>("Input")!.IsVisible);
            Assert.Equal("Пётр", field.Value); Assert.False(box.IsDropDownOpen);
        }
        finally { window.Close(); Pump(); }
    }

    [AvaloniaTheory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public void BothFieldsInBothFormsSupportEscapeTabAndThemeChangesWithoutSaving(bool edit, bool dark)
    {
        var created = ManualOrderTests.ValidForm(new DemoOrderStore());
        var store = new EditSpecificationImportTests.Store(); var edited = EditSpecificationImportTests.Form(store);
        var fields = edit ? new[] { edited.Manager, edited.Customer } : [created.Manager, created.Customer];
        foreach (var field in fields) field.Suggestions = ["Роман", "Руслан", "Пётр"];
        Window window = edit ? new EditOrderWindow { DataContext = edited } : new NewOrderWindow { DataContext = created };
        window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        try
        {
            window.Show(); Pump(); Assert.False(edited.HasChanges);
            foreach (var field in fields)
            {
                var box = Find(window, field); box.Focus(); Pump();
                window.KeyTextInput("Р"); Pump(); Assert.True(box.IsDropDownOpen);
                Press(window, Key.Escape); Assert.True(window.IsVisible); Assert.Equal("Р", field.Value);
                box.Focus(); Pump(); window.KeyTextInput("Р"); Pump();
                Press(window, Key.Tab); Assert.False(box.IsDropDownOpen); Assert.False(box.IsKeyboardFocusWithin);
                Assert.Equal("Роман", field.Value);
                box.Focus(); Pump(); window.KeyTextInput("Р"); Pump();
                window.RequestedThemeVariant = dark ? ThemeVariant.Light : ThemeVariant.Dark; Pump();
                Assert.Equal("Р", field.Value); box.IsDropDownOpen = true; Pump();
                Press(window, Key.Enter); Assert.Equal("Роман", field.Value); Assert.True(window.IsVisible);
            }
            Assert.Equal(0, store.Updates); Assert.False(created.IsSaved);
        }
        finally { window.Close(); Pump(); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void NativeArrowAndMouseSelectionWorkAndRebindingDoesNotChangeEitherModel(bool dark)
    {
        var original = new OrderEntryField("Заказчик", "Ро") { Suggestions = ["Роман", "Руслан", "Пётр"] };
        var entry = new OrderEntryControl { DataContext = original };
        var window = new Window { Content = entry, Width = 640, Height = 140, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show(); Pump(); entry.FocusInput(); Pump();
            var box = entry.FindControl<EntrySuggestionComboBox>("SuggestionsInput")!;
            Click(box.GetVisualDescendants().OfType<PathIcon>().Single(icon => icon.Name == "DropDownGlyph"));
            Assert.True(box.IsDropDownOpen); Assert.Equal(original.Suggestions, box.ItemsView.Cast<string>());
            Assert.Equal("Ро", original.Value);
            var chosen = box.ContainerFromIndex(2)!; Assert.NotNull(chosen);
            Screenshot(window, "entry"); Screenshot(TopLevel.GetTopLevel(chosen)!, "popup");
            Click(chosen); Assert.Equal("Пётр", original.Value); Assert.False(box.IsDropDownOpen);
            var replacement = new OrderEntryField("Заказчик", "Другой клиент") { Suggestions = ["Другой клиент", "Новый"] };
            entry.DataContext = replacement; Pump(); Assert.Equal("Другой клиент", box.Text);
            Assert.Equal("Пётр", original.Value); Assert.Equal("Другой клиент", replacement.Value);
            box.IsDropDownOpen = true; Pump(); replacement.IsInputEnabled = false; Pump();
            Assert.False(box.IsDropDownOpen); Assert.Equal("Другой клиент", replacement.Value);
            replacement.IsInputEnabled = true; Pump();
            box.IsDropDownOpen = true; Pump(); window.Close(); Pump(); Assert.False(box.IsDropDownOpen);
        }
        finally { window.Close(); Pump(); }
    }

    private static void Click(Control control)
    {
        var root = TopLevel.GetTopLevel(control)!;
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value;
        root.MouseMove(point); root.MouseDown(point, MouseButton.Left); root.MouseUp(point, MouseButton.Left); Pump();
    }
    private static void Screenshot(TopLevel root, string name)
    {
        if (Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(root.ClientSize, 1)); bitmap.Render(root);
        bitmap.Save(Path.Combine(directory, $"combo-{name}-{root.ActualThemeVariant}.png"), new PngBitmapEncoderOptions());
    }
    private static EntrySuggestionComboBox Find(Window window, OrderEntryField field) => window.GetVisualDescendants()
        .OfType<OrderEntryControl>().Single(entry => entry.IsEffectivelyVisible && entry.DataContext == field)
        .FindControl<EntrySuggestionComboBox>("SuggestionsInput")!;
    private static void Press(Window window, Key key)
    {
        var physical = key switch { Avalonia.Input.Key.Enter => PhysicalKey.Enter, Avalonia.Input.Key.Down => PhysicalKey.ArrowDown,
            Avalonia.Input.Key.Escape => PhysicalKey.Escape, Avalonia.Input.Key.Back => PhysicalKey.Backspace,
            Avalonia.Input.Key.Delete => PhysicalKey.Delete,
            Avalonia.Input.Key.Left => PhysicalKey.ArrowLeft, Avalonia.Input.Key.Right => PhysicalKey.ArrowRight,
            Avalonia.Input.Key.Tab => PhysicalKey.Tab, _ => (PhysicalKey)0 };
        window.KeyPress(key, RawInputModifiers.None, physical, null); window.KeyRelease(key, RawInputModifiers.None, physical, null); Pump();
    }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
}
