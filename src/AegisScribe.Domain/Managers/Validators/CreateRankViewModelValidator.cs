using AegisScribe.Domain.Managers.Models.ViewModels;
using FluentValidation;

namespace AegisScribe.Domain.Managers.Validators;

// Shape only. "Is this name already used in this community" needs the database, so it is a domain
// rule in TenantRankBusiness, not a validator (add-endpoint skill).
public class CreateRankViewModelValidator : AbstractValidator<CreateRankViewModel>
{
    public CreateRankViewModelValidator()
    {
        RuleFor(x => x.Name).RankName();
        RuleFor(x => x.Colour).RankColour();
        RuleFor(x => x.SortOrder).RankSortOrder();
    }
}
