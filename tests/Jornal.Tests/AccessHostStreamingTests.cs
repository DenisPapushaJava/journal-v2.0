using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Jornal.AccessHostProbe;
using Jornal.Core.Orders;
using Jornal.Infrastructure.Access;
using Jornal.Infrastructure.Diagnostics;
using Xunit;

namespace Jornal.Tests;

public sealed class AccessHostStreamingTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(4093)]
    public async Task Utf8MaySplitInsideCharactersNumbersAndEscapes(int chunkSize)
    {
        var row = Row(42);
        row = row with { CuttingTime = OrderCuttingTime.For(row, 1786), Annotations = OrderAnnotations.For(row, true, "Гнуть №2\nВторая строка") };
        var json = JsonSerializer.SerializeToUtf8Bytes(new AccessHostResponse(1, [row], AnnotationsSupported: true, OperationsSupported: true),
            new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        using var stream = new ChunkedStream(new MemoryStream(json), chunkSize);
        var result = await AccessHostClient.ReadResponseAsync(stream, TestContext.Current.CancellationToken);
        Assert.Equal(1, result!.Protocol);
        Assert.True(result.AnnotationsSupported);
        Assert.Equal(row, Assert.Single(result.Orders!));
    }

    [Fact]
    public async Task Utf8BomWhitespaceAndUnknownOptionalFieldsRemainCompatible()
    {
        var row = Row(42);
        var json = " \r\n{\"Unknown\":{\"value\":[1,2]},\"Orders\":[" + JsonSerializer.Serialize(row) +
            "],\"AnnotationsSupported\":true,\"OperationsSupported\":true,\"Protocol\":1}\r\n ";
        var bytes = Encoding.UTF8.Preamble.ToArray().Concat(Encoding.UTF8.GetBytes(json)).ToArray();
        using var stream = new ChunkedStream(new MemoryStream(bytes), 1);
        var result = await AccessHostClient.ReadResponseAsync(stream, TestContext.Current.CancellationToken);
        Assert.Equal(row, Assert.Single(result!.Orders!));
    }

    [Fact]
    public async Task InvalidUtf8IsRejectedRatherThanSilentlyReplacingData()
    {
        var bytes = "{\"Protocol\":1,\"Error\":\""u8.ToArray().Concat(new byte[] { 0xC3, 0x28 }).Concat("\"}"u8.ToArray()).ToArray();
        using var stream = new ChunkedStream(new MemoryStream(bytes), 1);
        var error = await Assert.ThrowsAsync<JsonException>(() => AccessHostClient.ReadResponseAsync(stream, TestContext.Current.CancellationToken));
        Assert.Equal(DiagnosticStage.ValidateAccessResponse, DiagnosticStages.Of(error));
    }

    [Fact]
    public async Task LargeReplyAndFullStderrAreReadWithoutPipeDeadlock()
    {
        var rows = Enumerable.Range(1, 4096).Select(Row).ToArray();
        using var host = new Host(JsonSerializer.Serialize(new AccessHostResponse(1, rows, AnnotationsSupported: true, OperationsSupported: true)), "stderr");
        var result = await host.Client.ReadAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        Assert.Equal(rows, result);
        Assert.Equal(1, host.Starts);
        host.AssertStopped();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \r\n")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{\"Protocol\":1,\"Orders\":[")]
    [InlineData("{\"Protocol\":2,\"Orders\":[]}")]
    [InlineData("{\"Protocol\":1,\"Orders\":[null]}")]
    [InlineData("{\"Protocol\":1,\"Orders\":[]}trailing")]
    [InlineData("{\"Protocol\":1,\"Orders\":[]}{\"Protocol\":1,\"Orders\":[]}")]
    public async Task InvalidOrIncompleteReplyNeverReturnsAPartialList(string payload)
    {
        using var host = new Host(payload);
        await Assert.ThrowsAsync<OrderReadException>(() => host.Client.ReadAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Equal(1, host.Starts);
        host.AssertStopped();
    }

    [Fact]
    public async Task JsonFailureStopsAHostThatNeverReadsALargeRequest()
    {
        using var host = new Host("", "invalid-before-input");
        var rows = Enumerable.Range(1, 4096).Select(Row).ToArray();
        var error = await Assert.ThrowsAsync<OrderWriteException>(() => host.Client.SetColorAsync(rows, OrderColors.Work,
            TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.OutcomeUnknown, error.Failure);
        Assert.True(error.RequiresReload);
        Assert.IsType<JsonException>(error.InnerException);
        Assert.Equal(DiagnosticStage.ValidateAccessResponse, DiagnosticStages.Of(error));
        Assert.Equal(1, host.Starts);
        host.AssertStopped();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDiscardsEvenACompleteJsonUntilEofAndExit(bool completeJson)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var json = JsonSerializer.Serialize(new AccessHostResponse(1, [Row(42)], AnnotationsSupported: true, OperationsSupported: true));
        using var host = new Host(completeJson ? json : json[..^5], "no-eof");
        var operation = host.Client.ReadAsync(cancellation.Token);
        await host.WaitForMarkerAsync();
        Assert.False(operation.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Equal(1, host.Starts);
        host.AssertStopped();
    }

    [Fact]
    public async Task CancellationAfterDispatchKeepsTheWriteUnconfirmedAndDoesNotRetry()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var host = new Host(JsonSerializer.Serialize(new AccessHostResponse(1, [Row(42) with { Color = OrderColors.Work }], AnnotationsSupported: true, OperationsSupported: true)), "no-eof");
        var operation = host.Client.SetColorAsync([Row(42)], OrderColors.Work, cancellation.Token);
        await host.WaitForMarkerAsync();
        Assert.False(operation.IsCompleted);
        cancellation.Cancel();
        var error = await Assert.ThrowsAsync<OrderWriteException>(() => operation.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.OutcomeUnknown, error.Failure);
        Assert.True(error.RequiresReload);
        Assert.Equal(1, host.Starts);
        host.AssertStopped();
    }

    [Fact]
    public async Task DeadlineIsAReadFailureAndStopsTheHostWithoutCancellingTheCaller()
    {
        var token = TestContext.Current.CancellationToken;
        using var host = new Host("", "silent", timeout: TimeSpan.FromSeconds(2));
        var error = await Assert.ThrowsAsync<OrderReadException>(() => host.Client.ReadAsync(token).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.IsAssignableFrom<OperationCanceledException>(error.InnerException);
        Assert.False(token.IsCancellationRequested);
        Assert.Equal(1, host.Starts);
        host.AssertStopped();
    }

    [Fact]
    public async Task ValidJsonWithAFailedExitDoesNotConfirmAWrite()
    {
        using var host = new Host(JsonSerializer.Serialize(new AccessHostResponse(1, [Row(42) with { Color = OrderColors.Work }], AnnotationsSupported: true, OperationsSupported: true)), exitCode: 7);
        var error = await Assert.ThrowsAsync<OrderWriteException>(() => host.Client.SetColorAsync([Row(42)], OrderColors.Work,
            TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.OutcomeUnknown, error.Failure);
        Assert.True(error.RequiresReload);
        Assert.Equal(1, host.Starts);
        host.AssertStopped();
    }

    [Fact]
    public async Task CancellationStopsOnlyTheStartedProcessAndClosesItsPipesBeforeReturning()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var host = new Host(JsonSerializer.Serialize(new AccessHostResponse(1, [], AnnotationsSupported: true, OperationsSupported: true)), "no-eof-with-child");
        var operation = host.Client.ReadAsync(cancellation.Token);
        await host.WaitForMarkerAsync();
        using var child = Process.GetProcessById(host.ChildProcessId);
        Assert.False(child.HasExited);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        host.AssertStopped();
        Assert.False(child.HasExited); // Не трогаем другие процессы, даже у подменённого тестового хоста.
        Assert.Equal(1, host.Starts);
    }

    [Fact]
    public async Task CleanupWaitsForActualExitEvenWhenTheRequestTokenIsAlreadyCancelled()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var stopRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = new Host("", "silent", stop: _ => stopRequested.TrySetResult());
        var operation = host.Client.ReadAsync(cancellation.Token);
        await host.WaitForMarkerAsync();
        cancellation.Cancel();
        await stopRequested.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        // Имитируем промежуток между запросом остановки и настоящим выходом процесса.
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.False(operation.IsCompleted);
        host.Stop();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        host.AssertStopped();
    }

    [Fact]
    public async Task ExitBetweenTheCheckAndStopDoesNotReplaceTheOriginalJsonFailure()
    {
        var stops = 0;
        using var host = new Host("", "invalid-before-input", stop: process =>
        {
            stops++;
            process.Kill();
            if (!process.WaitForExit(5000)) throw new TimeoutException("Probe did not exit.");
            throw new InvalidOperationException("Process exited before the stop request.");
        });
        var error = await Assert.ThrowsAsync<OrderWriteException>(() => host.Client.SetColorAsync([Row(42)], OrderColors.Work,
            TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.IsType<JsonException>(error.InnerException);
        Assert.Equal(DiagnosticStage.ValidateAccessResponse, DiagnosticStages.Of(error));
        Assert.Equal(OrderWriteFailure.OutcomeUnknown, error.Failure);
        Assert.True(error.RequiresReload);
        Assert.Equal(1, stops); Assert.Equal(1, host.Starts);
        host.AssertStopped();
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task RefusedOrIneffectiveStopIsBoundedAndKeepsTheWriteUnconfirmed(bool refused)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var stops = 0;
        using var host = new Host("", "silent", stop: _ =>
        {
            stops++;
            if (refused) throw new System.ComponentModel.Win32Exception(5);
        }, cleanupTimeout: TimeSpan.FromSeconds(1));
        var operation = host.Client.SetColorAsync([Row(42)], OrderColors.Work, cancellation.Token);
        await host.WaitForMarkerAsync();
        cancellation.Cancel();
        var error = await Assert.ThrowsAsync<OrderWriteException>(() => operation.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.IsAssignableFrom<OperationCanceledException>(error.InnerException);
        Assert.Equal(OrderWriteFailure.OutcomeUnknown, error.Failure); Assert.True(error.RequiresReload);
        Assert.Equal(1, host.Starts); Assert.Equal(1, stops);
        using var stillRunning = Process.GetProcessById(host.ProcessId);
        Assert.False(stillRunning.HasExited);
        host.AssertPipesClosed();
    }

    [Fact]
    public async Task CancellationBeforeStartDoesNotLaunchOrStopAnything()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var stops = 0;
        using var host = new Host("", stop: _ => stops++);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.Client.ReadAsync(cancellation.Token));
        Assert.Equal(0, host.Starts); Assert.Equal(0, stops);
    }

    private static OrderSummary Row(int id) => new(id, new(2026, 10, 19), $"1019--{id}", "Менеджер", "Заказчик 'А' №2 🔧", "Лазер", "5083H111", 2.5m,
        922337203685477.5807m, -25089)
    {
        SheetSize = "1250x2500, 1000x2000", SheetCount = "02 / комплект", CutLengthMeters = 123, WeightKilograms = "456,7",
        DeliveryNumber = null, StoredCoefficient = " 1,25 | 20 | 340 | 2 | N2 ", Coefficient = 1.25m,
        StoredOrderDateTime = new(2026, 10, 19, 8, 59, 42), PartCount = 20, PunchCount = 340, CuttingGas = "N2", MaterialId = "2"
    };

    private sealed class Host : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "journal-stream-host-" + Guid.NewGuid().ToString("N"));
        private readonly string _marker;
        private int? _pid;
        private SafeHandle[] _pipeHandles = [];
        public AccessHostClient Client { get; }
        public int Starts { get; private set; }
        public int ProcessId => _pid ?? throw new InvalidOperationException("Probe was not started.");
        public int ChildProcessId => int.Parse(File.ReadAllText(_marker + ".child"), System.Globalization.CultureInfo.InvariantCulture);

        public Host(string payload, string scenario = "response", int exitCode = 0, TimeSpan? timeout = null,
            Action<Process>? stop = null, TimeSpan? cleanupTimeout = null)
        {
            Directory.CreateDirectory(_root);
            var file = Path.Combine(_root, "reply.json");
            _marker = Path.Combine(_root, "ready");
            File.WriteAllText(file, payload, new UTF8Encoding(false));
            var dotnet = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "../../..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
            Client = new AccessHostClient("unused.mdb", dotnet, process =>
            {
                foreach (var argument in new[] { "exec", "--runtimeconfig", Path.ChangeExtension(typeof(AccessHostStreamingTests).Assembly.Location, ".runtimeconfig.json"),
                    typeof(ProbeMarker).Assembly.Location, scenario, file, _marker, exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Path.ChangeExtension(typeof(AccessHostStreamingTests).Assembly.Location, ".runtimeconfig.json") })
                    process.StartInfo.ArgumentList.Add(argument);
                process.Start();
                _pid = process.Id;
                _pipeHandles = new[] { process.StandardInput.BaseStream, process.StandardOutput.BaseStream, process.StandardError.BaseStream }
                    .Select(pipe => (SafeHandle)(pipe switch
                    {
                        System.IO.Pipes.PipeStream stream => stream.SafePipeHandle,
                        FileStream stream => stream.SafeFileHandle,
                        _ => throw new InvalidOperationException("Unexpected process pipe.")
                    })).ToArray();
                Starts++;
            }, timeout, stop, cleanupTimeout);
        }

        public async Task WaitForMarkerAsync()
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            while (!File.Exists(_marker)) await Task.Delay(10, deadline.Token);
        }

        public void AssertStopped()
        {
            if (_pid is not { } pid) throw new InvalidOperationException("Probe was not started.");
            try
            {
                using var process = Process.GetProcessById(pid);
                Assert.True(process.HasExited);
            }
            catch (ArgumentException) { /* Процесс уже завершён и удалён из таблицы процессов. */ }
            AssertPipesClosed();
        }

        public void AssertPipesClosed() => Assert.All(_pipeHandles, handle => Assert.True(handle.IsClosed));
        public void Stop() { if (_pid is { } pid) Stop(pid); }
        private static void Stop(int pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                if (!process.HasExited) process.Kill();
                process.WaitForExit(10000);
            }
            catch (ArgumentException) { }
        }

        public void Dispose()
        {
            Stop();
            if (File.Exists(_marker + ".child")) Stop(ChildProcessId);
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class ChunkedStream(Stream source, int chunkSize) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => source.Read(buffer, offset, Math.Min(count, chunkSize));
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            source.ReadAsync(buffer[..Math.Min(buffer.Length, chunkSize)], cancellationToken);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) source.Dispose(); base.Dispose(disposing); }
    }
}
