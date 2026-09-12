namespace AegisScribe.ApiService.Infrastructure.Idempotency;

public interface IIdempotencyStore
{
    Task<IdempotencyRecord?> FindAsync(string key, CancellationToken ct);

    Task SaveAsync(string key, IdempotencyRecord record, CancellationToken ct);
}
