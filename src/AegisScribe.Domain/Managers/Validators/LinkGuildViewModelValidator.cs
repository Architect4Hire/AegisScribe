using AegisScribe.Domain.Managers.Models.ViewModels;
using FluentValidation;

namespace AegisScribe.Domain.Managers.Validators;

public class LinkGuildViewModelValidator : AbstractValidator<LinkGuildViewModel>
{
    public LinkGuildViewModelValidator()
    {
        RuleFor(x => x.Region).RegionFormat();
        RuleFor(x => x.RealmSlug).NotEmpty().RealmSlugFormat();

        // Validated before the budget is charged, so a typo does not cost a community a Blizzard call.
        // 24 is the in-game limit on a guild name.
        RuleFor(x => x.GuildName).NotEmpty().MaximumLength(24);
    }
}
