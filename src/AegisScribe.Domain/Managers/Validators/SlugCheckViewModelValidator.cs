using AegisScribe.Domain.Managers.Models.ViewModels;
using FluentValidation;

namespace AegisScribe.Domain.Managers.Validators;

public class SlugCheckViewModelValidator : AbstractValidator<SlugCheckViewModel>
{
    public SlugCheckViewModelValidator()
    {
        // Exactly one of the two. Both at once is ambiguous about which answer the caller wants, so it
        // is a 400 rather than a silent precedence rule nobody can see from the outside.
        RuleFor(x => x)
            .Must(vm => string.IsNullOrWhiteSpace(vm.Name) ^ string.IsNullOrWhiteSpace(vm.Slug))
            .OverridePropertyName("slug")
            .WithMessage("Supply exactly one of 'name' or 'slug'.");

        // Note what is NOT validated here: the slug's format. A malformed slug is a normal ANSWER from
        // this endpoint (Available = false, Reason = Invalid), not a 400 — the form asks about every
        // keystroke, and a 400 per keystroke is not something it can render next to the field. Only an
        // absurd length is refused outright, so a pathological query cannot cost a derivation.
        RuleFor(x => x.Name).MaximumLength(200);
        RuleFor(x => x.Slug).MaximumLength(200);
    }
}
