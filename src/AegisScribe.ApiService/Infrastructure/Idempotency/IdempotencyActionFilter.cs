using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AegisScribe.Domain.Context;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AegisScribe.ApiService.Infrastructure.Idempotency;

// Idempotency-Key replay for creating POSTs (api-contract.md): a repeat within the retention window
// replays the stored response, and the same key with a different body is a 422 rather than a silent
// overwrite. The header is accepted, not required.
//
// Hashes the already-bound ViewModel rather than the raw body: simpler than buffering the stream, and
// the bound arguments are what determines the outcome. Only a 2xx is stored, so a corrected retry
// under the same key still goes through.
public class IdempotencyActionFilter(IIdempotencyStore store, ICurrentUser currentUser) : IAsyncActionFilter
{
    private const string HeaderName = "Idempotency-Key";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var headerValues) ||
            string.IsNullOrWhiteSpace(headerValues.ToString()))
        {
            await next();
            return;
        }

        var idempotencyKey = headerValues.ToString();
        var callerId = currentUser.Subject ?? currentUser.UserId ?? "anonymous";
        var cacheKey = $"idempotency:{context.ActionDescriptor.DisplayName}:{callerId}:{idempotencyKey}";
        var requestHash = HashArguments(context.ActionArguments);
        var ct = context.HttpContext.RequestAborted;

        var existing = await store.FindAsync(cacheKey, ct);
        if (existing is not null)
        {
            context.Result = existing.RequestHash == requestHash
                ? new ContentResult { StatusCode = existing.StatusCode, Content = existing.Body, ContentType = existing.ContentType }
                : ConflictResult(context);
            return;
        }

        var executed = await next();

        if (executed.Result is ObjectResult { Value: not null } objectResult)
        {
            var statusCode = objectResult.StatusCode ?? StatusCodes.Status200OK;
            if (statusCode is >= 200 and < 300)
            {
                await store.SaveAsync(cacheKey, new IdempotencyRecord
                {
                    RequestHash = requestHash,
                    StatusCode = statusCode,
                    ContentType = "application/json",
                    Body = JsonSerializer.Serialize(objectResult.Value, objectResult.Value.GetType()),
                }, ct);
            }
        }
    }

    private static ObjectResult ConflictResult(ActionExecutingContext context) => new(new ProblemDetails
    {
        Type = "https://api.aegisscribe.com/problems/idempotency-key-conflict",
        Title = "This Idempotency-Key was already used with a different request body.",
        Status = StatusCodes.Status422UnprocessableEntity,
        Instance = context.HttpContext.Request.Path,
    })
    {
        StatusCode = StatusCodes.Status422UnprocessableEntity,
        ContentTypes = { "application/problem+json" },
    };

    // CancellationToken is bound into ActionArguments alongside the ViewModel but carries a WaitHandle
    // that System.Text.Json can't serialize — excluded, since it plays no part in "same or different body".
    private static string HashArguments(IDictionary<string, object?> arguments)
    {
        var relevant = arguments.Where(kv => kv.Value is not CancellationToken)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        var json = JsonSerializer.Serialize(relevant);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
}
