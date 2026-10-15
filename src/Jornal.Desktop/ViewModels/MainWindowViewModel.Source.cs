using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Jornal.Core.Orders;
using Jornal.Desktop.DataSources;

namespace Jornal.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private OrderSource? _source;
    public string SourceDescription => _source?.Description ?? "Демонстрационные данные · без подключения к базе";
    public string SourceDetails => _source?.Details ?? "Демонстрационный источник";
    public string? DatabasePath => _source?.Path;
    private string BatchStorageDescription => _source?.Path is not null ? "Статусы и БЛЦБ сохраняются в MDB" : "Деморежим · до закрытия программы";
    public bool CanConfigureSource => sourceManager is not null && CanUseJournal && !IsCopyingResult;

    public void InitializeSource(OrderSource source)
    {
        _source = source;
        NotifySourceChanged();
    }

    public DatabaseSettingsViewModel? CreateDatabaseSettingsForm() => CanConfigureSource
        ? new(DatabasePath, ConnectSourceAsync) : null;

    private async Task<string?> ConnectSourceAsync(string? path, CancellationToken cancellationToken)
    {
        if (!CanConfigureSource) return "Дождитесь завершения текущего действия.";
        IsLoadingOrders = true;
        try
        {
            var candidate = sourceManager!.Create(path);
            var orders = await candidate.Reader.ReadAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            // До успешной проверки и сохранения пути прежний источник и экран остаются действующими.
            sourceManager.Save(candidate);
            var changed = _source?.Path != candidate.Path || _source?.Error is not null;
            orderReader = candidate.Reader;
            orderCreator = candidate.Creator;
            orderEditor = candidate.Editor;
            _source = candidate;
            _allOrders = orders;
            RequiresReload = false;
            if (changed)
            {
                ResultText = "";
                _resultOrderIds.Clear();
                ResetFilters();
            }
            ApplySearch();
            NotifySourceChanged();
            ErrorMessage = null;
            UpdatedText = $"Обновлено в {DateTime.Now:HH:mm} · F5 — обновить";
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            Trace.TraceError("Order source connection failed: {0}", exception);
            return exception is OrderReadException ? exception.Message :
                "Не удалось подключить базу или сохранить путь. Прежний источник и список сохранены — попробуйте ещё раз.";
        }
        finally { IsLoadingOrders = false; }
    }

    private void NotifySourceChanged()
    {
        foreach (var name in new[] { nameof(SourceDescription), nameof(SourceDetails), nameof(DatabasePath) }) OnPropertyChanged(name);
        NotifyJournalAvailability();
    }
}
