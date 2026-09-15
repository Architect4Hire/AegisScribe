using AegisScribe.Domain.Managers.Models.ViewModels;
using FluentValidation;

namespace AegisScribe.Domain.Managers.Validators;

public class SetOfficerNoteViewModelValidator : AbstractValidator<SetOfficerNoteViewModel>
{
    public SetOfficerNoteViewModelValidator()
    {
        // Mirrors RosterEntry.OfficerNote's HasMaxLength(1000). Null is valid and means "clear it".
        RuleFor(x => x.OfficerNote).MaximumLength(1000);
    }
}
