using AegisScribe.Domain.Business;
using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Facade;

// No ViewModel to validate and nothing cached yet — /me gains memberships in 2.7, and caching
// arrives with the cache-key convention in 2.8.
public class MeFacade(IMeBusiness business) : IMeFacade
{
    public Task<UserServiceModel> GetAsync(CancellationToken ct) => business.GetAsync(ct);

    public WhoAmIServiceModel WhoAmI() => business.WhoAmI();
}
