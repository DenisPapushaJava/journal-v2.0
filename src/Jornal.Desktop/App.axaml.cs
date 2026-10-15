using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Customization;
using Jornal.Desktop.Views;
using Jornal.Desktop.DataSources;

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
            var model = new MainWindowViewModel(source.Reader, source.Creator, source.Editor, sources);
            model.InitializeSource(source);
            desktop.MainWindow = new MainWindow(new JsonUiPreferencesStore(JsonUiPreferencesStore.DefaultPath))
            {
                DataContext = model
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
