using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;

namespace AegisScribe.Domain.Facade;

public interface ISyncFacade
{
    // The Owner-facing budget read.
    Task<SyncBudgetServiceModel> GetBudgetAsync(Guid tenantId, CancellationToken ct);

    // Officer-triggered refresh of one character, drawing on this community's budget. Throws
    // SyncBudgetExhaustedException when the budget is spent, which the API turns into a 429 with
    // Retry-After.
    Task<CharacterDetailServiceModel?> RefreshCharacterAsync(Guid tenantId, CharacterLookupViewModel viewModel, CancellationToken ct);
}
