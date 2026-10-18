using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Jornal.Desktop.Customization;
using Jornal.Desktop.Tables;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class RowHighlightSettingsTests
{
    [Theory]
    [InlineData(-100, 0)]
    [InlineData(1000, 100)]
    [InlineData(24, 25)]
    [InlineData(double.NaN, 50)]
    [InlineData(double.PositiveInfinity, 50)]
    public void StrengthCannotLeaveSafeBoundsAndSnapsToUsableSteps(double input, double expected) =>
        Assert.Equal(expected, RowHighlightSettings.Normalize(input));

    [AvaloniaTheory]
    [InlineData(false, null, 50)]
    [InlineData(true, null, 100)]
    [InlineData(true, 25d, 25)]
    public void LegacyModeStillMigratesAndExplicitSavedStrengthWins(bool enhanced, double? strength, double expected)
    {
        var store = new MemoryUiPreferencesStore();
        store.Save(new() { EnhancedRowContrast = enhanced, RowHighlightStrength = strength });
        var model = new MainWindowViewModel(new DemoOrderStore());
        var window = new MainWindow(store) { DataContext = model };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(expected, model.RowHighlightStrength);
            model.RowHighlightStrength = double.NaN; Assert.Equal(50, model.RowHighlightStrength);
            model.RowHighlightStrength = 1000; Assert.Equal(100, model.RowHighlightStrength);
        }
        finally { window.Close(); }
        Assert.Equal(100, store.Value!.RowHighlightStrength); Assert.False(store.Value.EnhancedRowContrast);
    }
}
