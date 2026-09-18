using AegisScribe.Domain.Managers.Models.ViewModels;
using FluentValidation;

namespace AegisScribe.Domain.Managers.Validators;

public class CreateInvitationViewModelValidator : AbstractValidator<CreateInvitationViewModel>
{
    // A link that outlives the reason it was made is a standing key to the community, so there is a
    // ceiling and it is not configurable per request beyond this.
    public const int MaxExpiryDays = 30;

    public CreateInvitationViewModelValidator()
    {
        // Shape only. Whether the ACTOR may grant this role needs their membership, so it is Business's.
        RuleFor(x => x.Role).IsInEnum();
        RuleFor(x => x.Note).MaximumLength(100);
        RuleFor(x => x.ExpiresInDays).InclusiveBetween(1, MaxExpiryDays).When(x => x.ExpiresInDays.HasValue);
    }
}
