using System.Globalization;
using System.Text;
using AegisScribe.ApiService.Auth;
using AegisScribe.ApiService.Infrastructure;
using AegisScribe.ApiService.Infrastructure.Idempotency;
using AegisScribe.Domain.Facade;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AegisScribe.ApiService.Controllers;

// This community's roster (7.2, completed in 7.4) — one row per character the community has taken in,
// carrying the rank IT assigned rather than the one the game reports.
//
// Reads are TenantMember, writes are TenantOfficer. Claiming is NOT here: 7.2b owns it, because adding
// somebody to the roster and claiming a character are different acts by different people.
//
// tenantSlug never binds into a ViewModel and never reaches the facade — ITenantContext.TenantId is
// the only tenant identifier below the controller. A caller with no membership in the named community
// gets a 404 from the resolution middleware, never a 403.
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/t/{tenantSlug}/roster")]
public class RosterController(IRosterFacade rosterFacade) : ControllerBase
{
    // Reads are TenantMember: a community's own roster is not privileged information inside it. The
    // officer note on each row is, and comes back null unless the caller is one.
    [HttpGet]
    [Authorize(Policy = AuthPolicies.TenantMember)]
    public async Task<ActionResult<CursorPageServiceModel<RosterEntryServiceModel>>> List(
        [FromQuery] string? cursor,
        [FromQuery] RosterSort sort = RosterSort.Rank,
        [FromQuery] int limit = 25,
        CancellationToken ct = default)
    {
        var (afterKey, afterId) = DecodeCursor(cursor, sort);
        var viewModel = new ListRosterViewModel
        {
            Limit = limit,
            Sort = sort,
            AfterKey = afterKey,
            AfterId = afterId,
        };

        var page = await rosterFacade.ListAsync(viewModel, ct);

        // hasMore is decided on the MAIN count, not the row count: the page is taken in mains, and a
        // full page of mains dragging alts along would otherwise look like an over-full page and end
        // the pagination early.
        var hasMore = page.MainCount == viewModel.Limit;

        return this.ConditionalOk(new CursorPageServiceModel<RosterEntryServiceModel>
        {
            Items = page.Items,
            NextCursor = hasMore ? EncodeCursor(page.Items, sort) : null,
            HasMore = hasMore,
        });
    }

    // Puts an existing global Character on the roster. Idempotency-Key'd, because a mobile network can
    // fail after the server commits and an officer would otherwise see the add refused as a duplicate
    // of itself.
    [HttpPost]
    [Idempotent]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [Authorize(Policy = AuthPolicies.TenantOfficer)]
    public async Task<IActionResult> Add(AddRosterEntryViewModel viewModel, CancellationToken ct)
    {
        var rosterEntryId = await rosterFacade.AddAsync(viewModel, ct);

        // Null means no such character — a 404 the foreign key would otherwise have made a 500.
        return rosterEntryId is null ? NotFound() : NoContent();
    }

    // Rank and note are separate PUTs rather than one PATCH, and that is a data-safety choice: JSON
    // cannot distinguish "field omitted" from "field set to null", so a combined PATCH sent by a UI
    // that only edits rank would silently wipe an officer's note.
    [HttpPut("{rosterEntryId:guid}/rank")]
    [Authorize(Policy = AuthPolicies.TenantOfficer)]
    public async Task<IActionResult> SetRank(
        Guid rosterEntryId, SetRosterRankViewModel viewModel, CancellationToken ct) =>
        await rosterFacade.SetRankAsync(rosterEntryId, viewModel, ct) ? NoContent() : NotFound();

    [HttpPut("{rosterEntryId:guid}/note")]
    [Authorize(Policy = AuthPolicies.TenantOfficer)]
    public async Task<IActionResult> SetOfficerNote(
        Guid rosterEntryId, SetOfficerNoteViewModel viewModel, CancellationToken ct) =>
        await rosterFacade.SetOfficerNoteAsync(rosterEntryId, viewModel, ct) ? NoContent() : NotFound();

