using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Business;

public interface IMeBusiness
{
    Task<UserServiceModel> GetAsync(CancellationToken ct);

    WhoAmIServiceModel WhoAmI();
}
