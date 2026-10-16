using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Jornal.Core.Pricing;
using Jornal.Infrastructure.Access;

namespace Jornal.Inspect;

internal static class PriceCommands
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static void PrintUsage()
    {
        Console.Error.WriteLine("Jornal.Inspect prices <база.mdb>");
        Console.Error.WriteLine("Jornal.Inspect price <база.mdb> <Лазер|Плазма|Газо-кислородная|HansLaser> <толщина_мм> <целые_метры> <коэффициент> [ID_материала]");
    }

    public static async Task<int> RunAsync(string[] args)
    {
        if (args[0] == "prices" && args.Length == 2)
        {
            var reader = new AccessPriceReader(args[1]);
            var failed = false;
            foreach (var name in PriceTables.All)
            {
                try { Console.WriteLine(JsonSerializer.Serialize(await reader.ReadAsync(name), JsonOptions)); }
                catch (Exception exception) when (exception is PriceReadException or ArgumentException)
                {
                    Console.Error.WriteLine(name + ": " + exception.Message);
                    failed = true;
                }
            }
            return failed ? 1 : 0;
        }
        if (args[0] == "price" && args.Length is 6 or 7)
        {
            try
            {
                var request = new CuttingPriceRequest(args[2], Number(args[3]),
                    int.Parse(args[4], NumberStyles.None, CultureInfo.InvariantCulture), Number(args[5]), args.Length == 7 ? args[6] : null);
                var quote = await new CuttingPriceService(new AccessPriceReader(args[1])).CalculateAsync(request);
                Console.WriteLine(JsonSerializer.Serialize(quote, JsonOptions));
                return 0;
            }
            catch (Exception exception) when (exception is PriceReadException or PriceCalculationException or ArgumentException or FormatException or OverflowException)
            {
                Console.Error.WriteLine("Цена не рассчитана: " + exception.Message);
                return 1;
            }
        }
        PrintUsage();
        return 1;
    }

    private static decimal Number(string value) => decimal.Parse(value.Replace(',', '.'),
        NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
}
