using AegisScribe.Domain.Managers.Models.ViewModels;
using FluentValidation;

namespace AegisScribe.Domain.Managers.Validators;

public class SetRosterRankViewModelValidator : AbstractValidator<SetRosterRankViewModel>
{
    public SetRosterRankViewModelValidator()
    {
        // Null is valid and means "clear the rank". An explicitly empty GUID is not — that is a client
        // that meant to send something and sent nothing, which should not read as "clear".
        RuleFor(x => x.TenantRankId).NotEmpty().When(x => x.TenantRankId.HasValue);
    }
}
