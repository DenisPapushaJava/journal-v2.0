using CommunityToolkit.Mvvm.ComponentModel;
using Jornal.Core.Orders;
using Jornal.Core.Results;
using Jornal.Desktop.Clipboard;
using Jornal.Infrastructure.Diagnostics;

namespace Jornal.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty] private bool _isCopyingSelection;
    public bool CanCopyRowComment => CanUseJournal && !RequiresReload && !IsCopyingResult && _selectedCount == 1;
    public bool CanCopyTelegramComment => CanUseJournal && !RequiresReload && !IsCopyingResult && _selectedCount > 0;
    public bool CanPrepareFolders => CanCopyTelegramComment;
    public string RowCommentHint => _selectedCount == 1 ? "Скопировать комментарий строки" : "Выберите одну заявку";
    private OrderSummary[] ClipboardSelection() => Orders.Where(row => _selectedOrderIds.Contains(row.Id)).Select(row => row.Source).ToArray();
    private void NotifyClipboardAvailability()
    {
        foreach (var name in new[] { nameof(CanCopyRowComment), nameof(CanCopyTelegramComment), nameof(CanPrepareFolders), nameof(RowCommentHint), nameof(CanGetColorCode) }) OnPropertyChanged(name);
    }
    partial void OnIsCopyingSelectionChanged(bool value) { NotifyJournalAvailability(); NotifyClipboardAvailability(); }
    partial void OnIsCopyingResultChanged(bool value) => NotifyClipboardAvailability();
    public async Task CopyCommentAsync(IJournalClipboard clipboard, bool telegram = false)
    {
        if (!(telegram ? CanCopyTelegramComment : CanCopyRowComment)) return;
        string text;
        try { var rows = ClipboardSelection(); text = telegram ? OrderCommentFormatter.Telegram(rows) : OrderCommentFormatter.Row(rows.Single()); }
        catch (ArgumentException exception) { ErrorMessage = exception.Message; return; }
        ErrorMessage = null; IsCopyingSelection = true; AppDiagnostics.Current?.Mark(DiagnosticOperation.Clipboard);
        try { await clipboard.WriteTextAsync(text); UpdatedText = telegram ? "Комментарий для Telegram скопирован" : "Комментарий скопирован"; }
        catch (Exception exception) { ErrorMessage = AppDiagnostics.Describe(DiagnosticOperation.Clipboard, exception, "Не удалось скопировать комментарий."); }
        finally { IsCopyingSelection = false; }
    }
    public FolderClipboardViewModel? CreateFolderForm(IJournalClipboard clipboard)
    {
        if (!CanPrepareFolders) return null;
        try
        {
            var form = new FolderClipboardViewModel(OrderFolderPlan.Create(ClipboardSelection(), clipboard.MaximumFolderNameLength), clipboard);
            form.BusyChanged += (_, _) => IsCopyingSelection = form.IsBusy;
            form.Copied += (_, _) => UpdatedText = $"Папки готовы к вставке: {form.Items.Count}";
            ErrorMessage = null; return form;
        }
        catch (ArgumentException exception) { ErrorMessage = exception.Message; return null; }
    }
}
