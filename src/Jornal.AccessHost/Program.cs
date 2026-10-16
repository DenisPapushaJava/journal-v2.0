using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Jornal.Core.Orders;
using Jornal.Infrastructure.Access;

Console.InputEncoding = new UTF8Encoding(false);
Console.OutputEncoding = new UTF8Encoding(false);
if (!OperatingSystem.IsWindows() || Environment.Is64BitProcess)
{
    Console.Error.WriteLine("Модуль Access должен запускаться как Windows x86.");
    return 2;
}

// Хост использует встроенный Jet, не регистрирует DLL и не устанавливает компоненты Office.
if (args.SequenceEqual(["--probe"]))
{
    try
    {
        var type = Type.GetTypeFromProgID("Microsoft.Jet.OLEDB.4.0", throwOnError: true)!;
        var provider = Activator.CreateInstance(type)!;
        Marshal.FinalReleaseComObject(provider);
        return 0;
    }
    catch (Exception exception) { Console.Error.WriteLine(exception.Message); return 3; }
}
if (args.Length != 0) return 2;

AccessHostRequest? request = null;
AccessHostResponse response;
try
{
    var json = await Console.In.ReadLineAsync() ?? throw new InvalidDataException("Запрос не передан.");
    request = JsonSerializer.Deserialize<AccessHostRequest>(json) ?? throw new InvalidDataException("Пустой запрос.");
    if (request.Protocol != 1 || !Enum.IsDefined(request.Operation) || request.Operation == AccessHostOperation.Probe ||
        !Path.IsPathFullyQualified(request.Path) || !string.Equals(Path.GetExtension(request.Path), ".mdb", StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("Несовместимый запрос модуля Access.");
    var editor = new OleDbOrderBatchEditor(request.Path);
    var rows = request.Operation switch
    {
        AccessHostOperation.Read => await new AccessOrderReader(request.Path).ReadAsync(),
        AccessHostOperation.SetColor => await editor.SetColorAsync(request.Expected ?? [], request.Color),
        AccessHostOperation.SetDeliveryNumber => await editor.SetDeliveryNumberAsync(request.Expected ?? [], request.DeliveryNumber ?? ""),
        _ => throw new InvalidDataException("Неизвестное действие.")
    };
    response = new(1, rows.ToArray());
}
catch (OrderWriteException exception) { response = new(1, Error: exception.Message, Failure: exception.Failure); }
catch (Exception exception)
{
    response = new(1, Error: exception is OrderReadException or ArgumentException or InvalidDataException
        ? exception.Message : "Модуль Access не смог выполнить запрос.",
        Failure: request?.Operation is AccessHostOperation.SetColor or AccessHostOperation.SetDeliveryNumber
            ? exception is ArgumentException or InvalidDataException ? OrderWriteFailure.NotSaved : OrderWriteFailure.OutcomeUnknown : null);
}
await Console.Out.WriteLineAsync(JsonSerializer.Serialize(response));
return 0;
