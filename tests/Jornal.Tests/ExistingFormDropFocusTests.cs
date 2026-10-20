using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed partial class SpecificationDropWindowTests
{
    [AvaloniaTheory]
    [InlineData(false, false, false)] [InlineData(false, false, true)]
    [InlineData(false, true, false)] [InlineData(false, true, true)]
    [InlineData(true, false, false)] [InlineData(true, false, true)]
    [InlineData(true, true, false)] [InlineData(true, true, true)]
    public async Task ExistingFormActivatesForAnAcceptedDropButNotForProgrammaticImport(bool edit, bool dark, bool drop)
    {
        using var sample = new SpecificationImportTests.Sample();
        var reader = new SpecificationImportTests.BlockingReader();
        var store = new EditSpecificationImportTests.Store(); var original = store.Current;
        var created = new NewOrderViewModel(new DemoOrderStore()) { ImportReader = reader };
        var edited = EditSpecificationImportTests.Form(store); edited.ImportReader = reader;
        var manager = edit ? edited.Manager : created.Manager; manager.Value = "Иван";
        Window window = edit ? new EditOrderWindow { DataContext = edited } : new NewOrderWindow { DataContext = created };
        window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        window.ShowActivated = false;
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); Assert.False(window.IsActive);
            window.ShowActivated = true;
            Task? import = null;
            if (drop)
            {
                using var file = await Transfer(window, sample.Path);
                Assert.Equal(DragDropEffects.Copy, Drag(window, DragDrop.DragOverEvent, file).DragEffects);
                Assert.False(window.IsActive); // Наведение с файлом ещё не активирует форму.
                Drag(window, DragDrop.DropEvent, file);
            }
            else import = edit ? ((EditOrderWindow)window).ImportSpecificationsAsync([sample.Path])
                : ((NewOrderWindow)window).ImportSpecificationsAsync([sample.Path]);
            await Until(() => reader.Calls == 1);
            Assert.Equal(drop, window.IsActive); // До окончания чтения, а не по его завершении.
            reader.Result.SetResult(await BatchImportTests.Preview());
            if (import is not null) await import;
            await Until(() => edit ? edited.HasPendingImport : created.HasImport);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(drop, window.IsActive);
            Assert.Equal("Иван", manager.Value);
            Assert.Equal(original, store.Current); Assert.Equal(0, store.Updates);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task AnUnsupportedDropDoesNotActivateAnExistingFormOrStartReading(bool edit)
    {
        using var sample = new SpecificationImportTests.Sample();
        var path = Path.ChangeExtension(sample.Path, ".txt"); File.WriteAllText(path, "Другой документ");
        var reader = new SpecificationImportTests.BlockingReader();
        var created = new NewOrderViewModel(new DemoOrderStore()) { ImportReader = reader };
        var edited = EditSpecificationImportTests.Form(new()); edited.ImportReader = reader;
        Window window = edit ? new EditOrderWindow { DataContext = edited } : new NewOrderWindow { DataContext = created };
        window.ShowActivated = false;
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); window.ShowActivated = true;
            using var file = await Transfer(window, path);
            Drag(window, DragDrop.DropEvent, file);
            Assert.False(window.IsActive); Assert.Equal(0, reader.Calls);
            Assert.True(edit ? edited.HasImportError : created.HasImportError);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    }
}
