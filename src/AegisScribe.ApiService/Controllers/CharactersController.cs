using System.Text;
using AegisScribe.ApiService.Infrastructure;
using AegisScribe.Domain.Facade;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

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

        return this.ConditionalOk(character);
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
        return this.ConditionalOk(new CursorPageServiceModel<CharacterSummaryServiceModel>
        {
            Items = items,
            NextCursor = hasMore ? EncodeCursor(items[^1]) : null,
            HasMore = hasMore,
        });
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
