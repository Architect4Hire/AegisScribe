namespace AegisScribe.ApiService.Infrastructure.Idempotency;

// A replayable HTTP response. Host-only shape (it's a protocol concern — replaying a whole response —
// not a domain concept), which is why this lives in AegisScribe.ApiService and not AegisScribe.Domain.
public class IdempotencyRecord
{
    public string RequestHash { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string? ContentType { get; set; }
    public string Body { get; set; } = string.Empty;
}
