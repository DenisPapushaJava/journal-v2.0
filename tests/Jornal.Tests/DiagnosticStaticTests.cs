using Jornal.Infrastructure.Diagnostics;
using Xunit;

namespace Jornal.Tests;

[CollectionDefinition("Diagnostic runtime", DisableParallelization = true)]
public sealed class DiagnosticRuntimeCollection;

[Collection("Diagnostic runtime")]
public sealed class DiagnosticStaticTests
{
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
