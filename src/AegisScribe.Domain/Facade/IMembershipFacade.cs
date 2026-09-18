using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;

namespace AegisScribe.Domain.Facade;

public interface IMembershipFacade
{
    Task<IReadOnlyList<TenantMemberServiceModel>> ListMembersAsync(
        DateTimeOffset? afterJoinedAt, string? afterId, int take, CancellationToken ct);

    Task<bool> SetRoleAsync(string userId, SetMemberRoleViewModel viewModel, CancellationToken ct);

    Task<bool> RemoveMemberAsync(string userId, CancellationToken ct);

    Task<bool> LeaveAsync(CancellationToken ct);

    Task<IReadOnlyList<InvitationServiceModel>> ListInvitationsAsync(CancellationToken ct);

    Task<CreatedInvitationServiceModel> CreateInvitationAsync(
        CreateInvitationViewModel viewModel, CancellationToken ct);

    Task<bool> RevokeInvitationAsync(Guid invitationId, CancellationToken ct);

    Task<InvitationPreviewServiceModel> PreviewInvitationAsync(string token, CancellationToken ct);

    Task<InvitationAcceptedServiceModel> AcceptInvitationAsync(string token, CancellationToken ct);

    Task<IReadOnlyList<JoinRequestServiceModel>> ListJoinRequestsAsync(
        JoinRequestStatus? status, CancellationToken ct);

    Task<bool> ApproveJoinRequestAsync(Guid requestId, SetMemberRoleViewModel viewModel, CancellationToken ct);

    Task<bool> DeclineJoinRequestAsync(Guid requestId, CancellationToken ct);

    Task<bool> RequestToJoinAsync(string slug, CreateJoinRequestViewModel viewModel, CancellationToken ct);

    Task SetJoinPolicyAsync(SetJoinPolicyViewModel viewModel, CancellationToken ct);
}
