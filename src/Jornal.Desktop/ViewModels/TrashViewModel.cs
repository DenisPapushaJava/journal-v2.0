using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jornal.Core.Orders;
using Jornal.Infrastructure.Trash;

namespace Jornal.Desktop.ViewModels;

public sealed record TrashItem(TrashEntry Entry)
{
    public string Specification => Entry.Record.Specification ?? "—";
    public string Customer => Entry.Record.Customer ?? "—";
    public string DeletedAt => Entry.DeletedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
    public string State => Entry.State switch { TrashState.Deleted => "В корзине", TrashState.DeletePending => "Проверить удаление", _ => "Проверить восстановление" };
    public string Retention => Entry.State == TrashState.Deleted
        ? $"Осталось дней: {Math.Max(0, (int)Math.Ceiling(OrderTrashService.RetentionDays - (DateTimeOffset.UtcNow - (Entry.ConfirmedAt ?? Entry.DeletedAt)).TotalDays))}"
        : "Хранится до проверки";
}

public partial class TrashViewModel(OrderTrashService service, string sourceDescription, Action<int, OrderRecord?> changed,
    Func<CancellationToken, Task> ensureReload, Func<Func<Task>, Task> runWrite, Action<Exception> failed,
    Action<IReadOnlyList<TrashEntry>>? entriesChanged = null) : ObservableObject
{
    public string SourceDescription { get; } = sourceDescription;
    public ObservableCollection<TrashItem> Items { get; } = [];
    public AccessHostHelpViewModel AccessHostHelp { get; } = new();
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanAct)), NotifyPropertyChangedFor(nameof(SelectedDetails))] private TrashItem? _selected;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasError))] private string? _error;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanAct))] private bool _isWriting;
    public bool HasError => Error is not null;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBusy)), NotifyPropertyChangedFor(nameof(CanAct))] private bool _isRefreshing;
    public bool IsBusy => IsWriting || IsRefreshing;
    public bool CanAct => Selected?.Entry.State == TrashState.Deleted && !IsBusy;
    public string CountText => $"Записей в корзине: {Items.Count}";
    public string SelectedDetails => Selected is { Entry.Record: var row } ?
        MainWindowViewModel.Describe(row) + $"\nДата исходная: {row.Date:dd.MM.yyyy HH:mm:ss} · {row.CuttingType} · {row.Material} · {row.Thickness} мм\n" +
        $"Лист: {row.SheetSize} · количество: {row.SheetCount} · вес: {row.Weight} · рез: {row.CutLength} м\n" +
        $"Цена: {row.Price?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "NULL"} · БЛЦБ: {row.DeliveryNumber ?? "NULL"} · цвет: {row.Color?.ToString() ?? "NULL"}\nКоэффициент: {row.Coefficient ?? "NULL"}" : "Выберите архивную заявку";

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (IsWriting) return;
        IsRefreshing = true; Error = null;
        try
        {
            await ensureReload(cancellationToken);
            var selected = Selected?.Entry.OperationId;
            var entries = await service.RefreshAsync(reconcile: true, cancellationToken);
            entriesChanged?.Invoke(entries);
            Items.Clear(); foreach (var entry in entries) Items.Add(new(entry));
            Selected = Items.FirstOrDefault(item => item.Entry.OperationId == selected) ?? Items.FirstOrDefault();
            OnPropertyChanged(nameof(CountText));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { Error = exception.Message; AccessHostHelp.ShowFor(exception); }
        finally { IsRefreshing = false; }
    }

    partial void OnIsWritingChanged(bool value) { OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(CanAct)); }

    public OrderConfirmationViewModel? CreateRestoreConfirmation() => CanAct && Selected is { } item ? Confirmation(
        "Восстановить заявку?", MainWindowViewModel.Describe(item.Entry.Record),
        "Вернутся прежние значения и цена без пересчёта, с тем же кодом. Занятая запись не перезаписывается.\nИсточник: " + SourceDescription,
        "Восстановить", async () =>
        {
            var restored = await service.RestoreAsync(item.Entry.OperationId);
            changed(restored.Id, restored);
        }) : null;

    public OrderConfirmationViewModel? CreatePurgeConfirmation() => CanAct && Selected is { } item ? Confirmation(
        "Удалить копию окончательно?", MainWindowViewModel.Describe(item.Entry.Record),
        "Архивная копия будет удалена с этого компьютера. Восстановить её из этой корзины больше не получится. Это действие не меняет MDB.",
        "Удалить окончательно", () => service.PurgeAsync(item.Entry.OperationId), destructive: true) : null;

    private OrderConfirmationViewModel Confirmation(string title, string details, string explanation, string label, Func<Task> action, bool destructive = false) =>
        new(title, details, explanation, label, async () =>
        {
            if (IsBusy) throw new InvalidOperationException("Дождитесь завершения действия.");
            IsWriting = true;
            try { await runWrite(action); }
            finally { IsWriting = false; }
        }, exception => { Error = exception.Message; failed(exception); }, destructive);
}
