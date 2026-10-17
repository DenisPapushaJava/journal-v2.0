using System.Globalization;
using Jornal.Core.Orders;
using Jornal.Desktop.Clipboard;
using Jornal.Desktop.Customization;

namespace Jornal.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    public bool CanGetColorCode => CanUseJournal && !IsCopyingResult;
    public RowColorViewModel? CreateRowColorForm(IJournalClipboard clipboard, bool copyOnly = false)
    {
        if (!(copyOnly ? CanGetColorCode : CanUpdateSelectedOrders)) return null;
        var expected = ClipboardSelection();
        var colors = expected.Select(order => order.Color).Distinct().ToArray();
        var initial = colors.Length == 1 && colors[0].HasValue ? colors[0]!.Value : TechnologistColorArgb;
        var form = new RowColorViewModel(initial, copyOnly, expected.Length, FrequentTechnologistColors.Concat(OtherTechnologistColors), async color =>
        {
            if (copyOnly) { await clipboard.WriteTextAsync(color.ToString(CultureInfo.InvariantCulture)); UpdatedText = "Код цвета скопирован"; return true; }
            return await UpdateOrdersAsync(expected.Select(order => order.Id).ToArray(), () => orderEditor!.SetColorAsync(expected, color), $"Цвет изменён · заявок: {expected.Length}");
        }, () => ErrorMessage, () => copyOnly ? !IsUpdatingOrders : CanUpdateSelectedOrders);
        if (copyOnly) form.BusyChanged += (_, _) => IsCopyingSelection = form.IsBusy;
        return form;
    }
    private EntrySuggestions _entrySuggestions = new();
    public string EntrySuggestionsSummary => $"Менеджеры: {_entrySuggestions.Managers.Length} · заказчики: {_entrySuggestions.Customers.Length}";
    public EntrySuggestions CaptureEntrySuggestions() => _entrySuggestions.Normalize();
    public void ApplyEntrySuggestions(EntrySuggestions value)
    {
        _entrySuggestions = value.Normalize();
        OnPropertyChanged(nameof(EntrySuggestionsSummary));
    }
}
