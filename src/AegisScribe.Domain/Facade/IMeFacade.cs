using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Facade;

public interface IMeFacade
{
    Task<UserServiceModel> GetAsync(CancellationToken ct);

    WhoAmIServiceModel WhoAmI();
}
