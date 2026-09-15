using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace AegisScribe.ApiService.Infrastructure;

// ETag + If-None-Match on collection and detail GETs (api-contract.md): a 304 costs a few bytes where
// the body costs kilobytes, and on a metered connection with a background refresh that difference is
// the user's data allowance.
//
// Extracted from CharactersController when the roster became the second caller (7.2). It stays in the
// API project because it is pure HTTP shape — Domain holds no IActionResult (backend.md).
public static class ConditionalResponse
{
    /// <summary>
    /// Returns <paramref name="body"/> with an <c>ETag</c>, or a bare 304 when the caller already has
    /// that exact representation.
    /// </summary>
    public static ActionResult<T> ConditionalOk<T>(this ControllerBase controller, T body)
    {
        var etag = ComputeETag(body);

        if (controller.Request.Headers.TryGetValue(HeaderNames.IfNoneMatch, out var ifNoneMatch)
            && ifNoneMatch == etag)
        {
            return controller.StatusCode(StatusCodes.Status304NotModified);
        }

        controller.Response.Headers.ETag = etag;

        return controller.Ok(body);
    }

    // A hash of the serialized body rather than a timestamp column. For a character that is because
    // CharacterEquipment tracks its own staleness independently of Character, so no single column is
    // authoritative; for a roster page it is because the page is a join across three tables whose
    // rows change independently. Hashing what was actually produced is correct in both cases.
    private static string ComputeETag<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        var hash = SHA256.HashData(bytes);

        return $"\"{Convert.ToHexString(hash)}\"";
    }
}
