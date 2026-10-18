using System.Runtime.CompilerServices;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Jornal.Desktop.Customization;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class WindowLifetimeTests
{
    [AvaloniaFact]
    public async Task ClosedWindowsAreCollectibleEvenWhenTheirJournalModelsAreRetained()
    {
        var models = new List<MainWindowViewModel>();
        var windows = Enumerable.Range(0, 3).Select(_ => OpenAndClose(models)).ToArray();
        // RunJobs не завершает отдельную очередь композиции. В полном наборе
        // последний кадр ещё может удерживать уже закрытое окно до следующего тика.
        // Даём обеим очередям завершить работу, сохраняя модели живыми весь тест.
        for (var attempt = 0; attempt < 5 && windows.Any(window => window.IsAlive); attempt++)
        {
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.SystemIdle);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        }
        Assert.All(windows, window => Assert.False(window.IsAlive));
        GC.KeepAlive(models);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference OpenAndClose(List<MainWindowViewModel> models)
    {
        var model = new MainWindowViewModel(new DemoOrderStore(), appUpdates: new AppUpdatesViewModel(new AppUpdatesTests.FakeUpdater { IsInstalled = false }));
        models.Add(model);
        var window = new MainWindow(new MemoryUiPreferencesStore()) { DataContext = model };
        window.Show(); Dispatcher.UIThread.RunJobs();
        window.Close(); Dispatcher.UIThread.RunJobs();
        return new(window);
    }
}
