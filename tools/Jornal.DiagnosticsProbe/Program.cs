using Jornal.Infrastructure.Diagnostics;

if (args.Length is not (2 or 3) || args.Length == 3 && args[2] is not ("--notification-test" or "--read-context-test") || !Uri.TryCreate(args[0], UriKind.Absolute, out var endpoint) || endpoint.Scheme != "https")
{
    Console.Error.WriteLine("Использование: Jornal.DiagnosticsProbe <HTTPS-приёмник> <папка тестового журнала> [--notification-test|--read-context-test]");
    return 2;
}
using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = Timeout.InfiniteTimeSpan };
Console.WriteLine($"Коммит: {AppDiagnostics.BuildCommit ?? "неизвестен"}. Изменения: {AppDiagnostics.BuildDirty?.ToString() ?? "неизвестно"}.");
using var service = new DiagnosticService(new(args[1]), http, endpoint, AppDiagnostics.AppVersion);
var readContext = args.Length == 3 && args[2] == "--read-context-test";
for (var i = 0; i < 2; i++)
{
    try { DiagnosticStages.Run(readContext ? DiagnosticStage.OpenDatabase : DiagnosticStage.ReadOrders, () =>
        { if (readContext) throw new Jornal.DiagnosticsProbe.ReadContextProbeException(); SyntheticFailure(args.Length == 3); }); }
    catch (Exception exception)
    {
        if (!readContext) Console.WriteLine(service.Capture(args.Length == 3 ? DiagnosticOperation.OrderRead : DiagnosticOperation.Unexpected, exception));
        else
        {
            // Типизированный искусственный пакет для проверки .NET → Worker → Issue, без обращения к MDB.
            var at = DateTimeOffset.UtcNow;
            var report = DiagnosticReport.Create(exception, DiagnosticOperation.PriceRead, service.SessionId, AppDiagnostics.AppVersion,
                recent: [new(DiagnosticOperation.OrderRead, at.AddMilliseconds(-200), DiagnosticReadAction.ReadDay, DiagnosticReadOutcome.Succeeded, 12),
                    new(DiagnosticOperation.PriceRead, at.AddMilliseconds(-37), DiagnosticReadAction.ReadMachineCatalog, DiagnosticReadOutcome.Failed, 37)])
                with { ReadContext = new(DiagnosticReadAction.ReadMachineCatalog, DiagnosticPathKind.UncPath, 37, at.AddMilliseconds(-188)) };
            service.Record(report); Console.WriteLine(report.Code);
        }
    }
}
await service.FlushLocalAsync();
await service.SendDueAsync();
Console.WriteLine($"В очереди: {service.PendingCount}. Подтверждённая доставка: {service.LastAcceptedUtc is not null}.");
return service.PendingCount == 0 && service.LastAcceptedUtc is not null ? 0 : 1;

static void SyntheticFailure(bool notification)
{
    if (notification) throw new Jornal.DiagnosticsProbe.NotificationProbeException();
    throw new Jornal.DiagnosticsProbe.ProbeException();
}

namespace Jornal.DiagnosticsProbe
{
    internal sealed class NotificationProbeException() : Exception("СИНТЕТИЧЕСКАЯ-ПРОВЕРКА-ПИСЬМА: этот текст не должен попасть в отчёт.");
    internal sealed class ProbeException() : Exception("СИНТЕТИЧЕСКАЯ-ПРОВЕРКА: этот текст не должен попасть в отчёт.");
    internal sealed class ReadContextProbeException() : Exception("SECRET-CUSTOMER", new FileNotFoundException("SECRET-PATH", @"\\SECRET-SERVER\SECRET-SHARE\SECRET.mdb"));
}
