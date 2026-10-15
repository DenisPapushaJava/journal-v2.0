using Jornal.Desktop.Updates;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class UpdateRestartTests
{
    [Fact]
    public async Task DraftRoundTripPreservesUnicodeAndManualEditsAndIsConsumedOnce()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var store = new JsonUpdateRestartStore(path);
            var model = new MainWindowViewModel(new DemoOrderStore());
            await model.ReloadCommand.ExecuteAsync(null); model.AddOrdersToResult([model.Orders[0]]);
            model.ResultText = "Примечание <заказчику>\n" + model.ResultText;
            var draft = model.CaptureUpdateDraft(); store.Save(draft);
            Assert.Equal(draft.ResultText, store.Take()!.ResultText); Assert.Null(store.Take());
            store.Save(draft);
            var restored = new MainWindowViewModel(new DemoOrderStore());
            restored.RestoreUpdateDraft(store);
            await restored.ReloadCommand.ExecuteAsync(null);
            Assert.Equal(model.ResultText, restored.ResultText); Assert.False(File.Exists(path));
            Assert.Empty(draft.ResultOrderIds); // Демозаявки не переживают закрытие приложения.
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void DatabaseInsideReplacedDirectoryIsRejectedWithoutMovingIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "Journal");
        Assert.Throws<InvalidOperationException>(() => AppUpdateSafety.EnsureExternalDatabase(Path.Combine(root, "current", "OrderDB.mdb"), Path.Combine(root, "current")));
        AppUpdateSafety.EnsureExternalDatabase(Path.Combine(root, "shared", "OrderDB.mdb"), Path.Combine(root, "current"));
        AppUpdateSafety.EnsureExternalDatabase(Path.Combine(root, "current-copy", "OrderDB.mdb"), Path.Combine(root, "current"));
        AppUpdateSafety.EnsureExternalDatabase(null, Path.Combine(root, "current"));
    }

    [Fact]
    public void BrokenDraftIsKeptAndReportedWithoutReplacingExistingResult()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, "broken JSON");
            var model = new MainWindowViewModel(new DemoOrderStore()) { ResultText = "Есть текст" };
            model.RestoreUpdateDraft(new JsonUpdateRestartStore(path));
            Assert.Equal("Есть текст", model.ResultText); Assert.True(model.HasResultError); Assert.True(File.Exists(path));
        }
        finally { File.Delete(path); }
    }
}
