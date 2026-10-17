using Jornal.Core.Orders;
using Jornal.Desktop.Customization;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class DailyToolsTests
{
    [Theory]
    [InlineData("#FF9DFF", -25089)] [InlineData(" -25089 ", -25089)] [InlineData("#FFFFFF", -1)] [InlineData("#000000", -16777216)]
    public void ColorAcceptsOpaqueHexAndLegacySignedArgb(string text, int expected)
    { Assert.True(RowColorViewModel.TryParse(text, out var color)); Assert.Equal(expected, color); }
    [Theory]
    [InlineData("")] [InlineData("#12345")] [InlineData("# 12345")] [InlineData("#GGGGGG")] [InlineData("0")] [InlineData("2147483647")] [InlineData("4294967295")]
    public void InvalidOrTransparentColorCannotBeApplied(string text) => Assert.False(RowColorViewModel.TryParse(text, out _));
    [Fact]
    public async Task ArbitraryColorChangesOnlySelectedColorsAndCodeCopyPreservesLetter()
    {
        var store = new DemoOrderStore(); var model = await BatchOrderTests.Model(store); var before = await store.ReadAsync(TestContext.Current.CancellationToken);
        var selected = model.Orders.Take(2).ToArray(); model.UpdateSelection(selected, selected[0]); model.ResultText = "Письмо";
        var clipboard = new RecordingJournalClipboard(); var form = model.CreateRowColorForm(clipboard)!;
        form.Code = "#FF9DFF"; await form.ConfirmCommand.ExecuteAsync(null);
        Assert.False(form.HasError); Assert.False(model.HasError); Assert.False(model.RequiresReload);
        var ids = selected.Select(row => row.Id).ToHashSet(); var after = await store.ReadAsync(TestContext.Current.CancellationToken);
        foreach (var row in after) Assert.Equal(before.Single(old => old.Id == row.Id) with { Color = ids.Contains(row.Id) ? -25089 : row.Color }, row);
        Assert.Equal("Письмо", model.ResultText);
        model.UpdateSelection([], null); Assert.Null(model.CreateRowColorForm(clipboard));
        var code = model.CreateRowColorForm(clipboard, true)!; code.Code = "#FF9DFF"; await code.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal("-25089", clipboard.Text); Assert.Equal("Письмо", model.ResultText); Assert.Equal(after, await store.ReadAsync(TestContext.Current.CancellationToken));
        clipboard.Failure = true; await code.ConfirmCommand.ExecuteAsync(null); Assert.True(code.HasError); Assert.False(model.IsCopyingSelection);
    }
    [Fact]
    public async Task ColorConflictRetainsInputAndBlocksRepeatUntilRefresh()
    {
        var store = new DemoOrderStore(); var model = await BatchOrderTests.Model(store); var row = model.Orders[0]; model.UpdateSelection([row], row);
        var form = model.CreateRowColorForm(new RecordingJournalClipboard())!; form.Code = "#AABBCC";
        await store.SetColorAsync([(await store.ReadAsync(TestContext.Current.CancellationToken)).Single(order => order.Id == row.Id)], OrderColors.Ready, TestContext.Current.CancellationToken);
        await form.ConfirmCommand.ExecuteAsync(null);
        Assert.True(model.RequiresReload); Assert.True(form.HasError); Assert.Equal("#AABBCC", form.Code); Assert.False(form.ConfirmCommand.CanExecute(null));
        Assert.Equal(OrderColors.Ready, (await store.ReadAsync(TestContext.Current.CancellationToken)).Single(order => order.Id == row.Id).Color);
    }
    [Fact]
    public void ListsTrimDeduplicateKeepCommasAndRemainLocalSuggestions()
    {
        var value = EntrySuggestions.FromText(" Иван \nИВАН\n\nПётр", "ООО, Пример\n Другой ");
        Assert.Equal(new[] { "Иван", "Пётр" }, value.Managers); Assert.Equal(new[] { "ООО, Пример", "Другой" }, value.Customers);
        Assert.Throws<ArgumentException>(() => EntrySuggestions.FromText(new string('Я', 256), ""));
        Assert.Throws<ArgumentException>(() => EntrySuggestions.FromText("Иван\tПетров", ""));
        Assert.Throws<ArgumentException>(() => new EntrySuggestions { Managers = Enumerable.Range(0, 201).Select(i => "Имя" + i).ToArray() }.Normalize());
        Assert.Throws<ArgumentException>(() => new EntrySuggestions { Customers = Enumerable.Range(0, 100).Select(i => new string('Я', 200) + i).ToArray() }.Normalize());
        EntrySuggestions? saved = null; var form = new EntrySuggestionsViewModel(value, result => { saved = result; return null; });
        form.CustomersText += "\nНовый"; form.SaveCommand.Execute(null); Assert.Equal(3, saved!.Customers.Length);
        var failed = new EntrySuggestionsViewModel(value, _ => "Нет доступа"); failed.ManagersText = "Новый"; failed.SaveCommand.Execute(null);
        Assert.Equal("Нет доступа", failed.Error); Assert.Equal("Новый", failed.ManagersText);
    }
    [Fact]
    public async Task NewAndEditFormsReceiveSuggestionsWithoutChangingSavedNames()
    {
        var store = new DemoOrderStore(); var model = await BatchOrderTests.Model(store);
        var value = EntrySuggestions.FromText("Иван\nПётр", "Пример"); model.ApplyEntrySuggestions(value);
        var form = model.CreateOrderForm()!;
        Assert.Equal(value.Managers, form.Manager.Suggestions); Assert.Equal(value.Customers, form.Customer.Suggestions);
        Assert.Empty(form.Manager.Value); form.Manager.Value = "Другое имя"; Assert.Equal("Другое имя", form.Manager.Value);
        var row = model.Orders[0]; model.UpdateSelection([row], row); var edit = (await model.CreateEditFormAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal(value.Managers, edit.Manager.Suggestions); Assert.Equal((await store.ReadAsync(TestContext.Current.CancellationToken)).Single(order => order.Id == row.Id).ManagerName, edit.Manager.Value);
        var copy = model.CaptureEntrySuggestions(); copy.Managers[0] = "Повреждено"; Assert.Equal("Иван", model.CaptureEntrySuggestions().Managers[0]);
    }
    [Fact]
    public void ProfilesRoundTripSuggestionsAndRejectCorruptListsWithoutLosingTheme()
    {
        var root = Path.Combine(Path.GetTempPath(), "jr-lists-" + Guid.NewGuid().ToString("N")); var path = Path.Combine(root, "ui.json"); var store = new JsonUiPreferencesStore(path);
        try
        {
            store.Save(new() { DarkTheme = true, EntrySuggestions = EntrySuggestions.FromText("Иван", "ООО, Пример") });
            Assert.True(new FileInfo(path).Length < 128 * 1024); var loaded = store.Load(); Assert.Null(loaded.Error); Assert.True(loaded.Preferences!.DarkTheme);
            Assert.Equal("ООО, Пример", Assert.Single(loaded.Preferences.EntrySuggestions.Customers));
            var nearLimit = Enumerable.Range(10, 32).Select(i => new string('Я', 247) + i).ToArray();
            store.Save(new() { EntrySuggestions = new() { Managers = nearLimit, Customers = nearLimit } });
            Assert.True(new FileInfo(path).Length < 128 * 1024); Assert.Null(store.Load().Error);
            var invalid = new UiPreferences { DarkTheme = true, EntrySuggestions = new() { Managers = [new string('x', 256)] } };
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(invalid));
            loaded = store.Load(); Assert.NotNull(loaded.Error); Assert.True(loaded.Preferences!.DarkTheme); Assert.Empty(loaded.Preferences.EntrySuggestions.Managers);
            File.WriteAllText(path, "{\"Version\":1,\"DarkTheme\":true}"); loaded = store.Load(); Assert.Null(loaded.Error); Assert.True(loaded.Preferences!.DarkTheme); Assert.Empty(loaded.Preferences.EntrySuggestions.Customers);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
