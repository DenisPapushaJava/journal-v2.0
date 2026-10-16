using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jornal.Parsing.Documents;
using Jornal.Parsing.Specifications;

namespace Jornal.Inspect;

internal static class HansSpecificationCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("Jornal.Inspect hans-spec <файл.pdf> [...]"); return 1; }
        var options = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters = { new JsonStringEnumConverter() } };
        var failed = false;
        foreach (var path in args.Skip(1))
        {
            try
            {
                var report = new HansPdfSpecificationParser().Parse(await new DocumentReader().ReadAsync(path));
                var normalized = report.IsComplete ? HansSpecificationNormalizer.Normalize(report) : null;
                Console.WriteLine(JsonSerializer.Serialize(new { File = Path.GetFileName(path), Report = report, Normalized = normalized }, options));
                failed |= !report.IsComplete;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            { Console.Error.WriteLine($"{Path.GetFileName(path)}: {exception.Message}"); failed = true; }
        }
        return failed ? 1 : 0;
    }
}
