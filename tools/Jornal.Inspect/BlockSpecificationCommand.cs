using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jornal.Parsing.Documents;
using Jornal.Parsing.Specifications;

namespace Jornal.Inspect;

internal static class BlockSpecificationCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Jornal.Inspect block-spec <файл.html|файл.htm> [...]");
            return 1;
        }
        var options = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters = { new JsonStringEnumConverter() } };
        var failed = false;
        foreach (var path in args.Skip(1))
        {
            try
            {
                var document = await new DocumentReader().ReadAsync(path);
                var report = new BlockHtmlSpecificationParser().Parse(document);
                var normalized = report.IsComplete ? BlockSpecificationNormalizer.Normalize(report) : null;
                Console.WriteLine(JsonSerializer.Serialize(new { File = Path.GetFileName(path), Report = report, Normalized = normalized }, options));
                failed |= !report.IsComplete;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Console.Error.WriteLine($"{Path.GetFileName(path)}: {exception.Message}"); failed = true;
            }
        }
        return failed ? 1 : 0;
    }
}
