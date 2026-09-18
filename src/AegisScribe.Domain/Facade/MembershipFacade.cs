using AegisScribe.Domain.Business;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using FluentValidation;

namespace AegisScribe.Domain.Facade;

// Not cached, and that is deliberate rather than an omission. Every read here is small, every one of
// them changes the moment an officer clicks something on the same screen, and a member list that
// disagrees with the role you just changed is worse than the read it saved.
//
// What the facade owes instead is the input discipline: the role is a closed whitelist, the note and
// message are bounded, and the invitation's lifetime has a ceiling.
public class MembershipFacade(
    IMembershipBusiness business,
    IValidator<CreateInvitationViewModel> invitationValidator,
    IValidator<SetMemberRoleViewModel> roleValidator,
    IValidator<CreateJoinRequestViewModel> joinRequestValidator) : IMembershipFacade
{
    public Task<IReadOnlyList<TenantMemberServiceModel>> ListMembersAsync(
        DateTimeOffset? afterJoinedAt, string? afterId, int take, CancellationToken ct) =>
        business.ListMembersAsync(afterJoinedAt, afterId, take, ct);

    public async Task<bool> SetRoleAsync(string userId, SetMemberRoleViewModel viewModel, CancellationToken ct)
    {
        await roleValidator.ValidateAndThrowAsync(viewModel, ct);

        return await business.SetRoleAsync(userId, viewModel, ct);
    }

    public Task<bool> RemoveMemberAsync(string userId, CancellationToken ct) =>
        business.RemoveMemberAsync(userId, ct);

    public Task<bool> LeaveAsync(CancellationToken ct) => business.LeaveAsync(ct);

    public Task<IReadOnlyList<InvitationServiceModel>> ListInvitationsAsync(CancellationToken ct) =>
        business.ListInvitationsAsync(ct);

    public async Task<CreatedInvitationServiceModel> CreateInvitationAsync(
        CreateInvitationViewModel viewModel, CancellationToken ct)
    {
        await invitationValidator.ValidateAndThrowAsync(viewModel, ct);

        return await business.CreateInvitationAsync(viewModel, ct);
    }

    public Task<bool> RevokeInvitationAsync(Guid invitationId, CancellationToken ct) =>
        business.RevokeInvitationAsync(invitationId, ct);

    // No validator: the token is a 256-bit opaque string and the only question worth asking about it
    // is whether a row matches its hash, which is the database's to answer. A length or charset rule
    // here would be a second, weaker copy of that check — and one that tells a prober which guesses
    // are even worth making.
    public Task<InvitationPreviewServiceModel> PreviewInvitationAsync(string token, CancellationToken ct) =>
        business.PreviewInvitationAsync(token, ct);

    public Task<InvitationAcceptedServiceModel> AcceptInvitationAsync(string token, CancellationToken ct) =>
        business.AcceptInvitationAsync(token, ct);

    public Task<IReadOnlyList<JoinRequestServiceModel>> ListJoinRequestsAsync(
        JoinRequestStatus? status, CancellationToken ct) =>
        business.ListJoinRequestsAsync(status, ct);

    public async Task<bool> ApproveJoinRequestAsync(
        Guid requestId, SetMemberRoleViewModel viewModel, CancellationToken ct)
    {
        await roleValidator.ValidateAndThrowAsync(viewModel, ct);

        return await business.ApproveJoinRequestAsync(requestId, viewModel, ct);
    }

    public Task<bool> DeclineJoinRequestAsync(Guid requestId, CancellationToken ct) =>
        business.DeclineJoinRequestAsync(requestId, ct);

    public async Task<bool> RequestToJoinAsync(
        string slug, CreateJoinRequestViewModel viewModel, CancellationToken ct)
    {
        await joinRequestValidator.ValidateAndThrowAsync(viewModel, ct);

        return await business.RequestToJoinAsync(slug, viewModel, ct);
    }

    public Task SetJoinPolicyAsync(SetJoinPolicyViewModel viewModel, CancellationToken ct) =>
        business.SetJoinPolicyAsync(viewModel, ct);
}
