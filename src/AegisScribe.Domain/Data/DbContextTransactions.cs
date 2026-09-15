using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Domain.Data;

// The one transaction idiom, in one place.
//
// The shape is forced rather than chosen (backend.md): Aspire's SQL Server integration enables
// retry-on-failure, and EF Core's execution strategy refuses to run inside a transaction the caller
// opened itself. Passing the whole unit in as a callback is what lets the two coexist.
//
// Two consequences every caller inherits:
//   - the callback MAY RUN MORE THAN ONCE on a transient failure, so it must be safe to repeat;
//   - only work done through this DbContext rolls back, so an outbound HTTP call, a model call or a
//     queue publish is staged BEFORE the callback, never inside it.
internal static class DbContextTransactions
{
    public static async Task<TResult> ExecuteInTransactionAsync<TResult>(
        this DbContext db, Func<CancellationToken, Task<TResult>> operation, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var result = await operation(ct);

            // One SaveChanges for the whole unit: the callback stages changes, this commits them, so a
            // throw on any leg rolls the entire operation back.
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return result;
        });
    }
}
