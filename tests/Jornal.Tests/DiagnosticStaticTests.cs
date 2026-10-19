using Jornal.Infrastructure.Diagnostics;
using Jornal.Core.Orders;
using Jornal.Infrastructure.Local;
using Xunit;

namespace Jornal.Tests;

[CollectionDefinition("Diagnostic runtime", DisableParallelization = true)]
public sealed class DiagnosticRuntimeCollection;

[Collection("Diagnostic runtime")]
public sealed class DiagnosticStaticTests
{
    [Fact]
    public async Task BusyMessageOmitsTechnicalCodeButKeepsTheReportAndNeverMasksAnUnknownWrite()
    {
        var root = Path.Combine(Path.GetTempPath(), "journal-diagnostic-busy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previous = AppDiagnostics.Current;
        using var http = new HttpClient();
        using var service = new DiagnosticService(new(root), http, null, "2026.1.0");
        try
        {
            AppDiagnostics.Current = service;
            var busy = new OrderReadException("База занята", new LocalJournalBusyException(new IOException("SECRET")));
            var message = AppDiagnostics.Describe(DiagnosticOperation.OrderEdit, busy, "Не удалось сохранить");
            Assert.Equal("База данных сейчас занята. Правки остались в форме. Повторите сохранение через несколько секунд.", message);
            var code = AppDiagnostics.Capture(DiagnosticOperation.OrderEdit, busy);
            Assert.NotNull(code); Assert.DoesNotContain(code, message);

            var unknown = new OrderWriteException(OrderWriteFailure.OutcomeUnknown, "Результат записи не подтверждён. Обновите список (F5).", busy);
            var uncertain = AppDiagnostics.Describe(DiagnosticOperation.OrderEdit, unknown, "Ошибка");
            Assert.StartsWith(unknown.Message, uncertain);
            Assert.Contains("Код: JR-", uncertain);
            Assert.DoesNotContain("Повторите сохранение", uncertain);
            Assert.True(unknown.RequiresReload);
            var unexpected = AppDiagnostics.Describe(DiagnosticOperation.OrderEdit, new IOException("SECRET"), "Не удалось сохранить");
            Assert.StartsWith("Не удалось сохранить", unexpected);
            Assert.Contains("Код: JR-", unexpected);
            await service.FlushLocalAsync(TestContext.Current.CancellationToken);
            Assert.Equal(3, Directory.GetFiles(Path.Combine(root, "events"), "*.json").Length);
        }
        finally { AppDiagnostics.Current = previous; service.Dispose(); Directory.Delete(root, true); }
    }

    [Fact]
    public async Task FatalValidationExceptionIsStillReportedAndOrdinaryPropagationIsDeduplicated()
    {
        var root = Path.Combine(Path.GetTempPath(), "journal-diagnostic-static-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previous = AppDiagnostics.Current;
        using var http = new HttpClient();
        using var service = new DiagnosticService(new(root), http, null, "2026.1.0");
        try
        {
            AppDiagnostics.Current = service;
            var validation = new ArgumentException("SECRET");
            Assert.Null(AppDiagnostics.Capture(DiagnosticOperation.OrderCreate, validation));
            Assert.NotNull(AppDiagnostics.Capture(DiagnosticOperation.Unexpected, validation, fatal: true));
            Assert.True(service.Store.HasFatal);
            var error = new IOException("SECRET");
            var code = AppDiagnostics.Capture(DiagnosticOperation.OrderRead, error);
            Assert.NotNull(code);
            Assert.Equal(code, AppDiagnostics.Capture(DiagnosticOperation.OrderRead, error));
            await service.FlushLocalAsync(TestContext.Current.CancellationToken);
            Assert.Equal(2, Directory.GetFiles(Path.Combine(root, "events"), "*.json").Length);
        }
        finally { AppDiagnostics.Current = previous; service.Dispose(); Directory.Delete(root, true); }
    }
}
