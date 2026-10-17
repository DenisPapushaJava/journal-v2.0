using System.Text;
using Avalonia.Input;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Platform.Storage;
using Jornal.Core.Orders;
using Jornal.Core.Results;
using Jornal.Desktop.Clipboard;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class JournalClipboardTests
{
    internal static OrderSummary Order(int id = 100) => new(id, new(2026, 10, 17), "1017-1234", "Иван", "Пример", "Лазер", "Ст3", 2.5m, 1000m, null) { DeliveryNumber = "РП-00012345" };
    [Fact]
    public void RowAndTelegramPreserveLegacyFormatAndSeparateHeadersBeforeMachines()
    {
        var order = Order();
        Assert.Equal("(2,5 мм)  1017-1234,  Иван,  (Пример),  РП-00012345", OrderCommentFormatter.Row(order));
        Assert.Equal("12345  Иван (Пример)\n**2,5 мм Ст3**   на Лазер\n\n", OrderCommentFormatter.Telegram([order]));
        var gas = order with { Id = 101, CuttingType = "Газо-кислородная", ThicknessMillimeters = 20 };
        var hydro = order with { Id = 102, CuttingType = "Гидроабразивная" };
        var another = order with { Id = 103, DeliveryNumber = "РП-00000042", CustomerName = "Другой заказчик" };
        var text = OrderCommentFormatter.Telegram([gas, hydro, order, another]);
        Assert.True(text.IndexOf("на Лазер", StringComparison.Ordinal) < text.IndexOf("на Кристалл", StringComparison.Ordinal));
        Assert.Contains("на Кристалл резка газом\n\n**2,5 мм Ст3**   на Гидроабразивная", text);
        Assert.Contains("42  Иван (Другой заказчик)", text);
        var collision = OrderCommentFormatter.Telegram([order, order with { Id = 104, DeliveryNumber = "ДРУГОЙ-12345" }]);
        Assert.Contains("РП-00012345  Иван", collision); Assert.Contains("ДРУГОЙ-12345  Иван", collision);
        Assert.Throws<ArgumentException>(() => OrderCommentFormatter.Telegram([order, order with { DeliveryNumber = "" }]));
        Assert.Equal("0  Иван (Пример)\n**2,5 мм Ст3**   на Лазер\n\n", OrderCommentFormatter.Telegram([order with { DeliveryNumber = "00000" }]));
    }
    [Theory]
    [InlineData("CON")][InlineData("nul.txt")][InlineData("COM¹")][InlineData("lpt9.")][InlineData("AUX ")][InlineData("CONOUT$")]
    public void ReservedWindowsNamesCannotBecomeDevicePaths(string name) => Assert.StartsWith("_", OrderFolderPlan.SafeName(name));
    [Fact]
    public void FolderNamesRemainUniqueSafeAndKeepIdentifiersWhenShortened()
    {
        var order = Order() with { CustomerName = "ООО \"Пример:/\\*?\" " + new string('Я', 220) };
        var plan = OrderFolderPlan.Create([order, order with { Id = 101 }]);
        Assert.Equal(2, plan.Items.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var item in plan.Items)
        {
            Assert.True(item.Changed); Assert.True(item.Name.Length <= 140);
            Assert.DoesNotContain(item.Name, c => "<>:\"/\\|?*".Contains(c) || char.IsControl(c));
            Assert.Contains("1017-1234", item.Name); Assert.Contains("РП-00012345", item.Name);
        }
        var unicode = OrderFolderPlan.SafeName(string.Concat(Enumerable.Repeat("😀", 200)) + "-12345");
        _ = new UTF8Encoding(false, true).GetBytes(unicode); Assert.EndsWith("12345", unicode);
        Assert.Equal("Заявка", OrderFolderPlan.SafeName(".."));
    }
    [Fact]
    public async Task CopyUsesExactlyTheSelectionPreservesLetterAndNeverWritesOrders()
    {
        var store = new DemoOrderStore(); var model = new MainWindowViewModel(store);
        await model.ReloadCommand.ExecuteAsync(null); model.AcceptCreatedOrders([Order(), Order(101) with { CustomerName = "Другой" }]);
        var rows = model.Orders.Where(row => row.Id >= 100).ToArray(); model.UpdateSelection(rows, rows[0]); model.ResultText = "Письмо";
        var clipboard = new RecordingJournalClipboard();
        Assert.False(model.CanCopyRowComment); Assert.True(model.CanCopyTelegramComment);
        await model.CopyCommentAsync(clipboard); Assert.Null(clipboard.Text);
        await model.CopyCommentAsync(clipboard, true); Assert.Contains("Пример", clipboard.Text); Assert.Contains("Другой", clipboard.Text);
        Assert.Equal("Письмо", model.ResultText); Assert.Equal(3, (await store.ReadAsync(TestContext.Current.CancellationToken)).Count);
        model.UpdateSelection([rows[0]], rows[0]); Assert.True(model.CanCopyRowComment);
        clipboard.Failure = true; await model.CopyCommentAsync(clipboard); Assert.True(model.HasError); Assert.Equal("Письмо", model.ResultText);
        clipboard.Failure = false;
        var form = model.CreateFolderForm(clipboard)!; Assert.Single(form.Items);
        await form.CopyCommand.ExecuteAsync(null); Assert.Single(clipboard.Plan!.Items); Assert.False(form.IsBusy);
        Assert.Contains("Папки готовы", model.UpdatedText); Assert.Equal("Письмо", model.ResultText);
    }
    [Fact]
    public async Task StagingRejectsTraversalAndRemovesOnlyExpiredOwnedEmptyFolders()
    {
        using var temporary = new Temporary(); var clock = new Clock(); var store = new TemporaryFolderStore(temporary.Root, clock);
        await Assert.ThrowsAsync<ArgumentException>(() => store.PrepareAsync(new([new(1, "bad", "../escape")]), TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(temporary.Root));
        var plan = OrderFolderPlan.Create([Order()], store.MaximumNameLength);
        var kept = await store.PrepareAsync(plan, TestContext.Current.CancellationToken);
        var empty = await store.PrepareAsync(plan, TestContext.Current.CancellationToken);
        var note = Path.Combine(kept[0], "user.txt"); await File.WriteAllTextAsync(note, "Данные пользователя", TestContext.Current.CancellationToken);
        clock.Utc = clock.Utc.AddDays(8); await store.CleanupExpiredAsync();
        Assert.True(File.Exists(note)); Assert.False(Directory.Exists(Path.GetDirectoryName(empty[0]))); Assert.True(Directory.Exists(kept[0]));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.PrepareAsync(plan, cancelled.Token));
    }
    [Fact]
    public async Task StagingNeverFollowsALinkToSomeoneElsesDirectory()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Проверка символьной ссылки на Linux.");
        using var temporary = new Temporary(); var outside = Path.Combine(temporary.Parent, "outside"); Directory.CreateDirectory(outside);
        var note = Path.Combine(outside, "user.txt"); await File.WriteAllTextAsync(note, "Сохранить", TestContext.Current.CancellationToken);
        Directory.CreateSymbolicLink(temporary.Root, outside); var store = new TemporaryFolderStore(temporary.Root);
        await Assert.ThrowsAsync<IOException>(() => store.PrepareAsync(OrderFolderPlan.Create([Order()], store.MaximumNameLength), TestContext.Current.CancellationToken));
        await store.CleanupExpiredAsync(); Assert.Equal("Сохранить", await File.ReadAllTextAsync(note, TestContext.Current.CancellationToken));
    }
    [AvaloniaFact]
    public async Task FilePayloadUsesStorageItemsWithExplicitCopyIntentAndEscapedUris()
    {
        using var temporary = new Temporary(); var path = Path.Combine(temporary.Parent, "Заказ с пробелом"); Directory.CreateDirectory(path);
        var window = new Window();
        try
        {
            window.Show(); using var item = await window.StorageProvider.TryGetFolderFromPathAsync(path); Assert.NotNull(item);
            using var payload = (IAsyncDataTransfer)FolderClipboardPayload.Create([item]);
            Assert.Equal(item, Assert.Single((await payload.TryGetFilesAsync())!));
            var bytes = await payload.TryGetValueAsync(FolderClipboardPayload.CopyFormat);
            if (OperatingSystem.IsWindows()) Assert.Equal(new byte[] { 1, 0, 0, 0 }, bytes);
            else if (OperatingSystem.IsLinux()) Assert.Equal("copy\n" + item.Path.AbsoluteUri + "\n", Encoding.UTF8.GetString(bytes!));
        }
        finally { window.Close(); }
    }

    private sealed class Clock : TimeProvider { public DateTimeOffset Utc = new(2026, 10, 17, 0, 0, 0, TimeSpan.Zero); public override DateTimeOffset GetUtcNow() => Utc; }
    private sealed class Temporary : IDisposable
    {
        public string Parent { get; } = Path.Combine(Path.GetTempPath(), "jr-" + Guid.NewGuid().ToString("N"));
        public string Root => Path.Combine(Parent, "clipboard");
        public Temporary() => Directory.CreateDirectory(Parent);
        public void Dispose() { if (Directory.Exists(Parent)) Directory.Delete(Parent, true); }
    }

}
internal sealed class RecordingJournalClipboard : IJournalClipboard
{
    public int MaximumFolderNameLength => 140;
    public bool Failure; public string? Text; public OrderFolderPlan? Plan;
    public Task WriteTextAsync(string text) { if (Failure) throw new IOException("Тестовая ошибка"); Text = text; return Task.CompletedTask; }
    public Task WriteFoldersAsync(OrderFolderPlan plan) { if (Failure) throw new IOException("Тестовая ошибка"); Plan = plan; return Task.CompletedTask; }
}
