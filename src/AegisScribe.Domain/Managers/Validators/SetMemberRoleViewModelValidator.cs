using AegisScribe.Domain.Managers.Models.ViewModels;
using FluentValidation;

namespace AegisScribe.Domain.Managers.Validators;

public class SetMemberRoleViewModelValidator : AbstractValidator<SetMemberRoleViewModel>
{
    public SetMemberRoleViewModelValidator()
    {
        // IsInEnum rather than a range: TenantRole's values are 0/10/20, so an unmapped integer would
        // otherwise sail through and compare as a rank nobody holds.
        RuleFor(x => x.Role).IsInEnum();
    }
}
