using System.Security.Cryptography;
using Jornal.Core.Orders;
using Jornal.Desktop.Import;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class SpecificationImportTests
{
    [Fact]
    public async Task FractionalHtmlCountRequiresManualInputAndIsNeverRounded()
    {
        using var sample = new Sample(Sample.Html.Replace("<td>Листов:</td><td>3</td>", "<td>Листов:</td><td>1,3</td>"));
        var preview = await new SpecificationImportService().ReadAsync(sample.Path, TestContext.Current.CancellationToken);
        var form = new NewOrderViewModel(new DemoOrderStore());
        form.ApplyImport(preview);
        Assert.Empty(form.SheetCount.Value); Assert.True(form.HasImportDiagnostics);
        form.Manager.Value = "Менеджер"; form.Customer.Value = "Клиент";
        form.SheetCount.Value = "1,3";
        Assert.Equal("Изменено вручную", form.SheetCount.ImportHint);
        await form.SaveCommand.ExecuteAsync(null);
        Assert.True(form.IsSaved); Assert.Equal("1,3", form.CreatedOrder!.SheetCount);
    }

    [Fact]
    public async Task OptionalPrivateCorpusPrefillsTheSameFieldsAsIndependentExpectationsWithoutSaving()
    {
        var path = Environment.GetEnvironmentVariable("JOURNAL_BLOCK_SPEC_MANIFEST");
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(path), "Частный корпус HTML не задан.");
        using var manifest = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(path!, TestContext.Current.CancellationToken));
        foreach (var item in manifest.RootElement.EnumerateArray())
        {
            var expected = item.GetProperty("Expected");
            if (expected.ValueKind == System.Text.Json.JsonValueKind.Null) continue;
            var store = new DemoOrderStore(); var form = new NewOrderViewModel(store);
            var preview = await new SpecificationImportService().ReadAsync(item.GetProperty("Path").GetString()!, TestContext.Current.CancellationToken);
            form.ApplyImport(preview);
            if (expected.TryGetProperty("CuttingDurationMinutes", out var duration))
            {
                var minutes = duration.GetInt64();
                Assert.Equal($"{minutes / 60:00}:{minutes % 60:00}", form.CuttingTime.Value);
            }
            Assert.Equal(expected.GetProperty("SpecificationNumber").GetString(), form.SpecificationNumber.Value);
            Assert.Equal(expected.GetProperty("Material").GetString(), form.Material.Value);
            Assert.Equal(expected.GetProperty("SheetSize").GetString(), form.SheetSize.Value);
            Assert.Equal(expected.GetProperty("SheetCount").GetString(), form.SheetCount.Value);
            Assert.Equal(expected.GetProperty("WeightKilograms").GetString(), form.Weight.Value);
            Assert.Equal(expected.GetProperty("CutLengthMeters").GetInt32().ToString(), form.CutLength.Value);
            Assert.Equal(expected.GetProperty("PartCount").GetInt32(), preview.PartCount);
            Assert.Equal(expected.GetProperty("PunchCount").GetInt32(), preview.PunchCount);
            Assert.Equal(3, (await store.ReadAsync(TestContext.Current.CancellationToken)).Count);
        }
    }

    [Fact]
    public async Task CompleteImportKeepsTotalsAndCountersAndOnlySavesAfterManualReview()
    {
        using var sample = new Sample();
        var bytes = SHA256.HashData(await File.ReadAllBytesAsync(sample.Path, TestContext.Current.CancellationToken));
        var store = new DemoOrderStore();
        var form = new NewOrderViewModel(store);
        await form.RequestImportAsync(sample.Path, TestContext.Current.CancellationToken);
        Assert.True(form.HasImport); Assert.False(form.HasImportDiagnostics);
        Assert.Equal("14.05.2026", form.OrderDate.Value); Assert.Equal("0514-0078", form.SpecificationNumber.Value);
        Assert.Equal("Лазер", form.CuttingType.Value); Assert.Equal("Сплав_X77", form.Material.Value);
        Assert.Equal("6", form.Thickness.Value); Assert.Equal("1500x4000, 1000x3000", form.SheetSize.Value);
        Assert.Equal("3", form.SheetCount.Value); Assert.Equal("48.2", form.Weight.Value); Assert.Equal("33", form.CutLength.Value);
        Assert.Equal("1.1", form.Coefficient.Value); Assert.Empty(form.Manager.Value); Assert.Empty(form.Customer.Value);
        Assert.Equal("Время резки: 200:01", form.ImportPreview!.Duration);
        Assert.Equal("Не рассчитана", form.PriceText); Assert.False(form.IsSaved);
        Assert.Equal(3, (await store.ReadAsync(TestContext.Current.CancellationToken)).Count);
        await form.SaveCommand.ExecuteAsync(null);
        Assert.True(form.Manager.HasError); Assert.True(form.Customer.HasError);
        Assert.Equal(3, (await store.ReadAsync(TestContext.Current.CancellationToken)).Count);
        form.Manager.Value = "Менеджер"; form.Customer.Value = "Клиент";
        await form.SaveCommand.ExecuteAsync(null);
        var saved = Assert.IsType<OrderSummary>(form.CreatedOrder);
        Assert.Equal(14, saved.PartCount); Assert.Equal(31, saved.PunchCount);
        Assert.Equal("1.1|14|31|-1|-1", saved.StoredCoefficient);
        Assert.Equal("200:01", form.CuttingTime.Value); Assert.Equal(12001L, saved.CuttingDurationMinutes);
        Assert.Equal(4, (await store.ReadAsync(TestContext.Current.CancellationToken)).Count);
        Assert.Equal(bytes, SHA256.HashData(await File.ReadAllBytesAsync(sample.Path, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task PartialConflictingAndUnknownReportsLeaveFieldsBlankWithoutDefaults()
    {
        using var sample = new Sample(Sample.Html.Replace("<td>Пробивки:</td><td>31</td>", "")
            .Replace("</table><!--total-->", "<tr><td>Вес:(кг)</td><td>999</td></tr></table><!--total-->"));
        var form = new NewOrderViewModel(new DemoOrderStore());
        await form.RequestImportAsync(sample.Path, TestContext.Current.CancellationToken);
        Assert.Equal("0514-0078", form.SpecificationNumber.Value); Assert.Equal("33", form.CutLength.Value);
        Assert.Empty(form.Weight.Value); Assert.Empty(form.Coefficient.Value);
        Assert.True(form.HasImportDiagnostics); Assert.Contains("999", form.ImportPreview!.Diagnostics);
        Assert.Contains("48.2", form.Weight.ImportEvidence);
        form.Weight.Value = "48.2";
        Assert.Equal("Изменено вручную", form.Weight.ImportHint);
        using var unknown = new Sample("<html><body>Незнакомый отчёт</body></html>");
        var blank = new NewOrderViewModel(new DemoOrderStore());
        var original = blank.Fields.Select(field => field.Value).ToArray();
        await blank.RequestImportAsync(unknown.Path, TestContext.Current.CancellationToken);
        Assert.Equal(original, blank.Fields.Select(field => field.Value));
        Assert.False(blank.HasImport); Assert.True(blank.HasImportError);
        Assert.Contains("не распознан", blank.ImportError);
    }

    [Fact]
    public async Task ReplacingParametersRequiresAnExplicitActionAndPreservesCustomerAndManager()
    {
        using var sample = new Sample();
        var form = ManualOrderTests.ValidForm(new DemoOrderStore());
        await form.RequestImportAsync(sample.Path, TestContext.Current.CancellationToken);
        Assert.True(form.HasPendingImport); Assert.False(form.SaveCommand.CanExecute(null)); Assert.False(form.HasImport);
        Assert.Equal("HansLaser", form.CuttingType.Value);
        form.CancelImportReplacementCommand.Execute(null);
        Assert.False(form.HasPendingImport); Assert.Equal("HansLaser", form.CuttingType.Value);
        await form.RequestImportAsync(sample.Path, TestContext.Current.CancellationToken);
        await form.ReplaceImportCommand.ExecuteAsync(null);
        Assert.False(form.HasPendingImport); Assert.Equal("Лазер", form.CuttingType.Value);
        Assert.Equal("Менеджер 3", form.Manager.Value); Assert.Equal("Заказчик 'Б'", form.Customer.Value);
        Assert.Empty(form.Gas.Value); Assert.Empty(form.MaterialId.Value);
        var number = form.SpecificationNumber.Value;
        await form.RequestImportAsync(sample.Path + ".missing.html", TestContext.Current.CancellationToken);
        await form.ReplaceImportCommand.ExecuteAsync(null);
        Assert.True(form.HasImportError); Assert.Equal(number, form.SpecificationNumber.Value);
        Assert.True(form.HasImport);
    }

    [Fact]
    public async Task ReadOnlyBusyAndPendingJournalNeverStartsAnotherImport()
    {
        using var sample = new Sample();
        var store = new DemoOrderStore(); var reader = new BlockingReader();
        var readOnly = new MainWindowViewModel(store);
        Assert.Null(await readOnly.ReadSpecificationAsync([sample.Path], reader, TestContext.Current.CancellationToken));
        Assert.Equal(0, reader.Calls);
        var model = new MainWindowViewModel(store, store) { ResultText = "Письмо" };
        using var cancel = new CancellationTokenSource();
        var pending = model.ReadSpecificationAsync([sample.Path], reader, cancel.Token);
        Assert.True(model.IsImportingSpecification); Assert.False(model.CanUseJournal); Assert.False(model.CanConfigureScreen);
        Assert.False(model.CanCheckUpdates); Assert.False(model.ReloadCommand.CanExecute(null));
        Assert.Null(await model.ReadSpecificationAsync([sample.Path], reader, cancel.Token)); Assert.Equal(1, reader.Calls);
        cancel.Cancel(); Assert.Null(await pending);
        Assert.False(model.IsImportingSpecification); Assert.Equal("Письмо", model.ResultText);
        Assert.True(model.CanCreateOrder); Assert.Equal(3, (await store.ReadAsync(TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task UnknownCommitDraftCannotBeOverwrittenByANewImportAfterReload()
    {
        using var sample = new Sample(); var store = new DemoOrderStore();
        var model = new MainWindowViewModel(store, new UnconfirmedCreator());
        var pending = model.CreateOrderForm()!; var valid = ManualOrderTests.ValidForm(store);
        for (var i = 0; i < valid.Fields.Count; i++) pending.Fields[i].Value = valid.Fields[i].Value;
        await pending.SaveCommand.ExecuteAsync(null); Assert.True(model.RequiresReload);
        await model.ReloadCommand.ExecuteAsync(null); Assert.False(model.RequiresReload);
        Assert.False(model.CanImportSpecification);
        var reader = new BlockingReader();
        Assert.Null(await model.ReadSpecificationAsync([sample.Path], reader, TestContext.Current.CancellationToken)); Assert.Equal(0, reader.Calls);
        Assert.Same(pending, model.CreateOrderForm()); Assert.Equal("1015--1004", pending.SpecificationNumber.Value);
    }

    [Fact]
    public async Task CancellingAReplacementReadKeepsAllPreviousFormValues()
    {
        using var sample = new Sample(); var store = new DemoOrderStore();
        var form = ManualOrderTests.ValidForm(store); var original = form.Fields.Select(field => field.Value).ToArray();
        var reader = new BlockingReader(); form.ImportReader = reader;
        await form.RequestImportAsync(sample.Path, TestContext.Current.CancellationToken);
        var reading = form.ReplaceImportCommand.ExecuteAsync(null); Assert.True(form.IsImporting);
        form.CancelImportRead(); await reading;
        Assert.False(form.IsBusy); Assert.False(form.HasImport); Assert.False(form.HasImportError);
        Assert.Equal(original, form.Fields.Select(field => field.Value));
    }
    private sealed class UnconfirmedCreator : IManualOrderCreator
    {
        public Task<OrderSummary> CreateAsync(ManualOrderDraft draft, CancellationToken cancellationToken = default) =>
            throw new OrderWriteException(OrderWriteFailure.OutcomeUnknown, "Неподтверждённое добавление");
    }

    [Theory]
    [InlineData("a.docx")]
    [InlineData("a.txt")]
    [InlineData("")]
    public void UnsupportedInputsAreExplained(string path) => Assert.NotNull(SpecificationImportService.CheckFiles([path]));

    internal sealed class BlockingReader : ISpecificationImportService
    {
        public int Calls { get; private set; }
        public TaskCompletionSource<SpecificationImportPreview> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<SpecificationImportPreview> ReadAsync(string path, CancellationToken cancellationToken)
        { Calls++; return await Result.Task.WaitAsync(cancellationToken); }
    }

    internal sealed class Sample : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "journal-import-" + Guid.NewGuid().ToString("N"));
        public string Path { get; }
        public Sample(string? html = null)
        { Directory.CreateDirectory(_directory); Path = System.IO.Path.Combine(_directory, "пример.html"); File.WriteAllText(Path, html ?? Html); }
        public void Dispose() => Directory.Delete(_directory, true);
        public const string Html = """
        <html><meta charset="utf-8"><body>
        <table><tr><td>Спецификация № 6 мм 2026-05-14-0078--ЛАЗЕР.rcam</td></tr></table>
        <table><tr><td>Листов:</td><td>3</td><td>Габариты:</td><td>6x4000x1500 1шт</td><td>6x3000x1000 2шт</td></tr></table>
        <table><tr><td>Материал:</td><td>Сплав_X77 6 mm</td><td>Одна деталь</td><td>Все детали</td></tr></table>
        <table><tr><td>Всего по заказу:</td><td>Общие кол-во деталей:</td><td>14</td></tr>
        <tr><td>Длина реза:(мм)</td><td>32500.5</td></tr><tr><td>Пробивки:</td><td>31</td></tr>
        <tr><td>Вес:(кг)</td><td>48.2</td></tr><tr><td>Время:(ч:мм)</td><td>200:01</td></tr></table><!--total-->
        </body></html>
        """;
    }
}
