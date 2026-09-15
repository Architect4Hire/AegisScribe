using AegisScribe.Domain.Managers.Models.ViewModels;
using FluentValidation;

namespace AegisScribe.Domain.Managers.Validators;

public class AddRosterEntryViewModelValidator : AbstractValidator<AddRosterEntryViewModel>
{
    public AddRosterEntryViewModelValidator()
    {
        // Shape only. Whether the character exists, whether it is already rostered, and whether the
        // rank is this community's all need the database and so are Business's.
        RuleFor(x => x.CharacterId).NotEmpty();
        RuleFor(x => x.TenantRankId).NotEmpty().When(x => x.TenantRankId.HasValue);
    }
}
