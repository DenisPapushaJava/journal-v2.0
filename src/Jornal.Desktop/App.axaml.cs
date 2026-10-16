using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Customization;
using Jornal.Desktop.Views;
using Jornal.Desktop.DataSources;
using Jornal.Desktop.Updates;

namespace Jornal.Desktop;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var sources = new OrderSourceManager(new JsonOrderSourceSettingsStore(JsonOrderSourceSettingsStore.DefaultPath));
            var source = sources.Load();
            var updates = new AppUpdatesViewModel(new VelopackAppUpdater()) { CheckAutomatically = true };
            var model = new MainWindowViewModel(source.Reader, source.Creator, source.Editor, sources, updates);
            model.InitializeSource(source);
            var restartStore = new JsonUpdateRestartStore(JsonUpdateRestartStore.DefaultPath);
            model.RestoreUpdateDraft(restartStore);
            desktop.MainWindow = new MainWindow(new JsonUiPreferencesStore(JsonUiPreferencesStore.DefaultPath), restartStore)
            {
                DataContext = model
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
