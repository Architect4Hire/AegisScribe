using AegisScribe.Domain.Managers.Models.ViewModels;
using FluentValidation;

namespace AegisScribe.Domain.Managers.Validators;

public class CreateTenantViewModelValidator : AbstractValidator<CreateTenantViewModel>
{
    public CreateTenantViewModelValidator()
    {
        // Lengths mirror the HasMaxLength values in OnModelCreating so the validator and the DB
        // constraint never disagree. The slug is the route key (/api/v1/t/{slug}/...), so it's
        // restricted to what's safe unencoded in a URL segment.
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(64).Matches("^[a-z0-9]+(-[a-z0-9]+)*$");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TimeZoneId)
            .NotEmpty()
            .MaximumLength(64)
            .Must(id => TimeZoneInfo.TryFindSystemTimeZoneById(id, out _))
            .WithMessage("'{PropertyName}' is not a recognized time zone id.");
    }
}
