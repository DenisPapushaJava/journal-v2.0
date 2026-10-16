using Jornal.Core.Pricing;
using Jornal.Infrastructure.Local;

namespace Jornal.Inspect;

internal static class LocalCopyCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (!OperatingSystem.IsLinux() || args.Length != 3)
        {
            Console.Error.WriteLine("Linux: Jornal.Inspect local-copy <копия.mdb> <новый.journal-test.json>");
            return 1;
        }
        try
        {
            await new LocalJournalImporter().ImportAsync(args[1], args[2]);
            var store = new LocalJournalStore(args[2]);
            Console.WriteLine($"Создана локальная тестовая копия: {(await store.ReadAsync()).Count} заявок, {PriceTables.All.Count} прайсов.");
            return 0;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}
