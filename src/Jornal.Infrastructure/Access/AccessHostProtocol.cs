using Jornal.Core.Orders;
using Jornal.Core.Pricing;

namespace Jornal.Infrastructure.Access;

public enum AccessHostOperation { Probe, Read, SetColor, SetDeliveryNumber, ReadPriceTable }

// Один запрос на процесс. Рабочие пути и данные передаются только по перенаправленному stdin.
public sealed record AccessHostRequest(int Protocol, AccessHostOperation Operation, string Path,
    OrderSummary[]? Expected = null, int Color = 0, string? DeliveryNumber = null, string? PriceTableName = null);

public sealed record AccessHostResponse(int Protocol, OrderSummary[]? Orders = null,
    string? Error = null, OrderWriteFailure? Failure = null, string? Provider = null, PriceTable? PriceTable = null);
