using AegisScribe.Domain.Managers.Models.ViewModels;
using FluentValidation;

namespace AegisScribe.Domain.Managers.Validators;

public class ImportGuildRosterViewModelValidator : AbstractValidator<ImportGuildRosterViewModel>
{
    public ImportGuildRosterViewModelValidator()
    {
        // Shape only. Whether THIS community follows that guild needs the database and so is
        // Business's — and it is a 404 there, not a validation error, because a guild another
        // community linked must not read as "exists but refused".
        RuleFor(x => x.GuildId).NotEmpty();
    }
}
