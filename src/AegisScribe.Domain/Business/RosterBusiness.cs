using AegisScribe.Domain.Context;
using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;

namespace AegisScribe.Domain.Business;

// The roster's rules: who may read what, and what an officer's writes are allowed to do.
//
// WHICH rows exist is the query filter's answer, and who may reach the endpoint at all is the policy's.
// What lives here is everything that needs to read something before it can decide (auth.md).
public class RosterBusiness(
    IRosterEntryDataLayer dataLayer,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    TimeProvider timeProvider) : IRosterBusiness
{
    public async Task<RosterPage> ListAsync(ListRosterViewModel viewModel, CancellationToken ct)
    {
        // Decided here because it depends on the caller's membership rather than the route, and passed
        // DOWN so the note is blanked in the SQL projection — an officer-private note never leaves the
        // database on a request that had no business reading it.
        var includeOfficerNote = await IsOfficerAsync(RequireUserId(), ct);

        return await dataLayer.ListAsync(
            viewModel.Sort,
            viewModel.AfterKey,
            viewModel.AfterId,
            viewModel.Limit,
            includeOfficerNote,
            ct);
    }

    public async Task<Guid?> AddAsync(AddRosterEntryViewModel viewModel, CancellationToken ct)
    {
        var userId = RequireUserId();

        // Without this the foreign key would turn an unknown character into a 500.
        if (!await dataLayer.CharacterExistsAsync(viewModel.CharacterId, ct))
        {
            return null;
        }

        // The unique index is the authority under a race; this is the readable answer.
        if (await dataLayer.IsOnRosterAsync(viewModel.CharacterId, ct))
        {
            throw new CharacterAlreadyOnRosterException();
        }

        await RequireRankIsOursAsync(viewModel.TenantRankId, ct);

        var entry = new RosterEntry
        {
            Id = Guid.NewGuid(),
            CharacterId = viewModel.CharacterId,
            TenantRankId = viewModel.TenantRankId,
            // TenantId is absent on purpose — the interceptor stamps it (tenancy.md).
            JoinedAt = timeProvider.GetUtcNow(),
        };

        await dataLayer.AddAsync(
            entry,
            await AuditIfActingOnSomebodyElseAsync(
                entry.CharacterId, entry.Id, userId, AuditAction.RosterEntryAdded, "Not on roster", "On roster", ct),
            ct);

        return entry.Id;
    }

    public async Task<bool> SetRankAsync(
        Guid rosterEntryId, SetRosterRankViewModel viewModel, CancellationToken ct)
    {
        var userId = RequireUserId();
        var entry = await dataLayer.FindEntityAsync(rosterEntryId, ct);

        if (entry is null)
        {
            return false;
        }

        // A rank id from another community resolves to nothing under the query filter. Refused rather
        // than silently stored or cleared — either would leave the roster showing something nobody
        // chose.
        await RequireRankIsOursAsync(viewModel.TenantRankId, ct);

        await dataLayer.SetRankAsync(
            rosterEntryId,
            viewModel.TenantRankId,
            await AuditIfActingOnSomebodyElseAsync(
                entry.CharacterId,
                entry.Id,
                userId,
                AuditAction.RosterEntryRankChanged,
                DescribeRank(entry.TenantRankId),
                DescribeRank(viewModel.TenantRankId),
                ct),
            ct);

        return true;
    }

    public async Task<bool> SetOfficerNoteAsync(
        Guid rosterEntryId, SetOfficerNoteViewModel viewModel, CancellationToken ct)
    {
        var userId = RequireUserId();
        var entry = await dataLayer.FindEntityAsync(rosterEntryId, ct);

        if (entry is null)
        {
            return false;
        }

        await dataLayer.SetOfficerNoteAsync(
            rosterEntryId,
            viewModel.OfficerNote,
            await AuditIfActingOnSomebodyElseAsync(
                entry.CharacterId,
                entry.Id,
                userId,
                AuditAction.RosterEntryNoteChanged,
                // The note's CONTENT is not copied into the audit row. It is one person's private
                // remark about another, and an officer-readable audit table would otherwise become a
                // second, permanent copy of it.
                DescribeNote(entry.OfficerNote),
                DescribeNote(viewModel.OfficerNote),
                ct),
            ct);

        return true;
    }

    public async Task<bool> RemoveAsync(Guid rosterEntryId, CancellationToken ct)
    {
        var userId = RequireUserId();
        var entry = await dataLayer.FindEntityAsync(rosterEntryId, ct);

        // Already gone is a satisfied intent — DELETE is idempotent (api-contract.md) — and so is an
        // entry belonging to another community, which the query filter makes indistinguishable.
        if (entry is null)
        {
            return true;
        }

        // Refused rather than cascading: detaching somebody's other characters as a side effect is the
        // silent kind of damage, and the officer cannot see what they would orphan. The Restrict FK
        // would fail this write anyway; this turns a 500 into an answer.
        var alts = await dataLayer.CountAltsAsync(rosterEntryId, ct);

        if (alts > 0)
        {
            throw new RosterEntryHasAltsException(alts);
        }

        await dataLayer.RemoveAsync(
            entry,
            await AuditIfActingOnSomebodyElseAsync(
                entry.CharacterId, entry.Id, userId, AuditAction.RosterEntryRemoved, "On roster", "Removed", ct),
            ct);

        return true;
    }

    private async Task RequireRankIsOursAsync(Guid? tenantRankId, CancellationToken ct)
    {
        if (tenantRankId is null || await dataLayer.RankExistsAsync(tenantRankId.Value, ct))
        {
            return;
        }

        throw new DomainValidationException(new Dictionary<string, string[]>
        {
            ["TenantRankId"] = ["That rank does not exist in this community."],
        });
    }

    private static string DescribeRank(Guid? rankId) => rankId is null ? "No rank" : $"Rank {rankId}";

    // Presence, never content — see the call site in SetOfficerNoteAsync.
    private static string DescribeNote(string? note) =>
        string.IsNullOrWhiteSpace(note) ? "No note" : "Note set";

    public async Task<bool> LinkAltAsync(Guid rosterEntryId, LinkAltViewModel viewModel, CancellationToken ct)
    {
        var userId = RequireUserId();

        // Rule 1 — the self-link. A field error rather than a conflict: the form highlights the
        // character picker and the member chooses somebody else.
        if (rosterEntryId == viewModel.MainRosterEntryId)
        {
            throw new DomainValidationException(new Dictionary<string, string[]>
            {
                [nameof(LinkAltViewModel.MainRosterEntryId)] =
                    ["A character cannot be its own main."],
            });
        }

        // Rule 2 — both ends must be in THIS community. The query filter makes an id from another
        // community come back null, so a cross-tenant link is a 404 by the same mechanism as every
        // other cross-tenant id in this repo.
        var entry = await dataLayer.FindEntityAsync(rosterEntryId, ct);
        var main = await dataLayer.FindEntityAsync(viewModel.MainRosterEntryId, ct);

        if (entry is null || main is null)
        {
            return false;
        }

        var callerClaimsEntry = await dataLayer.IsClaimedByAsync(entry.CharacterId, userId, ct);

        // Linking asserts a relationship BETWEEN two characters, so owning one end is not enough. An
        // officer passes by rank instead — and is audited for it below.
        if (!await IsOfficerAsync(userId, ct))
        {
            var callerClaimsMain = await dataLayer.IsClaimedByAsync(main.CharacterId, userId, ct);

            if (!callerClaimsEntry || !callerClaimsMain)
            {
                throw new AltLinkNotPermittedException();
            }
        }

        // Rules 3 and 4 are checked here for the MESSAGE, not for the enforcement — reading then
        // writing is a race that lets two opposite-direction links commit a cycle. They are enforced
        // inside the UPDATE's own WHERE, one layer down; what these produce is the SPECIFIC refusal,
        // which a rows-affected count cannot.
        await ThrowIfDepthWouldBeExceededAsync(entry, main, ct);

        // Captured before the write: an entry may already be an alt of somebody else, and re-parenting
        // is normal. An audit row claiming "No main" beforehand would be wrong in exactly the case an
        // officer is most likely to be asked about.
        var previousMainId = entry.MainRosterEntryId;

        var linked = await dataLayer.TryLinkAltAsync(
            entry.Id,
            main.Id,
            await AuditIfActingOnSomebodyElseAsync(
                entry.CharacterId,
                entry.Id,
                userId,
                AuditAction.RosterEntryAltLinked,
                DescribeMain(previousMainId),
                DescribeMain(main.Id),
                ct),
            ct);

        if (!linked)
        {
            // The checks above passed and the write still refused, so the state changed underneath this
            // request. Re-read to find out which rule now holds and say so — the alternative is a 409
            // with no reason, on the one path where the user did nothing wrong.
            await ThrowIfDepthWouldBeExceededAsync(
                await dataLayer.FindEntityAsync(entry.Id, ct) ?? entry,
                await dataLayer.FindEntityAsync(main.Id, ct) ?? main,
                ct);

            // Both rules read clean, so the row moved again between the refusal and now. Refuse rather
            // than retry: another attempt could loop, and the caller re-reading is the honest next step.
            throw new AltDepthException(AltDepthReason.TargetIsAlreadyAnAlt);
        }

        return true;
    }

    private async Task ThrowIfDepthWouldBeExceededAsync(RosterEntry entry, RosterEntry main, CancellationToken ct)
    {
        // Depth 2 from below: the target is itself an alt.
        if (main.MainRosterEntryId is not null)
        {
            throw new AltDepthException(AltDepthReason.TargetIsAlreadyAnAlt);
        }

        // Depth 2 from above: this entry is already somebody's main. The two together foreclose cycles
        // — every cycle needs a node that is both a main and an alt.
        if (await dataLayer.HasAltsAsync(entry.Id, ct))
        {
            throw new AltDepthException(AltDepthReason.EntryAlreadyHasAlts);
        }
    }

    public async Task<bool> UnlinkAltAsync(Guid rosterEntryId, CancellationToken ct)
    {
        var userId = RequireUserId();
        var entry = await dataLayer.FindEntityAsync(rosterEntryId, ct);

        if (entry is null)
        {
            return false;
        }

        var callerClaimsEntry = await dataLayer.IsClaimedByAsync(entry.CharacterId, userId, ct);

        // Only the entry being detached, deliberately not both ends. A member whose character an
        // officer attached to someone else's main must be able to detach it again; requiring them to
        // claim that main too would trap them in a link they never made.
        if (!callerClaimsEntry && !await IsOfficerAsync(userId, ct))
        {
            throw new AltLinkNotPermittedException();
        }

        // Already detached is a satisfied intent, and writes no audit row — nothing happened.
        if (entry.MainRosterEntryId is null)
        {
            return true;
        }

        var previousMainId = entry.MainRosterEntryId;

        await dataLayer.UnlinkAltAsync(
            entry.Id,
            await AuditIfActingOnSomebodyElseAsync(
                entry.CharacterId,
                entry.Id,
                userId,
                AuditAction.RosterEntryAltUnlinked,
                DescribeMain(previousMainId),
                DescribeMain(null),
                ct),
            ct);

        return true;
    }

    // An audit row only when the actor does not claim the entry — which is exactly "an officer reached
    // into someone else's data". A member managing their own characters writes nothing, and so does an
    // officer managing theirs. One read serves both halves: the claimant is the test and the subject.
    private async Task<AuditLog?> AuditIfActingOnSomebodyElseAsync(
        Guid characterId,
        Guid rosterEntryId,
        string actorUserId,
        AuditAction action,
        string before,
        string after,
        CancellationToken ct)
    {
        var claimantUserId = await dataLayer.FindClaimantAsync(characterId, ct);

        if (claimantUserId == actorUserId)
        {
            return null;
        }

        return new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorUserId,
            // Null for an unclaimed entry — the action still happened and is still worth recording, it
            // just has no subject to name.
            SubjectUserId = claimantUserId,
            Action = action,
            TargetType = nameof(RosterEntry),
            TargetId = rosterEntryId,
            Before = before,
            After = after,
            OccurredAt = timeProvider.GetUtcNow(),
        };
    }

    private static string DescribeMain(Guid? mainId) => mainId is null ? "No main" : $"Alt of {mainId}";

    private async Task<bool> IsOfficerAsync(string userId, CancellationToken ct) =>
        await dataLayer.GetTenantRoleAsync(tenantContext.TenantId, userId, ct) >= TenantRole.Officer;

    private string RequireUserId() =>
        currentUser.UserId ?? throw new AuthenticationRequiredException();
}
