using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Customization;
using Jornal.Desktop.Views;
using Jornal.Desktop.DataSources;
using Jornal.Desktop.Updates;
using Jornal.Desktop.Startup;

namespace Jornal.Desktop;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var startup = new DesktopStartup(CreateMainWindow, window => desktop.MainWindow = window);
            desktop.MainWindow = startup.Splash;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static MainWindow CreateMainWindow()
    {
        var sources = new OrderSourceManager(new JsonOrderSourceSettingsStore(JsonOrderSourceSettingsStore.DefaultPath));
        var source = sources.Load();
        var updates = new AppUpdatesViewModel(new VelopackAppUpdater(),
            preferencesStore: new JsonAppUpdatePreferencesStore(JsonAppUpdatePreferencesStore.DefaultPath));
        var model = new MainWindowViewModel(source.Reader, source.Creator, source.Editor, sources, updates,
            new Jornal.Infrastructure.Trash.JsonTrashStore(Path.Combine(Path.GetDirectoryName(JsonUiPreferencesStore.DefaultPath)!, "Trash")));
        model.InitializeSource(source);
        var restartStore = new JsonUpdateRestartStore(JsonUpdateRestartStore.DefaultPath);
        return new MainWindow(new JsonUiPreferencesStore(JsonUiPreferencesStore.DefaultPath), restartStore)
        {
            DataContext = model
        };
    }
}