    // 204 whether or not the entry was there: DELETE is idempotent and the intent is satisfied either
    // way (api-contract.md). The one case that is not 204 is an entry other entries call their main —
    // that is a 409, because removing it would orphan somebody's other characters.
    [HttpDelete("{rosterEntryId:guid}")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [Authorize(Policy = AuthPolicies.TenantOfficer)]
    public async Task<IActionResult> Remove(Guid rosterEntryId, CancellationToken ct)
    {
        await rosterFacade.RemoveAsync(rosterEntryId, ct);

        return NoContent();
    }

    // Alt linking (7.3). TenantMember, because a member reorganises their OWN characters — which ones
    // those are is a claim question, and answering it means reading data, so it is a Business rule
    // rather than a policy. An officer passes that rule by rank and is audited for it.
    [HttpPut("{rosterEntryId:guid}/main")]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [Authorize(Policy = AuthPolicies.TenantMember)]
    public async Task<IActionResult> LinkAlt(
        Guid rosterEntryId, LinkAltViewModel viewModel, CancellationToken ct) =>
        await rosterFacade.LinkAltAsync(rosterEntryId, viewModel, ct) ? NoContent() : NotFound();

    [HttpDelete("{rosterEntryId:guid}/main")]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = AuthPolicies.TenantMember)]
    public async Task<IActionResult> UnlinkAlt(Guid rosterEntryId, CancellationToken ct) =>
        await rosterFacade.UnlinkAltAsync(rosterEntryId, ct) ? NoContent() : NotFound();

    // Where an unranked entry sorts under RosterSort.Rank — must match
    // RosterEntryRepository.UnrankedOrder, because this is the value the keyset compares against when
    // the page resumes.
    private const int UnrankedOrder = int.MaxValue;

    // The sort key of the last MAIN on the page, plus its id. The sort's own name rides along so a
    // cursor minted under one ordering cannot be replayed against another — decoded below, a mismatch
    // restarts the page rather than seeking to a position that means nothing in the new ordering.
    private static string? EncodeCursor(IReadOnlyList<RosterEntryServiceModel> items, RosterSort sort)
    {
        var lastMain = items.LastOrDefault(item => item.MainRosterEntryId is null);

        if (lastMain is null)
        {
            return null;
        }

        // Compound for the sorts that tie: every ordering breaks a tie by character name before falling
        // back to the id, so the cursor has to carry both halves or the resumed page loses the
        // tie-break and starts reordering equally-ranked members between reads.
        var name = lastMain.CharacterName.ToLowerInvariant();

        var key = sort switch
        {
            RosterSort.Name => name,
            RosterSort.ItemLevel => $"{lastMain.ItemLevel.ToString(CultureInfo.InvariantCulture)}~{name}",
            _ => $"{(lastMain.RankSortOrder ?? UnrankedOrder).ToString(CultureInfo.InvariantCulture)}~{name}",
        };

        return Convert.ToBase64String(Encoding.UTF8.GetBytes($"{sort}|{key}|{lastMain.Id}"));
    }

    // The cursor is opaque but still client-supplied, so it is re-validated here like any other input
    // (api-contract.md) — a malformed value, or one from a different sort, restarts the page rather
    // than failing the request. There is nothing tenant-shaped in it to tamper with: the tenant comes
    // from the route either way.
    private static (string? AfterKey, Guid? AfterId) DecodeCursor(string? cursor, RosterSort sort)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            return (null, null);
        }

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = decoded.Split('|', 3);

            if (parts.Length == 3
                && Enum.TryParse<RosterSort>(parts[0], out var cursorSort)
                && cursorSort == sort
                && Guid.TryParse(parts[2], out var id))
            {
                return (parts[1], id);
            }
        }
        catch (FormatException)
        {
            // Fall through to the unpositioned page below.
        }

        return (null, null);
    }
}
