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
    public async Task ArbitraryColorChangesOnlySelectedColorsAndPreservesLetter()
    {
        var store = new DemoOrderStore(); var model = await BatchOrderTests.Model(store); var before = await store.ReadAsync(TestContext.Current.CancellationToken);
        var selected = model.Orders.Take(2).ToArray(); model.UpdateSelection(selected, selected[0]); model.ResultText = "Письмо";
        var form = model.CreateRowColorForm()!;
        form.Code = "#FF9DFF"; await form.ConfirmCommand.ExecuteAsync(null);
        Assert.False(form.HasError); Assert.False(model.HasError); Assert.False(model.RequiresReload);
        var ids = selected.Select(row => row.Id).ToHashSet(); var after = await store.ReadAsync(TestContext.Current.CancellationToken);
        foreach (var row in after) Assert.Equal(before.Single(old => old.Id == row.Id) with { Color = ids.Contains(row.Id) ? -25089 : row.Color }, row);
        Assert.Equal("Письмо", model.ResultText);
        model.UpdateSelection([], null); Assert.Null(model.CreateRowColorForm());
    }
    [Fact]
    public async Task ColorConflictRetainsInputAndBlocksRepeatUntilRefresh()
    {
        var store = new DemoOrderStore(); var model = await BatchOrderTests.Model(store); var row = model.Orders[0]; model.UpdateSelection([row], row);
        var form = model.CreateRowColorForm()!; form.Code = "#AABBCC";
        await store.SetColorAsync([(await store.ReadAsync(TestContext.Current.CancellationToken)).Single(order => order.Id == row.Id)], OrderColors.Ready, TestContext.Current.CancellationToken);
        await form.ConfirmCommand.ExecuteAsync(null);
        Assert.True(model.RequiresReload); Assert.True(form.HasError); Assert.Equal("#AABBCC", form.Code); Assert.False(form.ConfirmCommand.CanExecute(null));
        Assert.Equal(OrderColors.Ready, (await store.ReadAsync(TestContext.Current.CancellationToken)).Single(order => order.Id == row.Id).Color);
    }
    [Fact]
    public void ListsTrimDeduplicateKeepCommasAndRemainLocalSuggestions()
    {
        var value = EntrySuggestions.FromText(" Иван \nИВАН\n\nПётр", "ООО, Пример\n Другой ", " Ст3\r\nст3\r\n\r\n5083H111\nАмг3");
        Assert.Equal(new[] { "Иван", "Пётр" }, value.Managers); Assert.Equal(new[] { "ООО, Пример", "Другой" }, value.Customers);
        Assert.Equal(new[] { "Ст3", "5083H111", "Амг3" }, value.Materials);
        var empty = EntrySuggestions.FromText(null, null, null);
        Assert.Empty(empty.Managers); Assert.Empty(empty.Customers); Assert.Empty(empty.Materials);
        Assert.Throws<ArgumentException>(() => EntrySuggestions.FromText(new string('Я', 256), ""));
        Assert.Throws<ArgumentException>(() => EntrySuggestions.FromText("Иван\tПетров", ""));
        Assert.Throws<ArgumentException>(() => new EntrySuggestions { Managers = Enumerable.Range(0, 201).Select(i => "Имя" + i).ToArray() }.Normalize());
        Assert.Throws<ArgumentException>(() => new EntrySuggestions { Customers = Enumerable.Range(0, 100).Select(i => new string('Я', 200) + i).ToArray() }.Normalize());
        Assert.Throws<ArgumentException>(() => EntrySuggestions.FromText("", "", new string('x', 256)));
        Assert.Throws<ArgumentException>(() => EntrySuggestions.FromText("", "", "Ст\t3"));
        Assert.Throws<ArgumentException>(() => new EntrySuggestions { Materials = Enumerable.Range(0, 201).Select(i => "Марка" + i).ToArray() }.Normalize());
        var large = Enumerable.Range(0, 30).Select(i => new string('Я', 200) + i).ToArray();
        Assert.Throws<ArgumentException>(() => new EntrySuggestions { Managers = large, Customers = large, Materials = large }.Normalize());
        EntrySuggestions? saved = null; var form = new EntrySuggestionsViewModel(value, result => { saved = result; return null; });
        Assert.Equal("Ст3\n5083H111\nАмг3", form.MaterialsText); form.MaterialsText += "\n09Г2С";
        form.CustomersText += "\nНовый"; form.SaveCommand.Execute(null); Assert.Equal(3, saved!.Customers.Length);
        Assert.Equal(4, saved.Materials.Length);
        form.MaterialsText = null!; form.SaveCommand.Execute(null); Assert.Empty(saved.Materials);
        var failed = new EntrySuggestionsViewModel(value, _ => "Нет доступа"); failed.ManagersText = "Новый"; failed.SaveCommand.Execute(null);
        Assert.Equal("Нет доступа", failed.Error); Assert.Equal("Новый", failed.ManagersText); Assert.Equal("Ст3\n5083H111\nАмг3", failed.MaterialsText);
    }
    [Fact]
    public async Task NewAndEditFormsReceiveSuggestionsWithoutChangingSavedNames()
    {
        var store = new DemoOrderStore(); var model = await BatchOrderTests.Model(store);
        var value = EntrySuggestions.FromText("Иван\nПётр", "Пример", "Ст3\n09Г2С"); model.ApplyEntrySuggestions(value);
        var form = model.CreateOrderForm()!;
        Assert.Equal(value.Managers, form.Manager.Suggestions); Assert.Equal(value.Customers, form.Customer.Suggestions);
        Assert.Equal(value.Materials, form.Material.Suggestions);
        Assert.True(form.Material.HasEditableSuggestions);
        Assert.Empty(form.Material.Value); form.Material.Value = "Сплав_X77"; Assert.Equal("Сплав_X77", form.Material.Value);
        Assert.Empty(form.Manager.Value); form.Manager.Value = "Другое имя"; Assert.Equal("Другое имя", form.Manager.Value);
        var row = model.Orders[0]; model.UpdateSelection([row], row); var edit = (await model.CreateEditFormAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal(value.Managers, edit.Manager.Suggestions); Assert.Equal((await store.ReadAsync(TestContext.Current.CancellationToken)).Single(order => order.Id == row.Id).ManagerName, edit.Manager.Value);
        Assert.Equal(value.Materials, edit.Material.Suggestions); Assert.Equal(row.MaterialName, edit.Material.Value);
        Assert.True(edit.Material.HasEditableSuggestions);
        var copy = model.CaptureEntrySuggestions(); copy.Managers[0] = "Повреждено"; Assert.Equal("Иван", model.CaptureEntrySuggestions().Managers[0]);
        copy.Materials[0] = "Повреждено"; Assert.Equal("Ст3", model.CaptureEntrySuggestions().Materials[0]);
    }

    [Fact]
    public async Task SequentialImportKeepsMaterialSuggestionsWithoutReplacingParsedValues()
    {
        var token = TestContext.Current.CancellationToken;
        var store = new DemoOrderStore(); var model = new MainWindowViewModel(store, store);
        var suggestions = EntrySuggestions.FromText("Менеджер", "Клиент", "Ст3\n09Г2С");
        model.ApplyEntrySuggestions(suggestions);
        var form = model.CreateOrderForm()!;
        form.ImportReader = new BatchImportTests.Reader(await BatchImportTests.Preview());
        await form.ImportFilesAsync(["one.html"], token);
        var material = form.Material.Value;
        await form.ImportFilesAsync(["two.html"], token, appendToCurrent: true);
        Assert.Equal(2, form.BatchItems.Count);
        foreach (var item in form.BatchItems)
        { Assert.Equal(suggestions.Materials, item.Form.Material.Suggestions); Assert.Equal(material, item.Form.Material.Value); Assert.True(item.Form.Material.HasEditableSuggestions); }
        form.BatchItems[0].Form.Material.Value = "Другая_марка";
        Assert.All(form.BatchItems, item => Assert.Equal(suggestions.Materials, item.Form.Material.Suggestions));
        Assert.Equal("Другая_марка", form.BatchItems[0].Form.Material.Value);
        Assert.Equal(material, form.BatchItems[1].Form.Material.Value);
        Assert.Equal(3, (await store.ReadAsync(token)).Count);
    }
    [Fact]
    public void ProfilesRoundTripSuggestionsAndRejectCorruptListsWithoutLosingTheme()
    {
        var root = Path.Combine(Path.GetTempPath(), "jr-lists-" + Guid.NewGuid().ToString("N")); var path = Path.Combine(root, "ui.json"); var store = new JsonUiPreferencesStore(path);
        try
        {
            store.Save(new() { DarkTheme = true, EntrySuggestions = EntrySuggestions.FromText("Иван", "ООО, Пример", "Ст3\n5083H111") });
            Assert.True(new FileInfo(path).Length < 128 * 1024); var loaded = store.Load(); Assert.Null(loaded.Error); Assert.True(loaded.Preferences!.DarkTheme);
            Assert.Equal("ООО, Пример", Assert.Single(loaded.Preferences.EntrySuggestions.Customers));
            Assert.Equal(new[] { "Ст3", "5083H111" }, loaded.Preferences.EntrySuggestions.Materials);
            var nearLimit = Enumerable.Range(10, 21).Select(i => new string('Я', 247) + i).ToArray();
            store.Save(new() { EntrySuggestions = new() { Managers = nearLimit, Customers = nearLimit, Materials = nearLimit } });
            Assert.True(new FileInfo(path).Length < 128 * 1024); Assert.Null(store.Load().Error);
            var invalid = new UiPreferences { DarkTheme = true, EntrySuggestions = new() { Managers = [new string('x', 256)] } };
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(invalid));
            loaded = store.Load(); Assert.NotNull(loaded.Error); Assert.True(loaded.Preferences!.DarkTheme); Assert.Empty(loaded.Preferences.EntrySuggestions.Managers);
            File.WriteAllText(path, "{\"Version\":1,\"EntrySuggestions\":{\"Managers\":[\"Иван\"],\"Customers\":[\"Клиент\"]}}");
            loaded = store.Load(); Assert.Null(loaded.Error); Assert.Equal("Иван", Assert.Single(loaded.Preferences!.EntrySuggestions.Managers));
            Assert.Equal("Клиент", Assert.Single(loaded.Preferences.EntrySuggestions.Customers)); Assert.Empty(loaded.Preferences.EntrySuggestions.Materials);
            File.WriteAllText(path, "{\"Version\":1,\"DarkTheme\":true}"); loaded = store.Load(); Assert.Null(loaded.Error); Assert.True(loaded.Preferences!.DarkTheme); Assert.Empty(loaded.Preferences.EntrySuggestions.Customers);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
