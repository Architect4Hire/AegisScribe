namespace AegisScribe.Domain.Managers.Models.Domain;

// One community's current sync-budget window. Named for the window rather than the budget because the
// limit itself is configuration and the same for everyone; what is stored per tenant is how much of the
// current window they have spent.
//
// In SQL rather than Redis, which is the decision worth recording: IDistributedCache has no atomic
// increment, so two officers pressing re-sync at once would both read N and both write N+1, leaking the
// budget exactly when it is under most pressure. A conditional UPDATE is atomic in one statement, and
// the row survives a cache flush — which would otherwise hand everyone a full budget back.
public class TenantSyncBudgetWindow : ITenantScoped
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    // Fixed, not sliding: a sliding window needs per-request history, and this only has to be fair.
    public DateTimeOffset WindowStartedAt { get; set; }

    // Blizzard CALLS, not operations — the Terms of Use count calls, and a budget denominated in
    // "syncs" would let one community spend forty times another's while both showed the same number.
    public int CallsConsumed { get; set; }
}
