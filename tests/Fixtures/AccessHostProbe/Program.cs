using System.Diagnostics;
using System.Text;

var scenario = args[0];
if (scenario == "child-wait") { await Task.Delay(Timeout.InfiniteTimeSpan); return 0; }
var payloadPath = args[1];
var markerPath = args[2];
var output = Console.OpenStandardOutput();
if (scenario == "invalid-before-input")
{
    await output.WriteAsync("not-json\n"u8.ToArray());
    await output.FlushAsync();
    File.WriteAllText(markerPath, "started");
    var noise = new byte[4096];
    Array.Fill(noise, (byte)'x');
    while (true) await output.WriteAsync(noise);
}
Console.InputEncoding = new UTF8Encoding(false);
_ = await Console.In.ReadLineAsync();
if (scenario == "stderr")
{
    var error = Console.OpenStandardError();
    var noise = new byte[4096];
    Array.Fill(noise, (byte)'x');
    for (var index = 0; index < 512; index++) await error.WriteAsync(noise);
    await error.FlushAsync();
}
if (scenario != "silent")
{
    await using var payload = File.OpenRead(payloadPath);
    var buffer = new byte[4093];
    int read;
    while ((read = await payload.ReadAsync(buffer)) > 0) await output.WriteAsync(buffer.AsMemory(0, read));
    await output.FlushAsync();
}
if (scenario == "no-eof-with-child")
{
    // Только тест границ остановки: настоящий AccessHost не создаёт дочерних процессов.
    using var child = new Process
    {
        StartInfo = new(Environment.ProcessPath!)
        {
            UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        }
    };
    foreach (var argument in new[] { "exec", "--runtimeconfig", args[4], typeof(Jornal.AccessHostProbe.ProbeMarker).Assembly.Location, "child-wait" })
        child.StartInfo.ArgumentList.Add(argument);
    child.Start();
    File.WriteAllText(markerPath + ".child", child.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
if (scenario is "no-eof" or "silent" or "no-eof-with-child")
{
    File.WriteAllText(markerPath, "waiting");
    await Task.Delay(Timeout.InfiniteTimeSpan);
}
return args.Length > 3 ? int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 0;

namespace Jornal.AccessHostProbe { public sealed class ProbeMarker; }
