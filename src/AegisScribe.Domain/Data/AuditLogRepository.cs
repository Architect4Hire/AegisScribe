using AegisScribe.Domain.Managers.Models.Domain;

namespace AegisScribe.Domain.Data;

public class AuditLogRepository(AegisScribeDbContext db) : IAuditLogRepository
{
    public Task AddAsync(AuditLog entry, CancellationToken ct)
    {
        // Staged, not saved. This repository resolves the same scoped AegisScribeDbContext as the one
        // performing the action, which is what lets the two writes land in a single transaction — the
        // arrangement DomainServiceCollectionExtensions already relies on for RealmRepository and
        // CharacterRepository.
        db.AuditLogs.Add(entry);

        return Task.CompletedTask;
    }
}
