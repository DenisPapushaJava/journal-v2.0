using System.Text;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Jornal.Core.Orders;
using Jornal.Core.Pricing;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.DataSources;
using Jornal.Infrastructure.Access;
using Jornal.Infrastructure.Demo;
using Jornal.Infrastructure.Local;
using Xunit;

namespace Jornal.Tests;

public sealed class PriceEditingTests
{
    [AvaloniaFact]
    public void ExplicitSourceWithoutPricesNeverFallsBackToDemoTariffs()
    {
        var demo = new DemoOrderStore(); var model = new MainWindowViewModel(demo, demo, demo);
        Assert.True(model.CanOpenPrices);
        Assert.False(new NewOrderViewModel(demo).HasPricing);
        model.InitializeSource(new(Path.Combine(Path.GetTempPath(), "missing.mdb"), demo));
        Assert.False(model.CanOpenPrices); Assert.Null(model.CreatePriceEditor());
        model.InitializeSource(new(Path.Combine(Path.GetTempPath(), "source.mdb"), demo) { Prices = demo.Prices });
        var readOnly = model.CreatePriceEditor()!;
        Assert.True(readOnly.IsReadOnly); Assert.Equal("Только чтение", readOnly.ReadOnlyReason);
        Assert.Equal("Общие тарифы для пользователей этой базы", readOnly.EditScope);
    }

    [Fact]
    public void ChangesCannotActivateUnusedTablesOrAlterThicknessColumns()
    {
        var table = LocalJournalFixture.Prices[1];
        foreach (var name in new[] { "PriceLM500", PriceCatalog.UnusedHansLaser })
        {
            var unused = table with { Name = name };
            Assert.Throws<ArgumentException>(() => new PriceTableChange(unused, unused).Snapshot());
        }
        foreach (var changed in new[] {
            table with { Name = PriceTables.Plasma }, table with { HasRow = false, Rates = [] }, table with { Rates = [] },
            table with { Rates = table.Rates.Select((rate, i) => i == 0 ? rate with { ColumnName = "2.5" } : rate).ToArray() },
            table with { Rates = table.Rates.Select((rate, i) => i == 0 ? rate with { RublesPerMeter = -1 } : rate).ToArray() } })
            Assert.Throws<ArgumentException>(() => new PriceTableChange(table, changed).Snapshot());
        var snapshot = new PriceTableChange(table, Replace(table, "2,5", null)).Snapshot();
        Assert.Null(snapshot.Values.Rates[0].RublesPerMeter);
        Assert.NotSame(table.Rates, snapshot.Expected.Rates);
        Assert.Throws<ArgumentException>(() => new PriceTable(table.Name, true, [new(2.5m, "2,5", 1), new(3m, "2,5", 2)]).Validate());
        Assert.Equal(6, PriceTables.All.Count); // Старый формат Linux-копии не требует седьмого прайса.
    }

