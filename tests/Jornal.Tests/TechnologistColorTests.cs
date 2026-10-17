using System.Text.Json;
using Jornal.Core.Orders;
using Jornal.Desktop.Customization;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Demo;
using Jornal.Infrastructure.Local;
using Xunit;

namespace Jornal.Tests;

public sealed class TechnologistColorTests
{
    [Theory]
    [InlineData(-8323200, "#80FF80")]
    [InlineData(-12402177, "#42C1FF")]
    [InlineData(-25089, "#FF9DFF")]
    [InlineData(-128, "#FFFF80")]
    [InlineData(-8323073, "#80FFFF")]
    public void LegacySignedCodeKeepsExactArgbAndSelectsTheExistingSwatch(int argb, string hex)
    {
        var model = new MainWindowViewModel(new DemoOrderStore());
        model.TechnologistColorCode = argb.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(OrderColors.None, model.EffectiveNewOrderColor);
        model.UseTechnologistColor = true;
        Assert.Equal(argb, model.EffectiveNewOrderColor); Assert.Equal(hex, model.TechnologistColorHex);
        Assert.Equal(argb, Assert.Single(model.FrequentTechnologistColors.Concat(model.OtherTechnologistColors), item => item.IsSelected).Argb);
    }
    [Theory]
    [InlineData("0")] [InlineData("-1")] [InlineData("-882036")] [InlineData("-10238087")] [InlineData("-8154114")]
    [InlineData("2147483648")] [InlineData("-2147483648")] [InlineData("#FF9DFF")] [InlineData("2+1")]
    public void InvalidTransparentAndReservedColorsNeverReplaceTheLastValidValue(string text)
    {
        var model = new MainWindowViewModel(new DemoOrderStore()) { UseTechnologistColor = true };
        model.TechnologistColorCode = "-8323200";
        model.TechnologistColorCode = text;
        Assert.True(model.HasTechnologistColorError); Assert.Equal(-8323200, model.EffectiveNewOrderColor);
        Assert.Equal(-8323200, model.CaptureNewOrderColor().ColorArgb);
        model.CommitTechnologistColorCode(); Assert.Equal("-8323200", model.TechnologistColorCode); Assert.False(model.HasTechnologistColorError);
    }
    [Theory]
    [InlineData("")] [InlineData(",\"NewOrderColor\":null")] [InlineData(",\"NewOrderColor\":\"bad\"")]
    [InlineData(",\"NewOrderColor\":{\"Enabled\":true}")]
    [InlineData(",\"NewOrderColor\":{\"Enabled\":true,\"ColorArgb\":-1}")]
    [InlineData(",\"NewOrderColor\":{\"Enabled\":true,\"ColorArgb\":-882036}")]
    [InlineData(",\"NewOrderColor\":{\"Enabled\":true,\"ColorArgb\":1e99}")]
    [InlineData(",\"NewOrderColor\":{\"Enabled\":true,\"ColorArgb\":{}}")]
    public void OldAndDamagedSettingDisablesColorWithoutResettingOtherPreferences(string fragment)
    {
        var root = Path.Combine(Path.GetTempPath(), "journal-color-profile-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "ui-settings.json"); File.WriteAllText(path, "{\"Version\":1,\"DarkTheme\":true,\"Layout\":{\"RowHeight\":24},\"ResultPricing\":{\"MinimumPrice\":900}" + fragment + "}");
            var store = new JsonUiPreferencesStore(path); var loaded = store.Load();
            Assert.Null(loaded.Error); Assert.True(loaded.Preferences!.DarkTheme); Assert.Equal(24, loaded.Preferences.Layout.RowHeight);
            Assert.Equal(900m, loaded.Preferences.ResultPricing.MinimumPrice); Assert.Equal(new NewOrderColorSettings(), loaded.Preferences.NewOrderColor);
            store.Save(loaded.Preferences with { NewOrderColor = new() { Enabled = true, ColorArgb = -25089 } });
            Assert.Equal(-25089, store.Load().Preferences!.NewOrderColor.EffectiveColor);
            using var json = JsonDocument.Parse(File.ReadAllText(path)); Assert.Equal(-25089, json.RootElement.GetProperty("NewOrderColor").GetProperty("ColorArgb").GetInt32());
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task NewFormsCaptureTheLocalColorWithoutRecoloringHistoryOrChangingExistingStatus()
    {
        var store = new DemoOrderStore(); var history = await store.ReadAsync(TestContext.Current.CancellationToken);
        var model = new MainWindowViewModel(store, store) { UseTechnologistColor = true };
        var first = model.CreateOrderForm()!; Fill(first, store);
        model.TechnologistColorCode = "-8323200";
        await first.SaveCommand.ExecuteAsync(null); Assert.Equal(-25089, first.CreatedOrder!.Color);
        var second = model.CreateOrderForm()!; Fill(second, store); await second.SaveCommand.ExecuteAsync(null); Assert.Equal(-8323200, second.CreatedOrder!.Color);
        model.UseTechnologistColor = false;
        var third = model.CreateOrderForm()!; Fill(third, store); await third.SaveCommand.ExecuteAsync(null); Assert.Equal(OrderColors.None, third.CreatedOrder!.Color);
        var after = await store.ReadAsync(TestContext.Current.CancellationToken);
        Assert.All(history, row => Assert.Contains(row, after));
        await store.SetColorAsync([first.CreatedOrder], OrderColors.Ready, TestContext.Current.CancellationToken);
        model.UseTechnologistColor = true;
        Assert.Equal(-8323200, model.EffectiveNewOrderColor);
        Assert.Equal(OrderColors.Ready, (await store.ReadAsync(TestContext.Current.CancellationToken)).Single(row => row.Id == first.CreatedOrder.Id).Color);
    }
    [Fact]
    public async Task SequentialSingleToBatchImportKeepsOneCapturedColorForEveryCreatedRow()
    {
        var store = new DemoOrderStore(); var model = new MainWindowViewModel(store, store) { UseTechnologistColor = true };
        var form = model.CreateOrderForm()!; form.ImportReader = new BatchImportTests.Reader(await BatchImportTests.Preview());
        await form.ImportFilesAsync(["one.html"], TestContext.Current.CancellationToken, appendToCurrent: true);
        model.TechnologistColorCode = "-8323200";
        await form.ImportFilesAsync(["two.html"], TestContext.Current.CancellationToken, appendToCurrent: true);
        form.Manager.Value = "Иван"; form.Customer.Value = "Клиент";
        await form.SaveCommand.ExecuteAsync(null);
        Assert.Equal(2, form.CreatedOrders.Count); Assert.All(form.CreatedOrders, row => Assert.Equal(-25089, row.Color));
        Assert.All(form.BatchItems, item => Assert.Equal(-25089, item.Form.InitialColor));
    }
    [Fact]
    public async Task LocalColorIsPersistedInTheExistingFieldAndOrdinaryEditingPreservesIt()
    {
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync(); var store = new LocalJournalStore(fixture.Path);
        var model = new MainWindowViewModel(store, store) { UseTechnologistColor = true };
        var form = model.CreateOrderForm()!; Fill(form, new DemoOrderStore()); await form.SaveCommand.ExecuteAsync(null);
        var saved = (await new LocalJournalStore(fixture.Path).ReadRecordAsync(form.CreatedOrder!.Id, TestContext.Current.CancellationToken))!;
        Assert.Equal(-25089, saved.Color);
        model.TechnologistColorCode = "-8323200";
        var editor = new EditOrderViewModel(saved, store, "Тестовый источник", token => store.ReadRecordAsync(saved.Id, token));
        editor.Customer.Value = "Другой заказчик";
        await editor.CalculateCommand.ExecuteAsync(null); await editor.SaveCommand.ExecuteAsync(null);
        Assert.Equal(-25089, editor.SavedRecord!.Color); Assert.Equal(saved.Id, editor.SavedRecord.Id);
        var after = await store.ReadAsync(TestContext.Current.CancellationToken);
        Assert.All(LocalJournalFixture.Orders, row => Assert.Contains(row, after));
    }
    internal static void Fill(NewOrderViewModel form, DemoOrderStore store)
    {
        var valid = ManualOrderTests.ValidForm(store);
        for (var index = 0; index < valid.Fields.Count; index++) form.Fields[index].Value = valid.Fields[index].Value;
    }
}
