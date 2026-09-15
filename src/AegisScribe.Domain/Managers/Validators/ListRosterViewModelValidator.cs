using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ViewModels;
using FluentValidation;

namespace AegisScribe.Domain.Managers.Validators;

public class ListRosterViewModelValidator : AbstractValidator<ListRosterViewModel>
{
    public ListRosterViewModelValidator()
    {
        // The server-enforced max api-contract.md requires — an unbounded limit is a DoS endpoint with
        // extra steps. It counts MAINS now (7.4), and the ceiling is lower than the character search's
        // 100 for exactly that reason: each main can drag several alts along with it.
        RuleFor(x => x.Limit).InclusiveBetween(1, 50);

        // The whitelist. An unrecognised sort is a 400 rather than a silent fall back to the default:
        // the set is small and closed, so a value outside it is a client bug, and quietly returning a
        // differently-ordered page is how a paging bug gets blamed on the server.
        RuleFor(x => x.Sort).IsInEnum();

        // A cursor is client-supplied however opaque it looks, so its decoded parts are bounded like
        // any other string that reaches a query. 64 covers the longest key any sort produces (a
        // character name).
        RuleFor(x => x.AfterKey).MaximumLength(64);
    }
}
