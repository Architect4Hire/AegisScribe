using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AegisScribe.Domain.Facade;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace AegisScribe.ApiService.Controllers;

// Tenant-less: Character is global reference data (tenancy.md) — no /t/{tenantSlug} segment, no
// tenant policy, anonymous by default (no [Authorize], and the API has no fallback auth policy).
// This is the app's public front door (4.5, scrub-prompts.md) — do not move it under /api/v1/t/.
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/characters")]
public class CharactersController(ICharacterFacade characterFacade) : ControllerBase
{
    [HttpGet("{realm}/{name}")]
    public async Task<ActionResult<CharacterDetailServiceModel>> Get(
        string realm, string name, [FromQuery] string region, CancellationToken ct)
    {
        var viewModel = new CharacterLookupViewModel { Region = region, RealmSlug = realm, Name = name };
        var character = await characterFacade.GetCharacterAsync(viewModel, ct);
        if (character is null)
        {
            return NotFound();
        }

        return ConditionalOk(character);
    }

    [HttpGet]
    public async Task<ActionResult<CursorPageServiceModel<CharacterSummaryServiceModel>>> Search(
        [FromQuery] string region,
        [FromQuery] string? realm,
        [FromQuery] string? name,
        [FromQuery] string? cursor,
        [FromQuery] int limit = 25,
        CancellationToken ct = default)
    {
        var (afterNameLower, afterId) = DecodeCursor(cursor);
        var viewModel = new SearchCharactersViewModel
        {
            Region = region,
            RealmSlug = realm,
            Name = name,
            AfterNameLower = afterNameLower,
            AfterId = afterId,
            Limit = limit,
        };

        var items = await characterFacade.SearchCharactersAsync(viewModel, ct);

        // A full page is treated as "more may follow" — an exact answer needs fetching limit+1 rows,
        // which isn't worth it for how cursor pagination is consumed (the client just asks again and
        // gets an empty page at the true end).
        var hasMore = items.Count == viewModel.Limit;
        return ConditionalOk(new CursorPageServiceModel<CharacterSummaryServiceModel>
        {
            Items = items,
            NextCursor = hasMore ? EncodeCursor(items[^1]) : null,
            HasMore = hasMore,
        });
    }

    // ETag + If-None-Match on collection and detail GETs (api-contract.md): a 304 costs a few bytes
    // where the body costs kilobytes. The ETag is a hash of the serialized body rather than, say,
    // LastSyncedAt, because CharacterEquipment tracks its own staleness independently of Character
    // (CharacterEquipment.cs) — hashing the body is correct regardless of which part changed.
    private ActionResult<T> ConditionalOk<T>(T body)
    {
        var etag = ComputeETag(body);
        if (Request.Headers.TryGetValue(HeaderNames.IfNoneMatch, out var ifNoneMatch) && ifNoneMatch == etag)
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        Response.Headers.ETag = etag;
        return Ok(body);
    }

    private static string ComputeETag<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        var hash = SHA256.HashData(bytes);
        return $"\"{Convert.ToHexString(hash)}\"";
    }

    private static string EncodeCursor(CharacterSummaryServiceModel last) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{last.Name.ToLowerInvariant()}|{last.Id}"));

    // The cursor is opaque to the client and re-validated here like any other input (api-contract.md)
    // — a malformed value just restarts the page rather than failing the request.
    private static (string? AfterNameLower, Guid? AfterId) DecodeCursor(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            return (null, null);
        }

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = decoded.Split('|', 2);
            if (parts.Length == 2 && Guid.TryParse(parts[1], out var id))
            {
                return (parts[0], id);
            }
        }
        catch (FormatException)
        {
            // Fall through to (null, null) below.
        }

        return (null, null);
    }
}
