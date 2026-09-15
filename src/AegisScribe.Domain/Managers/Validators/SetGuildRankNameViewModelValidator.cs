using AegisScribe.Domain.Managers.Models.ViewModels;
using FluentValidation;

namespace AegisScribe.Domain.Managers.Validators;

public class SetGuildRankNameViewModelValidator : AbstractValidator<SetGuildRankNameViewModel>
{
    public SetGuildRankNameViewModelValidator()
    {
        // Mirrors GuildRankName.Name's HasMaxLength(32) — a rank label sits in a narrow table cell.
        // Null is valid and clears the name, which is how an officer undoes a mistake without the row
        // lingering as an empty string that renders as a blank label rather than a bare number.
        RuleFor(x => x.Name).MaximumLength(32);
    }
}
