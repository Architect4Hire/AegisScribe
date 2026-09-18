using AegisScribe.Domain.Managers.Models.ViewModels;
using FluentValidation;

namespace AegisScribe.Domain.Managers.Validators;

public class CreateJoinRequestViewModelValidator : AbstractValidator<CreateJoinRequestViewModel>
{
    public CreateJoinRequestViewModelValidator()
    {
        // Bounded because it is stored and later shown to officers — and because it is text from
        // outside the community that will eventually sit near a prompt (ai.md).
        RuleFor(x => x.Message).MaximumLength(500);
    }
}
