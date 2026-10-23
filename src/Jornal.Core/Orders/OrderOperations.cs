using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jornal.Core.Orders;

[Flags]
public enum OrderOperation { None = 0, Chamfer = 1 }

// Новые операции хранятся отдельно от прежней таблицы гибки/комментария.
public sealed record OrderOperations(OrderOperation Kinds, string IdentityHash)
{
    public const string UnavailableMessage = "Дополнительные операции недоступны. Обновите список (F5).";
    public bool HasChamfer => Kinds.HasFlag(OrderOperation.Chamfer);
    public void Validate()
    {
        if (Kinds == OrderOperation.None || (Kinds & ~OrderOperation.Chamfer) != 0 ||
            IdentityHash is not { Length: 64 } || !IdentityHash.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F'))
            throw new OrderWriteException(OrderWriteFailure.NotSaved, "Неизвестная дополнительная операция или её привязка к заявке.");
    }
    public bool Matches(OrderSummary row) => IdentityHash == Fingerprint(row);
    public static OrderOperations? For(OrderSummary row, bool hasChamfer) =>
        hasChamfer ? new(OrderOperation.Chamfer, Fingerprint(row)) : null;
    private static string Fingerprint(OrderSummary row) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new[] { "Journal.Operations.v1", row.OrderDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), row.SpecificationNumber }))));
}

public interface IOrderOperationsInitializer
{
    Task PrepareOperationsAsync(CancellationToken cancellationToken = default);
}
