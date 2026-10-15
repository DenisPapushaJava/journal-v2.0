using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jornal.Core.Results;
using Jornal.Desktop.Clipboard;

namespace Jornal.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private readonly HashSet<int> _resultOrderIds = [];
    private int _selectedCount;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanClearResult)), NotifyPropertyChangedFor(nameof(CanCopyResult))]
    private string _resultText = string.Empty;
    [ObservableProperty] private string _resultMessage = "Для письма · текст можно поправить";
    [ObservableProperty] private bool _hasResultError;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanAddResult)), NotifyPropertyChangedFor(nameof(CanClearResult)),
     NotifyPropertyChangedFor(nameof(CanCopyResult)), NotifyPropertyChangedFor(nameof(CanEditResult))]
    private bool _isCopyingResult;
    public bool CanAddResult => CanEditResult && _selectedCount > 0;
    public bool CanClearResult => CanEditResult && ResultText.Length > 0;
    public bool CanCopyResult => CanEditResult && !string.IsNullOrWhiteSpace(ResultText);
    public bool CanEditResult => CanUseJournal && !IsCopyingResult;

    partial void OnResultTextChanged(string value)
    {
        if (value.Length == 0) _resultOrderIds.Clear();
        HasResultError = false;
        ResultMessage = "Для письма · текст можно поправить";
    }

    public void AddOrdersToResult(IEnumerable<OrderRowViewModel> selected)
    {
        if (!CanEditResult) return;
        var visible = Orders.ToDictionary(row => row.Id, row => row.Source);
        var orders = selected.Select(row => row.Id).Distinct()
            .Where(id => visible.ContainsKey(id) && !_resultOrderIds.Contains(id)).Select(id => visible[id]).ToArray();
        var unpriced = orders.Where(order => order.StoredCuttingPrice is null).ToArray();
        if (unpriced.Length > 0)
        {
            HasResultError = true;
            var numbers = string.Join(", ", unpriced.Take(3).Select(order => order.SpecificationNumber));
            if (unpriced.Length > 3) numbers += $" и ещё {unpriced.Length - 3}";
            ResultMessage = $"Не рассчитана цена: {numbers}. Выберите заявки с рассчитанной ценой.";
            return;
        }
        if (orders.Length == 0)
        {
            HasResultError = false;
            ResultMessage = "Выбранные заявки уже добавлены в результат";
            return;
        }
        var addition = OrderResultFormatter.Format(orders);
        var separator = ResultText.Length > 0 && !ResultText.EndsWith('\n') && !ResultText.EndsWith('\r') ? "\n" : "";
        ResultText += separator + addition;
        foreach (var order in orders) _resultOrderIds.Add(order.Id);
        ResultMessage = $"Добавлено заявок: {orders.Length} · можно копировать в письмо";
    }

    [RelayCommand]
    private void ClearResult()
    {
        if (!CanEditResult) return;
        ResultText = string.Empty;
        _resultOrderIds.Clear();
        HasResultError = false;
        ResultMessage = "Результат очищен · выбор заявок сохранён";
    }

    public async Task CopyResultAsync(IResultClipboard clipboard)
    {
        if (!CanCopyResult) return;
        var text = ResultText;
        IsCopyingResult = true;
        HasResultError = false;
        ResultMessage = "Копирование…";
        try
        {
            await clipboard.WriteAsync(ResultClipboardContent.FromText(text));
            ResultMessage = "Скопировано · вставьте в письмо через Ctrl+V";
        }
        catch (Exception exception)
        {
            Trace.TraceError("Result clipboard write failed: {0}", exception);
            HasResultError = true;
            ResultMessage = "Не удалось скопировать. Результат сохранён — попробуйте ещё раз.";
        }
        finally { IsCopyingResult = false; }
    }
}
