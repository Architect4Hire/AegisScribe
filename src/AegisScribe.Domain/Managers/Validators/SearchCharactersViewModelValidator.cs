using AegisScribe.Domain.Managers.Models.ViewModels;
using FluentValidation;

namespace AegisScribe.Domain.Managers.Validators;

public class SearchCharactersViewModelValidator : AbstractValidator<SearchCharactersViewModel>
{
    public SearchCharactersViewModelValidator()
    {
        RuleFor(x => x.Region).RegionFormat();
        RuleFor(x => x.RealmSlug).RealmSlugFormat().When(x => x.RealmSlug is not null);
        RuleFor(x => x.Name).MaximumLength(64);
        RuleFor(x => x.AfterNameLower).MaximumLength(64);
        // The server-enforced max api-contract.md requires — an unbounded limit is a DoS endpoint
        // with extra steps.
        RuleFor(x => x.Limit).InclusiveBetween(1, 100);
    }
}
