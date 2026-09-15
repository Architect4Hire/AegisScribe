namespace AegisScribe.Domain.Managers.Models.Domain;

// Who did what to whom, in which community, when (7.2b).
//
// Built here because this is the first phase that must write one: 14.2 owns the officer-visible
// SCREEN, and 7.4, membership management and the event lock only ever say "writes an AuditLog row".
// So the entity itself has no owning phase before the first audited action, which is the officer's
// claim clear.
//
// Deliberately generic rather than claim-shaped, because those later phases all write here. There is
// no read path yet, and no endpoint — 14.2 adds that, under TenantOfficer, read-only. Audit rows are
// never editable or deletable from the app.
public class AuditLog : ITenantScoped
{
    public Guid Id { get; set; }

    // Stamped by TenantStampingInterceptor on insert, never assigned by business code (tenancy.md).
    public Guid TenantId { get; set; }

    // The officer who acted, from ICurrentUser. The whole point of the row: once officers can act on
    // other people's data, "who did this" stops being optional.
    public string ActorUserId { get; set; } = string.Empty;

    // The person acted upon, where there is one — the displaced claim holder, the demoted member.
    // Null for actions that are about a thing rather than a person.
    public string? SubjectUserId { get; set; }

    public AuditAction Action { get; set; }

    // What kind of row was acted on, and which one. Kept as a plain string/Guid pair rather than a
    // foreign key on purpose: an audit row must survive the deletion of the thing it describes, and an
    // FK would either block that deletion or cascade the evidence away with it.
    public string TargetType { get; set; } = string.Empty;

    public Guid? TargetId { get; set; }

    // Human-readable state either side of the action, for 14.2 to render. Not machine-parsed, and
    // deliberately not a serialized entity — a schema change must not make old rows unreadable.
    public string? Before { get; set; }

    public string? After { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
