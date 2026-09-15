namespace AegisScribe.Domain.Managers.Models.Domain;

// Two ranks with the same name in one community is a data-entry mistake, not a feature: delete the
// check and an officer gets two "Raider" rows they cannot tell apart.
//
// Unlike SlugTakenException this covers BOTH the pre-check and the race it cannot close — there is no
// shipped 400 promise to keep here, so one condition gets one status.
public class RankNameTakenException(string name)
    : Exception($"A rank named '{name}' already exists in this community.")
{
    public string Name { get; } = name;
}
