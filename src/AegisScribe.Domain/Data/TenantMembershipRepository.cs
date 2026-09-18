using AegisScribe.Domain.Managers.Mappers;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Domain.Data;

public class TenantMembershipRepository(AegisScribeDbContext db) : ITenantMembershipRepository
{
    public async Task<IReadOnlyList<TenantMemberServiceModel>> ListAsync(
        Guid tenantId, DateTimeOffset? afterJoinedAt, string? afterId, int take, CancellationToken ct)
    {
        var members = db.TenantMemberships.AsNoTracking().Where(m => m.TenantId == tenantId);

        // Newest first, tie-broken by user id so the order is total and the keyset can resume. Never
        // offset: a member list paged by offset skips and duplicates rows while somebody is added
        // (api-contract.md).
        if (afterJoinedAt is not null && afterId is not null)
        {
            members = members.Where(m =>
                m.JoinedAt < afterJoinedAt.Value
                || (m.JoinedAt == afterJoinedAt.Value && string.Compare(m.UserId, afterId) > 0));
        }

        var page = await members
            .OrderByDescending(m => m.JoinedAt)
            .ThenBy(m => m.UserId)
            .Take(take)
            .Join(db.Users, m => m.UserId, user => user.Id, (m, user) => new TenantMemberServiceModel
            {
                UserId = m.UserId,
                // DisplayName only. A fellow member's email is not theirs to see — the same line the
                // roster projection draws.
                DisplayName = user.DisplayName,
                Role = m.Role,
                JoinedAt = m.JoinedAt,
            })
            .ToListAsync(ct);

        await ApplyClaimsAsync(tenantId, page, ct);

        return page;
    }

    // What each member on THIS page has claimed. A second bounded query rather than a join into the
    // projection above, because one member may hold several claims and a join would multiply the page
    // rows before Take() could mean anything.
    //
    // CharacterClaim is ITenantScoped, so the global query filter already confines this to the resolved
    // tenant — the explicit TenantId predicate is deliberate anyway: it is the discipline every
    // TenantMembership query in this file follows, and it is what puts the read on the
    // (TenantId, UserId) index rather than leaving it to the filter's rewrite.
    private async Task ApplyClaimsAsync(
        Guid tenantId, List<TenantMemberServiceModel> page, CancellationToken ct)
    {
        if (page.Count == 0)
        {
            return;
        }

        var userIds = page.Select(member => member.UserId).ToList();

        var claims = await db.CharacterClaims
            .AsNoTracking()
            .Where(claim => claim.TenantId == tenantId && userIds.Contains(claim.UserId))
            .Select(claim => new
            {
                claim.UserId,
                Model = new MemberClaimedCharacterServiceModel
                {
                    CharacterId = claim.CharacterId,
                    Name = claim.Character.Name,
                    RealmSlug = claim.Character.Realm.Slug,
                    Region = claim.Character.Realm.Region,
                    Class = claim.Character.Class,
                },
            })
            .ToListAsync(ct);

        // The class→colour mapping lives once, in CharacterMappers, and a C# switch cannot run inside a
        // SQL projection — so it runs out here rather than becoming a second copy. Same reason
        // RosterEntryRepository.ApplyDerivedAsync exists.
        foreach (var claim in claims)
        {
            claim.Model.ClassColor = CharacterMappers.ClassColorHex(claim.Model.Class);
        }

        var byUser = claims
            .GroupBy(claim => claim.UserId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<MemberClaimedCharacterServiceModel>)
                    [.. group.Select(claim => claim.Model).OrderBy(model => model.Name)]);

        foreach (var member in page)
        {
            // Left as the model's empty default when they have claimed nothing — never null.
            if (byUser.TryGetValue(member.UserId, out var claimed))
            {
                member.ClaimedCharacters = claimed;
            }
        }
    }

    // Tenant named explicitly, like every other method here: TenantMembership has no query filter to
    // fall back on, so a dropped parameter would count the whole platform's members.
    public Task<int> CountAsync(Guid tenantId, CancellationToken ct) =>
        db.TenantMemberships.CountAsync(m => m.TenantId == tenantId, ct);

    public Task<TenantMembership?> FindAsync(Guid tenantId, string userId, CancellationToken ct) =>
        db.TenantMemberships
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == userId, ct);

    public async Task<bool> TrySetRoleAsync(
        Guid tenantId, string userId, TenantRole role, CancellationToken ct)
    {
        // Queue behind any other membership change in this community, then decide. The last-owner rule
        // reads OTHER rows, so the WHERE below cannot serialise it on its own — TenantLocks explains
        // why, and a reproducible "community left with no owner" is what taught it.
        await db.LockCommunityAsync(tenantId, ct);

        // One UPDATE, no prior SELECT deciding anything. The last-owner rule is the third clause: this
        // row may stop being an Owner only while some OTHER owner exists. Promotions and sideways
        // moves between non-owner roles pass it trivially, because they are not demotions of an owner.
        var affected = await db.TenantMemberships
            .Where(m => m.TenantId == tenantId
                && m.UserId == userId
                && (role == TenantRole.Owner
                    || m.Role != TenantRole.Owner
                    || db.TenantMemberships.Any(other =>
                        other.TenantId == tenantId
                        && other.Role == TenantRole.Owner
                        && other.UserId != userId)))
            .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.Role, role), ct);

        return affected > 0;
    }

    public async Task<bool> TryRemoveAsync(Guid tenantId, string userId, CancellationToken ct)
    {
        // Same guard, same reason, and the same serialisation in front of it: a removal and a demotion
        // racing is the same hole as two demotions racing, and leaving either one out reopens it.
        await db.LockCommunityAsync(tenantId, ct);

        var affected = await db.TenantMemberships
            .Where(m => m.TenantId == tenantId
                && m.UserId == userId
                && (m.Role != TenantRole.Owner
                    || db.TenantMemberships.Any(other =>
                        other.TenantId == tenantId
                        && other.Role == TenantRole.Owner
                        && other.UserId != userId)))
            .ExecuteDeleteAsync(ct);

        return affected > 0;
    }

    public Task AddAsync(TenantMembership membership, CancellationToken ct)
    {
        db.TenantMemberships.Add(membership);

        return Task.CompletedTask;
    }

    // TenantMembership's PRIMARY KEY is (TenantId, UserId), so "a person belongs to a community at
    // most once" is not a rule anybody has to remember — it is unrepresentable. This translation is
    // what turns the database winning that race into an answer rather than a 500.
    //
    // It matters most on the invitation-accept path, where two doors into one community can be open
    // at once: a pending join request and a live invitation. Whichever loses lands here.
    private const string MembershipKeyName = "PK_TenantMemberships";

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation, CancellationToken ct)
    {
        try
        {
            return await db.ExecuteInTransactionAsync(operation, ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolationOn(MembershipKeyName))
        {
            // Not a transient failure, so the execution strategy correctly does not retry it — and
            // the whole transaction rolls back, which on the accept path means the invitation it had
            // just consumed goes back to being live.
            throw new AlreadyAMemberException();
        }
    }
}
