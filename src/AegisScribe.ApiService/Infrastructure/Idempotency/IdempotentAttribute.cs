using Microsoft.AspNetCore.Mvc;

namespace AegisScribe.ApiService.Infrastructure.Idempotency;

// TypeFilterAttribute resolves IdempotencyActionFilter through DI, so it gets constructor-injected
// dependencies (IIdempotencyStore, ICurrentUser) that a plain attribute can't take directly.
public class IdempotentAttribute() : TypeFilterAttribute(typeof(IdempotencyActionFilter));
