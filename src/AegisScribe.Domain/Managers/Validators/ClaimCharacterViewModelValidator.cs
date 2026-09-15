using AegisScribe.Domain.Managers.Models.ViewModels;
using FluentValidation;

namespace AegisScribe.Domain.Managers.Validators;

public class ClaimCharacterViewModelValidator : AbstractValidator<ClaimCharacterViewModel>
{
    public ClaimCharacterViewModelValidator()
    {
        // Shape only. "Does this character exist" needs the database and so is Business's, where it
        // becomes a 404 rather than a 400 — and "is it already claimed" is Business's too, for the
        // same reason (add-endpoint skill).
        RuleFor(x => x.CharacterId).NotEmpty();
    }
}
