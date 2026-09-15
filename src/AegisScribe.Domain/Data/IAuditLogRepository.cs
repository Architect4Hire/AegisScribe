using AegisScribe.Domain.Managers.Models.Domain;

namespace AegisScribe.Domain.Data;

// Write-only for now. 14.2 adds the officer-visible read, under TenantOfficer; audit rows are never
// editable or deletable from the app, so there is no update or delete here and there should not be one.
public interface IAuditLogRepository
{
    // Stages the row only — an audit row is always written alongside the action it records, inside
    // that action's transaction. An audit write that could commit on its own would be a way to record
    // something that did not happen.
    Task AddAsync(AuditLog entry, CancellationToken ct);
}
