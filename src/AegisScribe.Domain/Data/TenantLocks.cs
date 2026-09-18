using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Domain.Data;

// Serialising the operations whose rule is about a SET of rows rather than about the row being
// written. One place, because there are now two of them and the reasoning is identical.
//
// The reasoning, which cost a real bug to learn (8.3, 2026-09-15):
//
// Putting a rule in the WHERE of a single conditional UPDATE is the repo's standard answer to a
// read-then-write race, and it works — when the rule reads THE ROW BEING WRITTEN. There the exclusive
// lock does the serialising for you.
//
// It is NOT enough when the rule is a subquery over other rows ("is there another Owner", "is anything
// pointing at this entry"). SQL Server evaluates that predicate BEFORE it takes the exclusive lock on
// the target, and under READ COMMITTED the shared locks the subquery takes are released as soon as it
// has read. Two statements can therefore both evaluate their guard against pre-commit state, then
// write DIFFERENT rows — nothing conflicts, nothing throws, and both rules are broken at once. Two
// owners demoting each other left a community with none, reproducibly, and that is exactly the shape.
//
// So: whoever wants to decide something about the set takes an exclusive lock on the community first,
// and the whole decision happens behind it.
internal static class TenantLocks
{
    /// <summary>
    /// Takes an exclusive lock on the community for the rest of the current transaction.
    /// </summary>
    /// <remarks>
    /// The tenant row rather than the rows being guarded, deliberately: a lock over "the owners of
    /// this community" or "the entries pointing at this one" is a range whose membership is what the
    /// racing statement is about to change, and range locks over a moving set are where this gets
    /// subtle. One row that every such operation agrees to queue on is simpler and obviously correct.
    ///
    /// UPDLOCK conflicts only with other UPDLOCK and exclusive holders, so ordinary READS of the
    /// tenant — every request's own tenant resolution among them — are untouched. HOLDLOCK keeps it to
    /// the end of the transaction instead of releasing it after the statement.
    ///
    /// The operations behind it are officer- and member-rate: role changes, removals, alt links. A few
    /// a day per community. Serialising them costs nothing worth measuring.
    ///
    /// Must be called INSIDE ExecuteInTransactionAsync — a lock taken outside one is released
    /// immediately and buys nothing.
    /// </remarks>
    public static Task LockCommunityAsync(this DbContext db, Guid tenantId, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync(
            $"SELECT 1 FROM Tenants WITH (UPDLOCK, HOLDLOCK) WHERE Id = {tenantId}", ct);
}
