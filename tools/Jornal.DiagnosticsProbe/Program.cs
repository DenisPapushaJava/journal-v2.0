using Jornal.Infrastructure.Diagnostics;

if (args.Length is not (2 or 3) || args.Length == 3 && args[2] != "--notification-test" || !Uri.TryCreate(args[0], UriKind.Absolute, out var endpoint) || endpoint.Scheme != "https")
{
    Console.Error.WriteLine("Использование: Jornal.DiagnosticsProbe <HTTPS-приёмник> <папка тестового журнала> [--notification-test]");
    return 2;
}
using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = Timeout.InfiniteTimeSpan };
Console.WriteLine($"Коммит: {AppDiagnostics.BuildCommit ?? "неизвестен"}. Изменения: {AppDiagnostics.BuildDirty?.ToString() ?? "неизвестно"}.");
using var service = new DiagnosticService(new(args[1]), http, endpoint, AppDiagnostics.AppVersion);
for (var i = 0; i < 2; i++)
{
    try { DiagnosticStages.Run(DiagnosticStage.ReadOrders, () => SyntheticFailure(args.Length == 3)); }
    catch (Exception exception) { Console.WriteLine(service.Capture(args.Length == 3 ? DiagnosticOperation.OrderRead : DiagnosticOperation.Unexpected, exception)); }
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
}
