using AegisScribe.Domain.Managers.Models.ViewModels;
using FluentValidation;

namespace AegisScribe.Domain.Managers.Validators;

public class CreateTenantViewModelValidator : AbstractValidator<CreateTenantViewModel>
{
    public CreateTenantViewModelValidator()
    {
        // Slug is optional (2.7b): omit it and Business derives one from Name via TenantSlugRules, so a
        // client — the MAUI app included — never has to reimplement the derivation. Relaxing a rule is
        // additive (api-contract.md); the shape rules below still apply to any slug that IS supplied.
        RuleFor(x => x.Slug).SlugFormat().When(x => x.Slug is not null);

        // The one case where omitting the slug is still a 400: a name that folds to nothing leaves
        // nothing to derive, and inventing a slug for someone is not on (TenantSlugRules.Derive).
        RuleFor(x => x.Slug)
            .Must((viewModel, _) => TenantSlugRules.Derive(viewModel.Name) is not null)
            .When(x => x.Slug is null)
            .WithMessage("'Name' contains no characters usable in a URL — supply a slug as well.");

        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TimeZoneId)
            .NotEmpty()
            .MaximumLength(64)
            .Must(id => TimeZoneInfo.TryFindSystemTimeZoneById(id, out _))
            .WithMessage("'{PropertyName}' is not a recognized time zone id.");
    }
}
