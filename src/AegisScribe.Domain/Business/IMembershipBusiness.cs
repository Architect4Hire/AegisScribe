using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;

namespace AegisScribe.Domain.Business;

public interface IMembershipBusiness
{
    Task<IReadOnlyList<TenantMemberServiceModel>> ListMembersAsync(
        DateTimeOffset? afterJoinedAt, string? afterId, int take, CancellationToken ct);

    // False when there is no such member here — a 404. Throws when the actor may not act on them, may
    // not grant that role, or when it would leave the community with no Owner.
    Task<bool> SetRoleAsync(string userId, SetMemberRoleViewModel viewModel, CancellationToken ct);

    Task<bool> RemoveMemberAsync(string userId, CancellationToken ct);

    /// <summary>
    /// The caller removes themselves from this community. Throws when they are its last Owner.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="RemoveMemberAsync"/> because the rank arithmetic does not apply: you
    /// may always act on yourself for this. Without it an Officer is trapped — they outrank nobody who
    /// could remove them except an Owner, so walking away would need somebody else's cooperation.
    /// The last-owner rule still binds, and is the only thing that can refuse this.
    /// </remarks>
    Task<bool> LeaveAsync(CancellationToken ct);

    Task<IReadOnlyList<InvitationServiceModel>> ListInvitationsAsync(CancellationToken ct);

    // The only time a token is ever returned. Throws when the actor would be granting a role above
    // their own.
    Task<CreatedInvitationServiceModel> CreateInvitationAsync(
        CreateInvitationViewModel viewModel, CancellationToken ct);

    // False when there is no such invitation here, or it was already accepted or revoked.
    Task<bool> RevokeInvitationAsync(Guid invitationId, CancellationToken ct);

    /// <summary>
    /// What a live token is worth telling its holder. Anonymous, and tenant-less.
    /// </summary>
    /// <remarks>
    /// Throws <see cref="InvitationNotFoundException"/> or <see cref="InvitationUnusableException"/>
    /// for a token that is not live — and neither of those carries the community's name.
    /// </remarks>
    Task<InvitationPreviewServiceModel> PreviewInvitationAsync(string token, CancellationToken ct);

    /// <summary>
    /// Spends a token and makes the caller a member. Tenant-less: the token is the only thing that
    /// names the community.
    /// </summary>
    /// <remarks>
    /// Idempotent for somebody already in: no second row, no role change in either direction, and the
    /// token is not spent — so an officer can hand it to somebody else.
    /// </remarks>
    Task<InvitationAcceptedServiceModel> AcceptInvitationAsync(string token, CancellationToken ct);

    Task<IReadOnlyList<JoinRequestServiceModel>> ListJoinRequestsAsync(
        JoinRequestStatus? status, CancellationToken ct);

    Task<bool> ApproveJoinRequestAsync(Guid requestId, SetMemberRoleViewModel viewModel, CancellationToken ct);

    Task<bool> DeclineJoinRequestAsync(Guid requestId, CancellationToken ct);

    /// <summary>
    /// A non-member asks to join the community named by <paramref name="slug"/>. False when there is
    /// no such community, or its door is shut.
    /// </summary>
    /// <remarks>
    /// Tenant-less: the caller has no membership, so no tenant was resolved for this request and the
    /// SLUG is the only thing naming a community. The id it resolves to never comes from the client.
    /// </remarks>
    Task<bool> RequestToJoinAsync(string slug, CreateJoinRequestViewModel viewModel, CancellationToken ct);

    Task SetJoinPolicyAsync(SetJoinPolicyViewModel viewModel, CancellationToken ct);
}