    [Fact]
    public async Task LocalEditsPersistNullAndZeroWithoutChangingOrdersOtherPricesOrHistoricalAmounts()
    {
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync();
        var first = new LocalJournalStore(fixture.Path); var second = new LocalJournalStore(fixture.Path);
        var historical = await first.ReadAsync(TestContext.Current.CancellationToken);
        var original = await first.ReadAsync(PriceTables.HansLaserId2, TestContext.Current.CancellationToken);
        var values = Replace(Replace(Replace(original, "2,5", 28), "6", 0), "10", null);
        Assert.True(PriceTableChange.Matches(values, await first.SaveAsync(new(original, values), TestContext.Current.CancellationToken)));
        Assert.True(PriceTableChange.Matches(values, await second.ReadAsync(original.Name, TestContext.Current.CancellationToken)));
        Assert.Equal(historical, await second.ReadAsync(TestContext.Current.CancellationToken));
        foreach (var table in LocalJournalFixture.Prices.Where(price => price.Name != original.Name))
            Assert.True(PriceTableChange.Matches(table, await second.ReadAsync(table.Name, TestContext.Current.CancellationToken)));
        var created = await second.CreateAsync(LocalJournalFixture.Draft, TestContext.Current.CancellationToken);
        Assert.Equal(CuttingPriceCalculator.Calculate(LocalJournalFixture.Draft.PriceRequest, values).StoredPrice, created.StoredCuttingPrice);
        Assert.NotEqual(historical[0].StoredCuttingPrice, created.StoredCuttingPrice);
        await Assert.ThrowsAsync<PriceReadException>(() => first.ReadAsync(PriceCatalog.UnusedHansLaser, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StaleWholePriceSnapshotAndPreCancelledSaveCannotOverwriteAnotherClient()
    {
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync();
        var first = new LocalJournalStore(fixture.Path); var second = new LocalJournalStore(fixture.Path);
        var original = await first.ReadAsync(PriceTables.Laser, TestContext.Current.CancellationToken);
        await first.SaveAsync(new(original, Replace(original, "6", 31)), TestContext.Current.CancellationToken);
        var bytes = await File.ReadAllBytesAsync(fixture.Path, TestContext.Current.CancellationToken);
        var conflict = await Assert.ThrowsAsync<OrderWriteException>(() => second.SaveAsync(new(original, Replace(original, "2,5", 42)), TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.Conflict, conflict.Failure);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.SaveAsync(new(original, Replace(original, "2,5", 42)), cancelled.Token));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(fixture.Path, TestContext.Current.CancellationToken));
    }

    [AvaloniaFact]
    public async Task UnchangedOrRevertedPriceDraftDoesNotCallTheEditor()
    {
        var store = new DemoOrderStore();
        var editor = new UnconfirmedEditor(store, false);
        var form = new PriceEditorViewModel(store.Prices, editor, "Тест", "Тест");
        await form.RefreshCommand.ExecuteAsync(null);
        form.BeginEditCommand.Execute(null);
        await form.SaveCommand.ExecuteAsync(null);
        Assert.Equal(0, editor.Attempts);
        var row = form.Rows.First();
        var before = row.Value;
        row.Value = "99";
        Assert.True(form.CanSave);
        row.Value = before;
        Assert.False(form.CanSave);
        await form.SaveCommand.ExecuteAsync(null);
        Assert.Equal(0, editor.Attempts);
    }

    [AvaloniaFact]
    public async Task DraftValidationCancelAndSuccessfulSaveAreExplicitAndKeepHistoricalOrders()
    {
        var store = new DemoOrderStore(); var before = await store.ReadAsync(TestContext.Current.CancellationToken);
        var model = new PriceEditorViewModel(store.Prices, store.Prices, "Деможурнал", "Демо");
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.True(model.CanEdit); Assert.False(model.CanSave);
        model.BeginEditCommand.Execute(null);
        var row = model.Rows.First(); row.Value = "2+1";
        Assert.True(model.HasInvalidPrices); Assert.False(model.CanSave);
        foreach (var invalid in new[] { "-1", "1,5", "2147483648", "комплект" })
        { row.Value = invalid; Assert.True(row.HasError); Assert.False(model.CanSave); }
        row.Value = "0"; Assert.True(model.CanSave);
        Assert.Equal(25, (await store.ReadAsync(PriceTables.Laser, TestContext.Current.CancellationToken)).Rates[0].RublesPerMeter);
        model.CancelEditCommand.Execute(null); Assert.False(model.HasChanges); Assert.Equal("25", model.Rows[0].Value);
        model.BeginEditCommand.Execute(null); model.Rows[0].Value = "0"; model.Rows[1].Value = "";
        await model.SaveCommand.ExecuteAsync(null);
        Assert.False(model.HasError, model.Error); Assert.False(model.IsEditing); Assert.False(model.HasChanges);
        Assert.Equal("Прайс сохранён", model.Status);
        var saved = await store.ReadAsync(PriceTables.Laser, TestContext.Current.CancellationToken);
        Assert.Equal(0, saved.Rates[0].RublesPerMeter); Assert.Null(saved.Rates[1].RublesPerMeter);
        Assert.Equal(before, await store.ReadAsync(TestContext.Current.CancellationToken));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConflictAndLostAcknowledgementKeepDraftAndOnlyExplicitRefreshAllowsAnotherSave(bool applied)
    {
        var store = new DemoOrderStore(); var failing = new UnconfirmedEditor(store, applied);
        var model = new PriceEditorViewModel(store.Prices, failing, "База", "Источник"); await model.RefreshCommand.ExecuteAsync(null);
        model.BeginEditCommand.Execute(null); model.Rows[0].Value = "42";
        await model.SaveCommand.ExecuteAsync(null);
        Assert.True(model.RequiresReload); Assert.True(model.IsEditing); Assert.Equal("42", model.Rows[0].Value);
        Assert.False(model.CanSave); Assert.False(model.CanNavigate); Assert.True(model.CanRefresh);
        model.Rows[0].Value = "43"; Assert.False(model.CanSave);
        var actual = await store.ReadAsync(PriceTables.Laser, TestContext.Current.CancellationToken);
        await store.SaveAsync(new(actual, Replace(actual, actual.Rates[1].ColumnName, 81)), TestContext.Current.CancellationToken);
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.False(model.RequiresReload); Assert.True(model.CanSave); Assert.Equal("43", model.Rows[0].Value);
        Assert.Equal("81", model.Rows[1].Value); Assert.False(model.Rows[1].IsChanged);
        Assert.Equal(applied ? "42" : "25", model.Rows[0].CurrentPrice);
        await model.SaveCommand.ExecuteAsync(null); Assert.False(model.HasError, model.Error);
        Assert.Equal(2, failing.Attempts);
        var saved = await store.ReadAsync(PriceTables.Laser, TestContext.Current.CancellationToken);
        Assert.Equal(43, saved.Rates[0].RublesPerMeter); Assert.Equal(81, saved.Rates[1].RublesPerMeter);
    }

    [AvaloniaFact]
    public async Task FailedRefreshKeepsDirtyValuesAndUnusedAndReadOnlySourcesCannotEdit()
    {
        var store = new DemoOrderStore(); var reader = new FailingReader(store.Prices);
        var model = new PriceEditorViewModel(reader, new UnconfirmedEditor(store, false), "База", "");
        await model.RefreshCommand.ExecuteAsync(null); model.BeginEditCommand.Execute(null); model.Rows[0].Value = "47";
        await model.SaveCommand.ExecuteAsync(null); reader.Fail = true;
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.True(model.HasError); Assert.True(model.RequiresReload); Assert.Equal("47", model.Rows[0].Value); Assert.False(model.CanSave);
        var readOnly = new PriceEditorViewModel(store.Prices, null, "MDB", ""); await readOnly.RefreshCommand.ExecuteAsync(null);
        Assert.True(readOnly.IsReadOnly); Assert.False(readOnly.CanEdit);
        var unused = new PriceEditorViewModel(store.Prices, store.Prices, "Демо", "") { SelectedGroup = PriceCatalog.Groups[^1] };
        if (unused.RefreshCommand.ExecutionTask is { } task) await task;
        Assert.False(unused.CanEdit); Assert.True(unused.IsReadOnly); Assert.NotEmpty(unused.Rows);
    }

    [Fact]
    public async Task ProtocolKeepsOperationNumbersAndTreatsCorruptedWriteAcknowledgementsAsUnknown()
    {
        Assert.Equal(11, (int)AccessHostOperation.CreateOrders); Assert.Equal(12, (int)AccessHostOperation.UpdatePriceTable);
        var table = LocalJournalFixture.Prices[1]; var values = Replace(table, "2,5", 39); var change = new PriceTableChange(table, values);
        var saved = await Host(new(1, PriceTable: values)).SaveAsync(change, TestContext.Current.CancellationToken);
        Assert.True(PriceTableChange.Matches(values, saved));
        foreach (var response in new AccessHostResponse[] { new(1), new(1, []), new(1, PriceTable: table), new(2, PriceTable: values),
            new(1, PriceTable: values, Orders: []), new(1, PriceTable: values with { Name = PriceTables.Plasma }) })
        {
            var error = await Assert.ThrowsAsync<OrderWriteException>(() => Host(response).SaveAsync(change, TestContext.Current.CancellationToken));
            Assert.Equal(OrderWriteFailure.OutcomeUnknown, error.Failure);
        }
        var conflict = await Assert.ThrowsAsync<OrderWriteException>(() => Host(new(1, Error: "Конфликт прайса", Failure: OrderWriteFailure.Conflict)).SaveAsync(change, TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.Conflict, conflict.Failure);
        var blocked = new AccessHostClient("unused.mdb", "blocked", _ => throw new System.ComponentModel.Win32Exception(4551));
        Assert.Equal(OrderWriteFailure.NotSaved, (await Assert.ThrowsAsync<OrderWriteException>(() => blocked.SaveAsync(change, TestContext.Current.CancellationToken))).Failure);
    }

    internal static PriceTable Replace(PriceTable table, string column, int? value) =>
        table with { Rates = table.Rates.Select(rate => rate.ColumnName == column ? rate with { RublesPerMeter = value } : rate).ToArray() };

    private sealed class UnconfirmedEditor(DemoOrderStore store, bool applied) : IPriceEditor
    {
        public int Attempts { get; private set; }
        public async Task<PriceTable> SaveAsync(PriceTableChange change, CancellationToken cancellationToken = default)
        {
            Attempts++;
            if (Attempts > 1) return await store.SaveAsync(change, cancellationToken);
            if (applied) await store.SaveAsync(change, cancellationToken);
            throw applied ? new OrderWriteException(OrderWriteFailure.OutcomeUnknown, "Обновите прайс.") : PriceTableChange.Conflict();
        }
    }
    private sealed class FailingReader(IPriceReader inner) : IPriceReader
    {
        public bool Fail { get; set; }
        public Task<PriceTable> ReadAsync(string tableName, CancellationToken cancellationToken = default) =>
            Fail ? Task.FromException<PriceTable>(new PriceReadException("База недоступна.")) : inner.ReadAsync(tableName, cancellationToken);
    }
    internal static AccessHostClient Host(AccessHostResponse response)
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response)));
        return new("unused.mdb", "synthetic-host", process =>
        {
            if (OperatingSystem.IsWindows())
            {
                process.StartInfo.FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
                foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command", "[Console]::In.ReadLine() | Out-Null; [Console]::Out.Write([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + payload + "')))" }) process.StartInfo.ArgumentList.Add(arg);
            }
            else
            {
                process.StartInfo.FileName = "/bin/sh"; process.StartInfo.ArgumentList.Add("-c");
                process.StartInfo.ArgumentList.Add("IFS= read -r ignored\nprintf %s " + payload + " | base64 -d");
            }
            process.Start();
        });
    }
}
