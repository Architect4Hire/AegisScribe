namespace AegisScribe.Domain.Managers.Models.Domain;

// One community's current sync-budget window. Tenant-scoped (tenancy.md): the whole point is that this
// is a fact about THIS community and no other, so it carries TenantId and gets the global query filter
// like every other tenant-scoped entity.
//
// Named for the window rather than the budget because the budget itself is configuration — the limit
// lives in TenantSyncBudgetOptions and is the same for everyone. What is stored per tenant is how much
// of the current window they have spent.
//
// In SQL rather than Redis, which is the decision worth recording: IDistributedCache has no atomic
// increment, so two officers pressing re-sync at the same moment would both read N and both write N+1,
// and the budget would leak exactly when it is under the most pressure. A conditional UPDATE gives
// atomicity in one statement (see TenantSyncBudgetRepository), and the row survives a cache flush —
// which would otherwise hand every community a full budget back.
public class TenantSyncBudgetWindow : ITenantScoped
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    // Fixed window, not sliding — the same choice Program.cs's rate limiter documents, and for the same
    // reason: a sliding window needs per-request history, and this only has to be fair, not smooth.
    public DateTimeOffset WindowStartedAt { get; set; }

    // Blizzard CALLS, not operations. A 400-member roster re-sync is around 800 calls, and the Terms of
    // Use count calls — a budget denominated in "syncs" would let one community spend forty times
    // another's while both showed the same number.
    public int CallsConsumed { get; set; }
}
