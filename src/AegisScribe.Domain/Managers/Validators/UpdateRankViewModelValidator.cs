using AegisScribe.Domain.Managers.Models.ViewModels;
using FluentValidation;

namespace AegisScribe.Domain.Managers.Validators;

public class UpdateRankViewModelValidator : AbstractValidator<UpdateRankViewModel>
{
    public UpdateRankViewModelValidator()
    {
        RuleFor(x => x.Name).RankName();
        RuleFor(x => x.Colour).RankColour();
        RuleFor(x => x.SortOrder).RankSortOrder();
    }
}
