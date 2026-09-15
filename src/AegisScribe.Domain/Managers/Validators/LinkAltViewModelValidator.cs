using AegisScribe.Domain.Managers.Models.ViewModels;
using FluentValidation;

namespace AegisScribe.Domain.Managers.Validators;

public class LinkAltViewModelValidator : AbstractValidator<LinkAltViewModel>
{
    public LinkAltViewModelValidator()
    {
        // Shape only. Whether the target exists, whether it is already an alt, and whether the caller
        // may touch either end all need the database, so they are Business's (add-endpoint skill).
        RuleFor(x => x.MainRosterEntryId).NotEmpty();
    }
}
