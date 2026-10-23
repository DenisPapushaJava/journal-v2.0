using System.Text.Json;
using Jornal.Core.Orders;
using Jornal.Core.Results;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Access;
using Jornal.Infrastructure.Demo;
using Jornal.Infrastructure.Local;
using Jornal.Infrastructure.Trash;
using Xunit;

namespace Jornal.Tests;

public sealed class OrderAnnotationsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void OptionalAnnotationsValidateTheirLengthAndIdentityWithoutChangingLegacyFields()
    {
        var row = LocalJournalFixture.Draft.ToOrder(42, 500);
        Assert.Null(OrderAnnotations.For(row, false, ""));
        var value = OrderAnnotations.For(row, true, new string('я', 255))!;
        value.Validate();
        Assert.Equal(value, JsonSerializer.Deserialize<OrderAnnotations>(JsonSerializer.Serialize(value)));
        Assert.True(value.Matches(row with { Id = 55, CuttingType = "ГАР", CutLengthMeters = 999, Color = 77, CustomerName = "Другой" }));
        Assert.False(value.Matches(row with { SpecificationNumber = "другой номер" }));
        Assert.False(value.Matches(row with { OrderDate = row.OrderDate!.Value.AddDays(1) }));
        Assert.Throws<OrderWriteException>(() => OrderAnnotations.For(row, false, new string('я', 256)));
        Assert.Throws<OrderWriteException>(() => OrderAnnotations.For(row, false, "Нельзя\0"));
        Assert.Throws<OrderWriteException>(() => (value with { IdentityHash = "bad" }).Validate());
        OrderAnnotations.ValidateComment("Строка 1\r\nСтрока 2\t'NULL'");
        var record = OrderRecord.FromSummary(row);
        Assert.Equal(record, (record with { Annotations = value }) with { Annotations = null });
        Assert.Equal(value, OrderRecord.FromSummary(row with { Annotations = value }).ToSummary().Annotations);
    }

    [Fact]
    public void CuttingTaskMarksOnlyItsOwnRowNeverIncludesCommentAndKeepsNumericOrder()
    {
        var draft = LocalJournalFixture.Draft;
        var thin = draft.ToOrder(1, 500) with { ThicknessMillimeters = 3, MaterialName = "Ст3", DeliveryNumber = "9285" };
        var thick = thin with { Id = 2, ThicknessMillimeters = 10 };
        thin = thin with { Annotations = OrderAnnotations.For(thin, true, "СЕКРЕТНЫЙ КОММЕНТАРИЙ") };
        thick = thick with { Annotations = OrderAnnotations.For(thick, false, "ЕЩЁ КОММЕНТАРИЙ") };
        var text = OrderCommentFormatter.Telegram([thick, thin]);
        Assert.Contains("3 мм Ст3 ГИБКА", text); Assert.Contains("10 мм Ст3  ", text);
        Assert.True(text.IndexOf("3 мм", StringComparison.Ordinal) < text.IndexOf("10 мм", StringComparison.Ordinal));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, "ГИБКА"));
        Assert.DoesNotContain("КОММЕНТАРИЙ", text);
        var letter = OrderResultFormatter.Format([thin]);
        Assert.Equal(OrderResultFormatter.Format([thin with { Annotations = null }]).TrimEnd('\n') + "   ГИБКА\n", letter);
        Assert.DoesNotContain("КОММЕНТАРИЙ", letter);
        Assert.Equal(OrderCommentFormatter.Row(thin with { Annotations = null }), OrderCommentFormatter.Row(thin));
        Assert.Throws<ArgumentException>(() => OrderCommentFormatter.Telegram([thin with { AnnotationsUnavailable = true }]));
        var stale = thin with { SpecificationNumber = "новая" };
        Assert.True(stale.AnnotationsNeedReview); Assert.False(stale.HasBending); Assert.Equal("", stale.Comment);
        Assert.Throws<ArgumentException>(() => OrderCommentFormatter.Telegram([stale]));
    }

    [Fact]
    public async Task CreateAndEditHaveIndependentOptionalFieldsAndConciseDiff()
    {
        var store = new DemoOrderStore(); var form = ManualOrderTests.ValidForm(store);
        Assert.False(form.Annotations.HasBending); Assert.Equal("", form.Annotations.Comment);
        form.Annotations.HasBending = true; form.Annotations.Comment = "Гнуть по чертежу";
        await form.SaveCommand.ExecuteAsync(null);
        Assert.True(form.IsSaved); Assert.True(form.CreatedOrder!.HasBending); Assert.Equal("Гнуть по чертежу", form.CreatedOrder.Comment);
        var record = (await store.Maintenance.ReadRecordAsync(form.CreatedOrder.Id, Token))!;
        var edit = new EditOrderViewModel(record, store.Maintenance, "test", _ => Task.FromResult<OrderRecord?>(record));
        Assert.True(edit.Annotations.HasBending); Assert.Empty(edit.Changes);
        edit.Annotations.HasBending = false; edit.Annotations.Comment = "Исправлено";
        Assert.Contains(edit.Changes, change => change.Label == "Гибка");
        Assert.Contains(edit.Changes, change => change.Label == "Комментарий");
        var values = edit.BuildChange().Values;
        Assert.False(values.Annotations!.HasBending); Assert.Equal("Исправлено", values.Annotations.Comment);
        edit.Annotations.Comment = ""; Assert.Null(edit.BuildChange().Values.Annotations);
        edit.Annotations.Comment = new string('я', 256);
        Assert.Throws<ArgumentException>(() => edit.BuildChange()); Assert.True(edit.Annotations.HasCommentError);
    }

    [Fact]
    public async Task IdentityChangesRequireExplicitReviewButMachineChangesKeepManualNotes()
    {
        var record = OrderMaintenanceTests.Record();
        record = record with { Annotations = OrderAnnotations.For(record.ToSummary(), true, "Ручная пометка") };
        var edit = new EditOrderViewModel(record, new DemoOrderStore().Maintenance, "test", _ => Task.FromResult<OrderRecord?>(record));
        edit.Weight.Value = "12"; edit.CuttingType.Value = "Плазма";
        Assert.False(edit.Annotations.NeedsReview); Assert.True(edit.BuildChange().Values.ToSummary().HasBending);
        edit.SpecificationNumber.Value = "новая";
        Assert.True(edit.Annotations.NeedsReview); Assert.False(edit.CanSave);
        Assert.Throws<ArgumentException>(() => edit.BuildChange());
        edit.Annotations.ConfirmReviewCommand.Execute(null);
        Assert.True(edit.CanSave); Assert.True(edit.BuildChange().Values.ToSummary().HasBending);
        var stale = record with { Specification = "legacy-change" };
        var reviewed = new OrderAnnotationsEditor(); reviewed.Load(stale.Annotations, stale.ToSummary());
        Assert.True(reviewed.NeedsReview); Assert.False(reviewed.Validate());
        reviewed.ConfirmReviewCommand.Execute(null); Assert.True(reviewed.Validate());
        Assert.True(reviewed.Build(stale.ToSummary())!.Matches(stale.ToSummary()));
        // Импорт новой спецификации сохраняет ручные значения, а не подменяет их пустыми.
        var imported = new EditOrderViewModel(record, new DemoOrderStore().Maintenance, "test", _ => Task.FromResult<OrderRecord?>(record))
        { ImportReader = new BatchImportTests.Reader(await BatchImportTests.Preview()) };
        await imported.RequestImportAsync(["new.html"], Token);
        Assert.True(imported.HasPendingImport); imported.ReplaceImportCommand.Execute(null);
        Assert.True(imported.Annotations.HasBending); Assert.Equal("Ручная пометка", imported.Annotations.Comment);
    }

    [Fact]
    public async Task SingleToBatchKeepsFirstAnnotationsAndValidatesEveryCardIndependently()
    {
        var store = new DemoOrderStore(); var form = ManualOrderTests.ValidForm(store);
        form.Annotations.HasBending = true; form.Annotations.Comment = "Первая";
        form.ImportReader = new BatchImportTests.Reader(await BatchImportTests.Preview());
        await form.ImportFilesAsync(["one.html", "two.html"], Token);
        Assert.Equal(3, form.BatchItems.Count);
        Assert.True(form.BatchItems[0].Form.Annotations.HasBending);
        Assert.Equal("Первая", form.BatchItems[0].Form.Annotations.Comment);
        Assert.All(form.BatchItems.Skip(1), item => { Assert.False(item.Form.Annotations.HasBending); Assert.Equal("", item.Form.Annotations.Comment); });
        var other = form.BatchItems[1].Form.Annotations;
        other.Comment = new string('я', 256); Assert.False(form.SaveCommand.CanExecute(null));
        other.Comment = "Вторая"; other.HasBending = true;
        Assert.True(form.SaveCommand.CanExecute(null));
        await form.SaveCommand.ExecuteAsync(null);
        Assert.True(form.IsSaved); Assert.Equal(3, form.CreatedOrders.Count);
        Assert.Equal(new[] { "Первая", "Вторая", "" }, form.CreatedOrders.Select(row => row.Comment));
        Assert.Equal(new[] { true, true, false }, form.CreatedOrders.Select(row => row.HasBending));
    }

    [Fact]
    public async Task LocalReloadConflictsAndDurableTrashPreserveBothAnnotationsAndTime()
    {
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync();
        var store = new LocalJournalStore(fixture.Path);
        Assert.All(await store.ReadAsync(Token), row => Assert.Null(row.Annotations));
        var created = await store.CreateAsync(LocalJournalFixture.Draft with { HasBending = true, Comment = "Локально\n'NULL'", CuttingDurationMinutes = 1786 }, Token);
        var record = (await new LocalJournalStore(fixture.Path).ReadRecordAsync(created.Id, Token))!;
        Assert.True(record.ToSummary().HasBending); Assert.Equal("Локально\n'NULL'", record.ToSummary().Comment);
        var changed = await store.UpdateAsync(new(record, record with { Annotations = OrderAnnotations.For(record.ToSummary(), false, "Правка") }), Token);
        var conflict = await Assert.ThrowsAsync<OrderWriteException>(() => store.UpdateAsync(new(record, record with { Customer = "Чужая правка" }), Token));
        Assert.Equal(OrderWriteFailure.Conflict, conflict.Failure);
        var colored = Assert.Single(await store.SetColorAsync([changed.ToSummary()], 123, Token));
        Assert.Equal(changed.Annotations, colored.Annotations);
        var delivered = Assert.Single(await store.SetDeliveryNumberAsync([colored], "9285", Token));
        Assert.Equal(changed.Annotations, delivered.Annotations);
        record = (await store.ReadRecordAsync(created.Id, Token))!;
        var archive = new JsonTrashStore(Path.Combine(fixture.Directory, "Trash"));
        var service = new OrderTrashService(store, new("local", fixture.Path), archive);
        var entry = await service.DeleteAsync(record, Token);
        var restored = await new OrderTrashService(new LocalJournalStore(fixture.Path), new("local", fixture.Path), archive).RestoreAsync(entry.OperationId, Token);
        Assert.Equal(record, restored); Assert.Equal(1786L, restored.ToSummary().CuttingDurationMinutes);
        Assert.Equal("Правка", restored.ToSummary().Comment); Assert.False(restored.ToSummary().HasBending);
    }

    [Theory]
    [InlineData("-1", true)] [InlineData("1", true)] [InlineData("0", false)] [InlineData("true", true)]
    public void MdbExportReadsTypedBendingAndQuotedComments(string flag, bool expected)
    {
        var csv = "OrderId,HasBending,Comment,IdentityHash,FormatVersion\n42," + flag + ",\"строка 1\nстрока \"\"2\"\"\",\"" + new string('A', 64) + "\",1\n";
        var value = OrderAnnotationsData.ReadExport(new StringReader(csv), Token)[42];
        Assert.Equal(expected, value.HasBending); Assert.Equal("строка 1\nстрока \"2\"", value.Comment);
        Assert.Throws<OrderReadException>(() => OrderAnnotationsData.ReadExport(new StringReader(csv.Replace(",1\n", ",2\n")), Token));
        Assert.Throws<OrderReadException>(() => OrderAnnotationsData.ReadExport(new StringReader(csv.Replace("," + flag + ",", ",NULL,")), Token));
    }

    [Fact]
    public async Task AnnotationPreparationFailureDoesNotHideRowsOrRepeatSuccessfulDurationPreparation()
    {
        var initializer = new Initializer(); var store = new DemoOrderStore();
        var reader = new OrderDurationPreparingReader(store, initializer, initializer);
        Assert.Equal(await store.ReadAsync(Token), await reader.ReadAsync(Token));
        Assert.Contains("Занято", reader.PreparationWarning);
        initializer.FailAnnotations = false;
        await reader.ReadAsync(Token); Assert.Null(reader.PreparationWarning);
        await reader.ReadAsync(Token);
        Assert.Equal(1, initializer.DurationCalls); Assert.Equal(2, initializer.AnnotationCalls);
    }

    private sealed class Initializer : IOrderDurationInitializer, IOrderAnnotationsInitializer
    {
        public int DurationCalls, AnnotationCalls;
        public bool FailAnnotations = true;
        public Task PrepareCuttingTimeAsync(CancellationToken cancellationToken = default)
        { DurationCalls++; return Task.CompletedTask; }
        public Task PrepareAnnotationsAsync(CancellationToken cancellationToken = default)
        {
            AnnotationCalls++;
            return FailAnnotations ? Task.FromException(new OrderWriteException(OrderWriteFailure.NotSaved, "Занято")) : Task.CompletedTask;
        }
    }
}
