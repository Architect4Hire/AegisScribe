using AegisScribe.Domain.Managers.Models.ViewModels;
using FluentValidation;

namespace AegisScribe.Domain.Managers.Validators;

public class CharacterLookupViewModelValidator : AbstractValidator<CharacterLookupViewModel>
{
    public CharacterLookupViewModelValidator()
    {
        RuleFor(x => x.Region).RegionFormat();
        RuleFor(x => x.RealmSlug).NotEmpty().RealmSlugFormat();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(64);
    }
}
