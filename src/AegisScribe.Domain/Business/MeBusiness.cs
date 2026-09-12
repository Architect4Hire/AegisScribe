using AegisScribe.Domain.Context;
using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Mappers;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Business;

public class MeBusiness(IUserDataLayer dataLayer, ITenantDataLayer tenantDataLayer, ICurrentUser currentUser) : IMeBusiness
{
    // A valid token for a user that no longer exists — or a client-credentials token, which names no
    // Identity user at all — is an unauthenticated caller as far as /me is concerned.
    public async Task<UserServiceModel> GetAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new AuthenticationRequiredException();
        var user = await dataLayer.FindByIdAsync(userId, ct) ?? throw new AuthenticationRequiredException();
        var memberships = await tenantDataLayer.GetMembershipsForUserAsync(userId, ct);
        return user.ToServiceModel(memberships);
    }

    public WhoAmIServiceModel WhoAmI() => new()
    {
        Sub = currentUser.Subject,
        IsPlatformAdmin = currentUser.IsPlatformAdmin,
    };
}
