using Jornal.Core.Pricing;

namespace Jornal.Infrastructure.Local;

public sealed partial class LocalJournalStore : IPriceEditor
{
    public Task<PriceTable> SaveAsync(PriceTableChange change, CancellationToken cancellationToken = default)
    {
        var snapshot = change.Snapshot();
        return WriteAsync(book =>
        {
            var current = book.Prices.SingleOrDefault(price => price.Name == snapshot.Expected.Name);
            if (current is null || !PriceTableChange.Matches(current, snapshot.Expected)) throw PriceTableChange.Conflict();
            return (book with { Prices = book.Prices.Select(price => price.Name == current.Name ? snapshot.Values : price).ToArray() }, snapshot.Values);
        }, cancellationToken);
    }
}
