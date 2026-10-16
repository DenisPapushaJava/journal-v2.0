using System.Text.Encodings.Web;
using System.Text.Json;
using Jornal.Parsing.Documents;
using Jornal.Inspect;

if (args.Length > 0 && args[0] is "prices" or "price") return await PriceCommands.RunAsync(args);
if (args.Length > 0 && args[0] == "local-copy") return await LocalCopyCommand.RunAsync(args);
if (args.Length > 0 && args[0] == "block-spec") return await BlockSpecificationCommand.RunAsync(args);
if (args.Length > 0 && args[0] == "hans-spec") return await HansSpecificationCommand.RunAsync(args);

if (args.Length == 0)
{
    Console.Error.WriteLine("Использование: Jornal.Inspect <файл.html|файл.pdf> [...]");
    PriceCommands.PrintUsage();
    Console.Error.WriteLine("Linux: Jornal.Inspect local-copy <копия.mdb> <новый.journal-test.json>");
    Console.Error.WriteLine("Jornal.Inspect block-spec <файл.html|файл.htm> [...]");
    Console.Error.WriteLine("Jornal.Inspect hans-spec <файл.pdf> [...]");
    return 1;
}

var jsonOptions = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
var reader = new DocumentReader();
var failed = false;
foreach (var path in args)
{
    try
    {
        var document = await reader.ReadAsync(path);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            File = Path.GetFileName(path),
            Format = document.Kind.ToString(),
            Tables = document.Tables.Count,
            Rows = document.Tables.Sum(table => table.Rows.Count),
            NonEmptyCells = document.Tables.Sum(table => table.Rows.Sum(row => row.Cells.Count(cell => !string.IsNullOrWhiteSpace(cell.Text)))),
            Pages = document.Pages.Count,
            Words = document.Pages.Sum(page => page.Words.Count)
        }, jsonOptions));
    }
    catch (Exception exception) when (exception is not OutOfMemoryException)
    {
        Console.Error.WriteLine($"{Path.GetFileName(path)}: {exception.Message}");
        failed = true;
    }
}

return failed ? 1 : 0;
