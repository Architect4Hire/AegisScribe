using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;

namespace AegisScribe.Domain.Managers.Mappers;

public static class TenantRankMappers
{
    // Id is generated in Business, not read from the client. TenantId is absent on purpose — the
    // SaveChanges interceptor stamps it from the ambient context, and a mapper that assigned it would
    // be the exact line tenancy.md says is always the symptom of a missing tenant context.
    public static TenantRank ToEntity(this CreateRankViewModel viewModel, Guid id) => new()
    {
        Id = id,
        Name = viewModel.Name,
        SortOrder = viewModel.SortOrder,
        Colour = viewModel.Colour,
    };

    // Applied to a rank already loaded through the query filter, so it is this tenant's row by
    // construction. Everything a PUT replaces, and nothing else.
    public static void Apply(this UpdateRankViewModel viewModel, TenantRank rank)
    {
        rank.Name = viewModel.Name;
        rank.SortOrder = viewModel.SortOrder;
        rank.Colour = viewModel.Colour;
    }

    public static TenantRankServiceModel ToServiceModel(this TenantRank rank) => new()
    {
        Id = rank.Id,
        Name = rank.Name,
        SortOrder = rank.SortOrder,
        Colour = rank.Colour,
    };
}
