using AegisScribe.Domain.Context;
using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using AegisScribe.Domain.Managers.Validators;
using Microsoft.Extensions.Logging;

namespace AegisScribe.Domain.Business;

// The membership lifecycle's rules: who may act on whom, what they may grant, and the one invariant
// that outranks all of it — a community always has an Owner.
//
// The route's policy has already answered "are you at least an officer here". Everything below needs
// to READ something before it can decide, which is exactly what makes it Business's and not a
// policy's (auth.md).
// The concrete TenantContext rather than the read-only ITenantContext, and this is the ONLY class
// below the middleware that takes it. Accepting an invitation is tenant resolution by token — the
// caller is not a member yet, so nothing upstream could have resolved one — and the write that
// follows needs an ambient tenant for the interceptor to stamp an audit row from. See
// ResolveLiveTokenAsync; every other method here only reads TenantId, exactly as before.
public class MembershipBusiness(
    IMembershipDataLayer dataLayer,
    ICurrentUser currentUser,
    TenantContext tenantContext,
    TimeProvider timeProvider,
    ILogger<MembershipBusiness> logger) : IMembershipBusiness
{
    private const int DefaultExpiryDays = 7;

    // Bounded like every other list on the wire (api-contract.md). A community's membership is small
    // enough that this is one page in practice and a cursor for the rare case.
    private const int MaxPageSize = 100;

    public Task<IReadOnlyList<TenantMemberServiceModel>> ListMembersAsync(
        DateTimeOffset? afterJoinedAt, string? afterId, int take, CancellationToken ct) =>
        dataLayer.ListMembersAsync(
            tenantContext.TenantId, afterJoinedAt, afterId, Math.Clamp(take, 1, MaxPageSize), ct);

    // ---- the two predicates every refusal below comes from ----

    /// <summary>
    /// Whether <paramref name="actor"/> may act on somebody holding <paramref name="target"/>.
    /// </summary>
    /// <remarks>
    /// Strictly above, or an Owner. That single line yields every rule auth.md states: an Officer may
    /// act on a Member but not on another Officer, only an Owner may demote an Officer — and, by the
    /// same arithmetic, only an Owner may REMOVE one, which auth.md does not spell out but which
    /// follows, since removing somebody is strictly worse than demoting them.
    /// </remarks>
    private static bool MayActOn(TenantRole actor, TenantRole target) =>
        actor > target || actor == TenantRole.Owner;

    // You cannot hand out what you do not hold. This is what makes Owner mintable only by an Owner.
    private static bool MayGrant(TenantRole actor, TenantRole role) => role <= actor;

    public async Task<bool> SetRoleAsync(string userId, SetMemberRoleViewModel viewModel, CancellationToken ct)
    {
        var actorUserId = RequireUserId();
        var actorRole = await RequireActorRoleAsync(actorUserId, ct);

        var target = await dataLayer.FindMembershipAsync(tenantContext.TenantId, userId, ct);

        // Not a member here. 404 — and a member of ANOTHER community is indistinguishable from this,
        // correctly, because the lookup names our tenant.
        if (target is null)
        {
            return false;
        }

        if (!MayActOn(actorRole, target.Role))
        {
            throw MembershipActionNotPermittedException.OutranksActor();
        }

        if (!MayGrant(actorRole, viewModel.Role))
        {
            throw MembershipActionNotPermittedException.RoleAboveActor();
        }

        var changed = await dataLayer.TrySetRoleAsync(
            tenantContext.TenantId,
            userId,
            viewModel.Role,
            Audit(actorUserId, userId, AuditAction.MemberRoleChanged, target.Role.ToString(), viewModel.Role.ToString()),
            ct);

        // The checks above passed and the write still refused, so the only clause left is the one this
        // request could not see: the target is the community's last Owner. Nothing changed.
        if (!changed)
        {
            throw new LastOwnerException();
        }

        return true;
    }

    public async Task<bool> RemoveMemberAsync(string userId, CancellationToken ct)
    {
        var actorUserId = RequireUserId();
        var actorRole = await RequireActorRoleAsync(actorUserId, ct);

        var target = await dataLayer.FindMembershipAsync(tenantContext.TenantId, userId, ct);

        if (target is null)
        {
            return false;
        }

        if (!MayActOn(actorRole, target.Role))
        {
            throw MembershipActionNotPermittedException.OutranksActor();
        }

        var removed = await dataLayer.TryRemoveMemberAsync(
            tenantContext.TenantId,
            userId,
            Audit(actorUserId, userId, AuditAction.MemberRemoved, target.Role.ToString(), "Removed"),
            ct);

        if (!removed)
        {
            throw new LastOwnerException();
        }

        return true;
    }

    public async Task<bool> LeaveAsync(CancellationToken ct)
    {
        var userId = RequireUserId();

        var membership = await dataLayer.FindMembershipAsync(tenantContext.TenantId, userId, ct);

        // Not reachable through the route — the resolution middleware already proved membership — but
        // a caller who left in a concurrent request lands here, and it is a 404 rather than a throw.
        if (membership is null)
        {
            return false;
        }

        // No MayActOn check, deliberately, and it is the one place that predicate is skipped: acting
        // on yourself is always permitted for this. An Officer outranks nobody who could remove them
        // but an Owner, so without this exception walking away needs somebody else to agree.
        var removed = await dataLayer.TryRemoveMemberAsync(
            tenantContext.TenantId,
            userId,
            // Actor and subject are the same person, which is exactly what makes this row worth
            // distinguishing from a removal.
            Audit(userId, userId, AuditAction.MemberLeft, membership.Role.ToString(), "Left"),
            ct);

        // The only rule that can refuse this. A community's last Owner cannot walk out of it — they
        // promote somebody first, which is the same answer demote and remove give.
        if (!removed)
        {
            throw new LastOwnerException();
        }

        return true;
    }

    // ---- invitations ----

    public async Task<IReadOnlyList<InvitationServiceModel>> ListInvitationsAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var rows = await dataLayer.ListInvitationsAsync(tenantContext.TenantId, MaxPageSize, ct);

        return [.. rows.Select(row => ToServiceModel(row, now))];
    }

    public async Task<CreatedInvitationServiceModel> CreateInvitationAsync(
        CreateInvitationViewModel viewModel, CancellationToken ct)
    {
        var actorUserId = RequireUserId();
        var actorRole = await RequireActorRoleAsync(actorUserId, ct);

        // An invitation is a role grant with a delay on it, so it is bounded by exactly what a direct
        // promotion is bounded by. Without this an Officer mints an Owner token and walks around the
        // rule by handing it to themselves.
        if (!MayGrant(actorRole, viewModel.Role))
        {
            throw MembershipActionNotPermittedException.RoleAboveActor();
        }

        var now = timeProvider.GetUtcNow();

        // The plaintext lives in this method and in the response. What is persisted is its hash.
        var token = InvitationTokens.Create();

        var invitation = new TenantInvitation
        {
            Id = Guid.NewGuid(),
            // Named explicitly: TenantInvitation is not ITenantScoped, so nothing stamps this.
            TenantId = tenantContext.TenantId,
            TokenHash = InvitationTokens.Hash(token),
            Role = viewModel.Role,
            Note = viewModel.Note,
            CreatedByUserId = actorUserId,
            CreatedAt = now,
            ExpiresAt = now.AddDays(
                Math.Clamp(
                    viewModel.ExpiresInDays ?? DefaultExpiryDays,
                    1,
                    CreateInvitationViewModelValidator.MaxExpiryDays)),
        };

        await dataLayer.CreateInvitationAsync(
            invitation,
            // No subject: nobody has accepted it yet, so there is no person this was done to. The role
            // it grants is the part worth recording.
            Audit(
                actorUserId,
                subjectUserId: null,
                AuditAction.MemberInvited,
                "No invitation",
                $"Invited as {viewModel.Role}",
                nameof(TenantInvitation),
                invitation.Id),
            ct);

        return new CreatedInvitationServiceModel
        {
            Invitation = ToServiceModel(invitation, now),
            Token = token,
        };
    }

    public async Task<bool> RevokeInvitationAsync(Guid invitationId, CancellationToken ct)
    {
        var actorUserId = RequireUserId();
        await RequireActorRoleAsync(actorUserId, ct);

        var invitation = await dataLayer.FindInvitationAsync(tenantContext.TenantId, invitationId, ct);

        if (invitation is null)
        {
            return false;
        }

        // Already accepted or revoked reads as "no longer there" rather than as a conflict: the
        // officer's intent — this link is dead — is satisfied either way, and DELETE is idempotent
        // (api-contract.md). An expired one is still revocable, deliberately.
        return await dataLayer.TryRevokeInvitationAsync(
            tenantContext.TenantId,
            invitationId,
            actorUserId,
            timeProvider.GetUtcNow(),
            Audit(
                actorUserId,
                subjectUserId: null,
                AuditAction.InvitationRevoked,
                $"Invitation as {invitation.Role}",
                "Revoked",
                nameof(TenantInvitation),
                invitationId),
            ct);
    }

    // ---- accepting an invitation (8.3b) ----

    public async Task<InvitationPreviewServiceModel> PreviewInvitationAsync(string token, CancellationToken ct)
    {
        var (invitation, tenant) = await ResolveLiveTokenAsync(token, ct);

        return new InvitationPreviewServiceModel
        {
            TenantSlug = tenant.Slug,
            TenantName = tenant.Name,
            Role = invitation.Role,
            ExpiresAt = invitation.ExpiresAt,
        };
    }

    public async Task<InvitationAcceptedServiceModel> AcceptInvitationAsync(string token, CancellationToken ct)
    {
        var userId = RequireUserId();
        var (invitation, tenant) = await ResolveLiveTokenAsync(token, ct);

        // Already in: a no-op that lands them in the community. No second row, no role change in
        // EITHER direction — an Owner who clicks a Member invitation stays an Owner — and the token is
        // deliberately not spent, so it is still worth something to whoever it was meant for.
        var existing = await dataLayer.FindMembershipAsync(invitation.TenantId, userId, ct);

        if (existing is not null)
        {
            return Accepted(tenant, existing.Role, alreadyAMember: true);
        }

        var now = timeProvider.GetUtcNow();

        var membership = new TenantMembership
        {
            TenantId = invitation.TenantId,
            UserId = userId,
            Role = invitation.Role,
            JoinedAt = now,
        };

        bool accepted;

        try
        {
            accepted = await dataLayer.TryAcceptInvitationAsync(
                InvitationTokens.Hash(token),
                membership,
                now,
                Audit(
                    userId,
                    userId,
                    AuditAction.InvitationAccepted,
                    "Not a member",
                    $"Joined as {invitation.Role}",
                    nameof(TenantInvitation),
                    invitation.Id),
                ct);
        }
        catch (AlreadyAMemberException)
        {
            // They joined by another door between the check above and the insert — a pending request
            // approved a moment ago, most likely. The primary key refused the duplicate and took the
            // whole transaction with it, so the token was NOT spent and is still live.
            var raced = await dataLayer.FindMembershipAsync(invitation.TenantId, userId, ct);

            return Accepted(tenant, raced?.Role ?? invitation.Role, alreadyAMember: true);
        }

        if (!accepted)
        {
            // Somebody spent, revoked or outlasted this token between the read above and the write.
            // Re-read to say WHICH — a rows-affected count cannot, and "it didn't work" is the one
            // answer that helps nobody.
            var fresh = await dataLayer.FindInvitationByTokenHashAsync(InvitationTokens.Hash(token), ct);

            throw new InvitationUnusableException(
                Classify(fresh, timeProvider.GetUtcNow()) ?? InvitationRefusal.Consumed);
        }

        return Accepted(tenant, invitation.Role, alreadyAMember: false);
    }

    /// <summary>
    /// The token, the community it names, and the refusal if it names nothing usable.
    /// </summary>
    /// <remarks>
    /// This is tenant RESOLUTION, by token instead of by slug — and where it succeeds it sets the
    /// ambient tenant, exactly as the resolution middleware does for a member arriving at
    /// /t/{slug}/... For a member, the proof is their TenantMembership row; here it is the token, and
    /// the invitation row is the authority on which community that is.
    ///
    /// It has to happen: AuditLog is ITenantScoped, so the stamping interceptor reads
    /// ITenantContext.TenantId on any write below this point, and on a tenant-less route there is
    /// nothing to read. tenancy.md already sanctions setting the ambient tenant deliberately outside
    /// middleware — it is what the sync worker's per-tenant jobs do.
    ///
    /// Scope creep is the risk, so: nothing is set for a token that does not resolve, and the only
    /// writes that follow are the membership this token grants and the rows recording it.
    /// </remarks>
    private async Task<(TenantInvitation Invitation, Tenant Tenant)> ResolveLiveTokenAsync(
        string token, CancellationToken ct)
    {
        var invitation = await dataLayer.FindInvitationByTokenHashAsync(InvitationTokens.Hash(token), ct);

        if (invitation is null)
        {
            // The one refusal that CANNOT be audited: AuditLog is tenant-scoped and a token matching
            // nothing names no community, so there is no log to write it to. A plain log line is the
            // honest substitute — somebody hammering this is worth seeing, and it is the signal the
            // rate limiter's 429s would otherwise be the only trace of.
            //
            // The token is deliberately absent from it. Logging the value to record that it was wrong
            // would put a live one here the day the lookup is wrong for another reason.
            logger.LogInformation(
                "An invitation token matching no invitation was presented by {UserId}.",
                currentUser.UserId ?? "an anonymous caller");

            throw new InvitationNotFoundException();
        }

        // An invitation whose community has gone reads as no invitation at all. Nothing to name, and
        // nothing a holder could do with the knowledge.
        var tenant = await dataLayer.FindTenantByIdAsync(invitation.TenantId, ct)
            ?? throw new InvitationNotFoundException();

        tenantContext.SetTenant(invitation.TenantId);

        var refusal = Classify(invitation, timeProvider.GetUtcNow());

        if (refusal is not null)
        {
            // Audited, because "somebody tried the link after I revoked it" is a question an officer
            // will ask. Only possible for a token that named a real community — a token that never
            // existed has no audit log to be written to, which is a limit of the model rather than an
            // omission.
            await dataLayer.RecordAsync(
                Audit(
                    currentUser.UserId ?? "anonymous",
                    subjectUserId: null,
                    AuditAction.InvitationRefused,
                    $"Invitation as {invitation.Role}",
                    refusal.Value.ToString(),
                    nameof(TenantInvitation),
                    invitation.Id),
                ct);

            throw new InvitationUnusableException(refusal.Value);
        }

        return (invitation, tenant);
    }

    // Null means live. Order matters: a revoked invitation that has also expired reads as revoked,
    // because that is the thing a human did.
    private static InvitationRefusal? Classify(TenantInvitation? invitation, DateTimeOffset now) =>
        invitation switch
        {
            null => InvitationRefusal.Consumed,
            { AcceptedAt: not null } => InvitationRefusal.Consumed,
            { RevokedAt: not null } => InvitationRefusal.Revoked,
            _ when invitation.ExpiresAt <= now => InvitationRefusal.Expired,
            _ => null,
        };

    private static InvitationAcceptedServiceModel Accepted(Tenant tenant, TenantRole role, bool alreadyAMember) =>
        new()
        {
            TenantSlug = tenant.Slug,
            TenantName = tenant.Name,
            Role = role,
            AlreadyAMember = alreadyAMember,
        };

    // ---- join requests ----

    public Task<IReadOnlyList<JoinRequestServiceModel>> ListJoinRequestsAsync(
        JoinRequestStatus? status, CancellationToken ct) =>
        dataLayer.ListJoinRequestsAsync(tenantContext.TenantId, status, MaxPageSize, ct);

    public async Task<bool> ApproveJoinRequestAsync(
        Guid requestId, SetMemberRoleViewModel viewModel, CancellationToken ct)
    {
        var actorUserId = RequireUserId();
        var actorRole = await RequireActorRoleAsync(actorUserId, ct);

        // Same ceiling as an invitation, and for the same reason: approving somebody straight in at
        // Owner is a role grant however it is spelled.
        if (!MayGrant(actorRole, viewModel.Role))
        {
            throw MembershipActionNotPermittedException.RoleAboveActor();
        }

        var request = await dataLayer.FindJoinRequestAsync(tenantContext.TenantId, requestId, ct);

        if (request is null)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();

        var membership = new TenantMembership
        {
            TenantId = tenantContext.TenantId,
            UserId = request.UserId,
            Role = viewModel.Role,
            JoinedAt = now,
        };

        // False when somebody else decided it first — the conditional update one layer down is what
        // makes two officers racing produce exactly one membership.
        return await dataLayer.TryApproveJoinRequestAsync(
            tenantContext.TenantId,
            requestId,
            membership,
            actorUserId,
            now,
            Audit(
                actorUserId,
                request.UserId,
                AuditAction.JoinRequestApproved,
                "Requested",
                $"Joined as {viewModel.Role}",
                nameof(TenantJoinRequest),
                requestId),
            ct);
    }

    public async Task<bool> DeclineJoinRequestAsync(Guid requestId, CancellationToken ct)
    {
        var actorUserId = RequireUserId();
        await RequireActorRoleAsync(actorUserId, ct);

        var request = await dataLayer.FindJoinRequestAsync(tenantContext.TenantId, requestId, ct);

        if (request is null)
        {
            return false;
        }

        return await dataLayer.TryDeclineJoinRequestAsync(
            tenantContext.TenantId,
            requestId,
            actorUserId,
            timeProvider.GetUtcNow(),
            Audit(
                actorUserId,
                request.UserId,
                AuditAction.JoinRequestDeclined,
                "Requested",
                "Declined",
                nameof(TenantJoinRequest),
                requestId),
            ct);
    }

    public async Task<bool> RequestToJoinAsync(
        string slug, CreateJoinRequestViewModel viewModel, CancellationToken ct)
    {
        var userId = RequireUserId();

        var tenant = await dataLayer.FindTenantBySlugAsync(slug, ct);

        // "No such community" and "that community does not take requests" are the SAME answer on
        // purpose. A distinguishable refusal turns this route into a directory of every community on
        // the platform, which is the enumeration oracle the slug-check endpoint also refuses to be.
        if (tenant is null || !tenant.AcceptsJoinRequests)
        {
            return false;
        }

        // Already in. Not an error and not a second row — the caller's intent is satisfied.
        if (await dataLayer.IsMemberAsync(tenant.Id, userId, ct))
        {
            return true;
        }

        // The readable answer; the filtered unique index is the authority when two submissions race.
        if (await dataLayer.HasPendingJoinRequestAsync(tenant.Id, userId, ct))
        {
            throw new JoinRequestAlreadyPendingException();
        }

        await dataLayer.CreateJoinRequestAsync(
            new TenantJoinRequest
            {
                Id = Guid.NewGuid(),
                // From the slug, never from the client, and never from ambient state — there is none,
                // because this caller is not a member of anything yet.
                TenantId = tenant.Id,
                UserId = userId,
                Message = viewModel.Message,
                RequestedAt = timeProvider.GetUtcNow(),
                Status = JoinRequestStatus.Pending,
            },
            ct);

        return true;
    }

    public Task SetJoinPolicyAsync(SetJoinPolicyViewModel viewModel, CancellationToken ct) =>
        dataLayer.SetJoinPolicyAsync(tenantContext.TenantId, viewModel.AcceptsJoinRequests, ct);

    // ---- shared ----

    // Derived, never stored. Order matters: a revoked invitation that has also expired reads as
    // revoked, because that is the thing a human did.
    private static InvitationServiceModel ToServiceModel(TenantInvitation invitation, DateTimeOffset now) =>
        new()
        {
            Id = invitation.Id,
            Role = invitation.Role,
            Note = invitation.Note,
            CreatedAt = invitation.CreatedAt,
            ExpiresAt = invitation.ExpiresAt,
            Status = invitation switch
            {
                { AcceptedAt: not null } => InvitationStatus.Accepted,
                { RevokedAt: not null } => InvitationStatus.Revoked,
                _ when invitation.ExpiresAt <= now => InvitationStatus.Expired,
                _ => InvitationStatus.Pending,
            },
        };

    // Every one of these writes a row, without the "only when acting on somebody else" test the roster
    // applies: there is no managing-your-own case in a membership change. Once officers can act on
    // other people's standing, "who did this" stops being optional (auth.md).
    private AuditLog Audit(
        string actorUserId,
        string? subjectUserId,
        AuditAction action,
        string before,
        string after,
        string targetType = nameof(TenantMembership),
        Guid? targetId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorUserId,
            SubjectUserId = subjectUserId,
            Action = action,
            // A membership has no surrogate key — its identity is (TenantId, UserId), and the tenant
            // is already implicit in a tenant-scoped audit row while the user is SubjectUserId. So
            // TargetId stays null for the two membership actions and names the row for the other four.
            TargetType = targetType,
            TargetId = targetId,
            Before = before,
            After = after,
            OccurredAt = timeProvider.GetUtcNow(),
        };

    // The actor's own standing, read rather than assumed. The policy guaranteed "at least Officer";
    // every rule here needs to know which.
    private async Task<TenantRole> RequireActorRoleAsync(string actorUserId, CancellationToken ct) =>
        (await dataLayer.FindMembershipAsync(tenantContext.TenantId, actorUserId, ct))?.Role
        ?? throw new AuthenticationRequiredException();

    private string RequireUserId() =>
        currentUser.UserId ?? throw new AuthenticationRequiredException();
}
