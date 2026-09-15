using AegisScribe.Domain.Business;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using FluentValidation;

namespace AegisScribe.Domain.Facade;

// Not cached, unlike CharacterFacade's detail read, and deliberately so on both methods: a budget
// served from a five-minute cache would tell an Owner they have calls left seconds after they ran out,
// and a refresh is a write.
public class SyncFacade(
    ITenantSyncBudget budget,
    ISyncBusiness business,
    IValidator<CharacterLookupViewModel> lookupValidator) : ISyncFacade
{
    public Task<SyncBudgetServiceModel> GetBudgetAsync(Guid tenantId, CancellationToken ct) =>
        budget.GetAsync(tenantId, ct);

    public async Task<CharacterDetailServiceModel?> RefreshCharacterAsync(
        Guid tenantId, CharacterLookupViewModel viewModel, CancellationToken ct)
    {
        // Validated before any budget is spent — a malformed realm slug should not cost a community
        // two calls it could have spent on a real character.
        await lookupValidator.ValidateAndThrowAsync(viewModel, ct);

        return await business.RefreshCharacterAsync(tenantId, viewModel.Region, viewModel.RealmSlug, viewModel.Name, ct);
    }
}
