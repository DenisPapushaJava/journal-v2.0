using System.Text.Json;
using Avalonia.Headless.XUnit;
using Jornal.Core.Orders;
using Jornal.Desktop.DataSources;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Access;
using Xunit;

namespace Jornal.Tests;

public sealed class OrderSourceTests
{
    [AvaloniaFact]
    public async Task ConnectsReadOnlyDataAndClearsOnlySourceDependentState()
    {
        var (model, settings, reader) = Create();
        await model.ReloadCommand.ExecuteAsync(null);
        model.UpdateSelection(model.Orders, model.Orders[0]);
        model.AddOrdersToResult(model.Orders);
        model.SearchText = "5083";
        model.IsDarkTheme = true;
        var form = model.CreateDatabaseSettingsForm()!;
        form.UseDatabase = true; form.DatabasePath = DatabasePath;
        await form.ConnectCommand.ExecuteAsync(null);
        Assert.True(form.IsConnected, form.Error);
        Assert.Equal(DatabasePath, settings.Saved!.DatabasePath);
        Assert.Equal(DatabasePath, model.DatabasePath);
        Assert.Contains("только чтение", model.SourceDescription);
        Assert.Equal(DatabasePath, model.SourceDetails);
        Assert.Empty(model.ResultText); Assert.Empty(model.SearchText); Assert.True(model.IsDarkTheme);
        Assert.Equal(42, Assert.Single(model.Orders).Id);
        Assert.Equal(1234.5678m, model.Orders[0].StoredCuttingPrice);
        Assert.False(model.CanCreateOrder);
        model.UpdateSelection(model.Orders, model.Orders[0]);
        Assert.False(model.CanUpdateSelectedOrders); Assert.True(model.CanAddResult);
        model.AddOrdersToResult(model.Orders); Assert.NotEmpty(model.ResultText);
        var result = model.ResultText;
        reader.Fail = true;
        await model.ReloadCommand.ExecuteAsync(null);
        Assert.Equal(result, model.ResultText); Assert.Equal(42, Assert.Single(model.Orders).Id);
        Assert.Equal("Сетевая папка недоступна.", model.ErrorMessage);
        reader.Fail = false;
        await model.ReloadCommand.ExecuteAsync(null);
        Assert.False(model.HasError); Assert.Equal(result, model.ResultText);
        var demo = model.CreateDatabaseSettingsForm()!; demo.UseDemo = true;
        await demo.ConnectCommand.ExecuteAsync(null);
        Assert.True(demo.IsConnected); Assert.Null(model.DatabasePath); Assert.True(model.CanCreateOrder);
        Assert.Equal(3, model.Orders.Count); Assert.Empty(model.ResultText);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedReadOrPreferenceSavePreservesThePreviousScreen(bool failSave)
    {
        var (model, settings, reader) = Create();
        await model.ReloadCommand.ExecuteAsync(null);
        model.UpdateSelection(model.Orders, model.Orders[0]); model.AddOrdersToResult(model.Orders);
        var rows = model.Orders.ToArray(); var result = model.ResultText;
        var form = model.CreateDatabaseSettingsForm()!; form.UseDatabase = true; form.DatabasePath = DatabasePath;
        settings.Fail = failSave; reader.Fail = !failSave;
        await form.ConnectCommand.ExecuteAsync(null);
        Assert.False(form.IsConnected); Assert.True(form.HasError); Assert.Null(settings.Saved);
        Assert.Equal(rows, model.Orders); Assert.Equal(result, model.ResultText);
        Assert.Null(model.DatabasePath); Assert.True(model.CanCreateOrder); Assert.True(model.CanUpdateSelectedOrders);
        Assert.Equal(DatabasePath, form.DatabasePath);
        reader.Fail = false; settings.Fail = false;
        await form.ConnectCommand.ExecuteAsync(null);
        Assert.True(form.IsConnected, form.Error);
    }

    [AvaloniaFact]
    public async Task CancellationDoesNotSwitchSourcesAndBlocksConcurrentActions()
    {
        var settings = new MemorySettings(); var reader = new DelayedReader();
        var manager = new OrderSourceManager(settings, _ => reader);
        var model = Model(manager);
        await model.ReloadCommand.ExecuteAsync(null);
        var form = model.CreateDatabaseSettingsForm()!; form.UseDatabase = true; form.DatabasePath = DatabasePath;
        var connecting = form.ConnectCommand.ExecuteAsync(null);
        Assert.True(model.IsLoadingOrders); Assert.False(model.CanConfigureSource); Assert.False(model.CanCreateOrder);
        Assert.False(model.CanInteractWithTable); Assert.False(model.ReloadCommand.CanExecute(null));
        form.ConnectCommand.Cancel();
        await connecting;
        Assert.Null(model.DatabasePath); Assert.Null(settings.Saved); Assert.Equal(3, model.Orders.Count);
        Assert.True(model.CanConfigureSource); Assert.False(form.IsConnected);
    }

    [AvaloniaFact]
    public async Task BrokenSavedSourceDoesNotSilentlyFallBackToDemo()
    {
        var settings = new MemorySettings { Loaded = new(new(DatabasePath: DatabasePath)) };
        var manager = new OrderSourceManager(settings, _ => new Reader { Fail = true });
        var model = Model(manager);
        await model.ReloadCommand.ExecuteAsync(null);
        Assert.Empty(model.Orders); Assert.True(model.HasError); Assert.False(model.CanCreateOrder);
        Assert.Equal(DatabasePath, model.DatabasePath); Assert.Null(settings.Saved);
        settings.Loaded = new(null, "Настройки повреждены.");
        model = Model(manager);
        await model.ReloadCommand.ExecuteAsync(null);
        Assert.Equal("Настройки повреждены.", model.ErrorMessage);
        Assert.Equal("Источник не подключён", model.SourceDescription); Assert.False(model.CanCreateOrder);
    }

    [Fact]
    public void SettingsKeepDatabasePathSeparateAndRecoverFromCorruption()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "data-source.json"); var store = new JsonOrderSourceSettingsStore(path);
            Assert.Null(store.Load().Settings!.DatabasePath);
            var settings = new OrderSourceSettings(DatabasePath: DatabasePath);
            store.Save(settings);
            Assert.Equal(settings, new JsonOrderSourceSettingsStore(path).Load().Settings);
            Assert.Single(Directory.GetFiles(directory));
            File.WriteAllText(path, JsonSerializer.Serialize(new OrderSourceSettings(99, DatabasePath)));
            Assert.NotNull(store.Load().Error);
            File.WriteAllText(path, "{broken");
            Assert.NotNull(store.Load().Error); Assert.Equal("{broken", File.ReadAllText(path));
            foreach (var invalid in new[] { "{}", "[]", "{\"Version\":1}", "{\"Version\":\"1\",\"DatabasePath\":null}" })
            {
                File.WriteAllText(path, invalid);
                Assert.NotNull(store.Load().Error); Assert.Null(store.Load().Settings);
            }
            store.Save(new()); Assert.Null(store.Load().Settings!.DatabasePath);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("")]
    [InlineData("OrderDB.mdb")]
    [InlineData("/tmp/OrderDB.txt")]
    public async Task InvalidPathIsRejectedBeforeAccess(string path)
    {
        var form = new DatabaseSettingsViewModel(null, (_, _) => throw new InvalidOperationException())
            { UseDatabase = true, DatabasePath = path };
        await form.ConnectCommand.ExecuteAsync(null);
        Assert.True(form.HasError); Assert.False(form.IsConnected);
    }

    internal static string DatabasePath => Path.Combine(Path.GetTempPath(), "Journal test's folder", "OrderDB.mdb");
    internal static (MainWindowViewModel Model, MemorySettings Settings, Reader Reader) Create()
    {
        var settings = new MemorySettings(); var reader = new Reader();
        return (Model(new OrderSourceManager(settings, _ => reader)), settings, reader);
    }
    internal static MainWindowViewModel Model(OrderSourceManager manager)
    {
        var source = manager.Load();
        var model = new MainWindowViewModel(source.Reader, source.Creator, source.Editor, manager);
        model.InitializeSource(source); return model;
    }
    internal sealed class MemorySettings : IOrderSourceSettingsStore
    {
        public OrderSourceSettings? Saved { get; private set; }
        public bool Fail { get; set; }
        public OrderSourceSettingsLoad Loaded { get; set; } = new(new());
        public OrderSourceSettingsLoad Load() => Saved is null ? Loaded : new(Saved);
        public void Save(OrderSourceSettings settings)
        {
            if (Fail) throw new IOException("Сохранение невозможно.");
            Saved = settings;
        }
    }
    internal sealed class Reader : IOrderReader
    {
        public bool Fail { get; set; }
        public Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken cancellationToken = default) =>
            Fail ? throw new OrderReadException("Сетевая папка недоступна.") :
                Task.FromResult<IReadOnlyList<OrderSummary>>([AccessOrderMapper.Map(AccessOrderTests.Row())]);
    }
    internal sealed class DelayedReader : IOrderReader
    {
        public Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken cancellationToken = default) =>
            new TaskCompletionSource<IReadOnlyList<OrderSummary>>().Task.WaitAsync(cancellationToken);
    }
}
