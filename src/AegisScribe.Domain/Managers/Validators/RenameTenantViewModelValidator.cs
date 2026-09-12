using AegisScribe.Domain.Managers.Models.ViewModels;
using FluentValidation;

namespace AegisScribe.Domain.Managers.Validators;

public class RenameTenantViewModelValidator : AbstractValidator<RenameTenantViewModel>
{
    public RenameTenantViewModelValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}
