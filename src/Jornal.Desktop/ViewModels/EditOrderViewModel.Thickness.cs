using Jornal.Core.Pricing;

namespace Jornal.Desktop.ViewModels;

public partial class EditOrderViewModel
{
    private ThicknessPriceSource _thicknessSource = null!;
    private bool _thicknessStopped;
    public ThicknessOptionsViewModel ThicknessOptions { get; private set; } = null!;
    public Func<PriceEditorViewModel?>? PriceEditorFactory { get; set; }
    public bool IsEditingPrices { get; private set; }
    private void InitializeThicknessOptions()
    {
        _thicknessSource = new(() => _store);
        ThicknessOptions = new(Thickness, _thicknessSource,
            () => new(CuttingType.Value, IsHansLaser ? MaterialId.Value.Trim() : "", SelectedMachine,
                IsHansLaser && ImportPreview?.Report.Profile == Jornal.Parsing.Specifications.HansPdfSpecificationParser.Profile), AccessHostHelp.ShowFor);
        ThicknessOptions.ReloadAll = () => RefreshThicknessOptionsAsync(true);
        ThicknessOptions.Changed += (_, _) => NotifyBusy();
    }
    public async Task RefreshThicknessOptionsAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        if (force) _thicknessSource.Invalidate();
        await ThicknessOptions.RefreshAsync(cancellationToken);
    }
    public PriceEditorViewModel? CreateThicknessPriceEditor(OrderEntryField field)
    {
        if (IsBusy || RequiresReload || field.ThicknessOptions is not { CanOpenPrices: true, TableName: { } table }) return null;
        var form = PriceEditorFactory?.Invoke() ?? new PriceEditorViewModel(_store, _store as IPriceEditor, SourceName, SourceDescription);
        form.InitialTableName = table; return form;
    }
    public void SetPriceEditorOpen(bool open) { IsEditingPrices = open; NotifyBusy(); }
    public async Task RefreshAfterPriceEditingAsync(CancellationToken cancellationToken = default)
    { await LoadMachinesAsync(cancellationToken); await RefreshThicknessOptionsAsync(true, cancellationToken); }
    public void StopThicknessOptions() { if (_thicknessStopped) return; _thicknessStopped = true; ThicknessOptions.Stop(); _thicknessSource.Stop(); }
    public void StartThicknessOptions()
    {
        if (!_thicknessStopped) return;
        _thicknessStopped = false; _thicknessSource = new(() => _store); ThicknessOptions.Restart(_thicknessSource);
    }
}
