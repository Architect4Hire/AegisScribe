using AegisScribe.ApiService.Managers.Models.ViewModels;
using FluentValidation;

namespace AegisScribe.ApiService.Managers.Validators;

public class RegisterViewModelValidator : AbstractValidator<RegisterViewModel>
{
    public RegisterViewModelValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
        RuleFor(x => x.DisplayName).MaximumLength(64);
    }
}
