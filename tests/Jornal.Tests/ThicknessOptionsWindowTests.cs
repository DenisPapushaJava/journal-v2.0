using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Desktop.Controls;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Local;
using Xunit;

namespace Jornal.Tests;

public sealed class ThicknessOptionsWindowTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task UnlistedThicknessStaysVisibleAndNestedPricesRepairItWithoutLosingTheForm(bool dark, bool edit)
    {
        var token = TestContext.Current.CancellationToken;
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync();
        var store = new LocalJournalStore(fixture.Path);
        Window window;
        OrderEntryField thickness;
        IReadOnlyList<OrderEntryField> fields;
        NewOrderViewModel? creation = null;
        EditOrderViewModel? editing = null;
        if (edit)
        {
            var record = (await store.ReadRecordAsync(42, token))! with { Id = 99, Thickness = 6 };
            await store.RestoreAsync(record, token);
            editing = new(record, store, fixture.Path, ct => store.ReadRecordAsync(record.Id, ct));
            thickness = editing.Thickness; fields = editing.Fields;
            window = new EditOrderWindow { DataContext = editing };
        }
        else
        {
            creation = ManualOrderTests.ValidForm(store); creation.Thickness.Value = "6";
            thickness = creation.Thickness; fields = creation.Fields;
            window = new NewOrderWindow { DataContext = creation };
        }
        window.Width = 1060; window.Height = 660; window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var expected = fields.Select(field => field.Value).ToArray();
        var originalRows = await store.ReadAsync(token);
        var before = await File.ReadAllBytesAsync(fixture.Path, token);
        try
        {
            window.Show();
            await WaitAsync(() => thickness.ThicknessOptions!.IsVerified);
            Assert.Equal("6", thickness.Value); Assert.True(thickness.ThicknessOptions!.HasUnavailableValue);
            Assert.Equal(before, await File.ReadAllBytesAsync(fixture.Path, token));
            var entry = window.GetVisualDescendants().OfType<OrderEntryControl>().Single(control => ReferenceEquals(control.DataContext, thickness));
            var combo = entry.FindControl<ComboBox>("Choice")!;
            Assert.True(combo.IsEffectivelyVisible); Assert.Null(combo.SelectedItem);
            Assert.DoesNotContain(entry.GetVisualDescendants().OfType<TextBox>(), box => box.IsEffectivelyVisible);
            combo.SelectedItem = null; Dispatcher.UIThread.RunJobs(); Assert.Equal("6", thickness.Value);
            Assert.Contains(entry.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text == "6");
            Save(window, $"thickness-{dark}-{edit}");
            entry.RaiseEvent(new RoutedEventArgs(OrderEntryControl.PricesRequestedEvent, entry));
            await WaitAsync(() => window.OwnedWindows.OfType<PriceEditorWindow>().Any());
            var pricesWindow = window.OwnedWindows.OfType<PriceEditorWindow>().Single();
            var prices = (PriceEditorViewModel)pricesWindow.DataContext!;
            await WaitAsync(() => !prices.IsLoading && prices.Rows.Count > 0);
            Assert.Equal(Jornal.Core.Pricing.PriceTables.HansLaserId2, prices.TableName);
            prices.BeginEditCommand.Execute(null); prices.Rows.Single(row => row.Thickness == "6").Value = "23";
            await prices.SaveCommand.ExecuteAsync(null); Assert.False(prices.HasError, prices.Error);
            pricesWindow.Close();
            await WaitAsync(() => thickness.ThicknessOptions.IsVerified && !thickness.ThicknessOptions.HasUnavailableValue &&
                (creation?.IsEditingPrices != true && editing?.IsEditingPrices != true));
            Assert.Equal(expected, fields.Select(field => field.Value));
            Assert.Equal("6", thickness.SelectedChoice); Assert.Null(thickness.ChoiceError);
            Assert.Equal(originalRows, await store.ReadAsync(token));
            Assert.Null(creation?.CreatedOrder); Assert.Null(editing?.SavedRecord);
            Assert.True(creation?.SaveCommand.CanExecute(null) ?? editing!.CanSave);
        }
        finally
        {
            foreach (var child in window.OwnedWindows.ToArray()) child.Close();
            window.Close();
        }
    }
    private static async Task WaitAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, TestContext.Current.CancellationToken); }
        Dispatcher.UIThread.RunJobs(); Assert.True(condition());
    }
    private static void Save(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("JOURNAL_WINDOW_SMOKE_OUTPUT");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var image = new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize, 1)); image.Render(window);
        image.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
}
