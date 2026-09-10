using AegisScribe.ApiService.Managers.Models.ViewModels;
using FluentValidation;

namespace AegisScribe.ApiService.Managers.Validators;

public class ChangePasswordViewModelValidator : AbstractValidator<ChangePasswordViewModel>
{
    public ChangePasswordViewModelValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty();
    }
}
