namespace AegisScribe.Domain.Managers.Models.Domain;

// Two ranks with the same name in one community is a data-entry mistake, not a feature, so the name
// is unique per tenant. Thrown by TenantRankBusiness after reading the existing rows — a rule, by the
// add-endpoint skill's own test: delete the check and an officer gets two "Raider" rows they cannot
// tell apart, which is a wrong answer rather than a stale one.
//
// Unlike SlugTakenException this covers BOTH the pre-check and the race the pre-check cannot close:
// there is no shipped 400 promise to keep here (7.1 is the first version of this endpoint), so one
// condition gets one status. 409 with a stable type URI, which is what the client branches on.
public class RankNameTakenException(string name)
    : Exception($"A rank named '{name}' already exists in this community.")
{
    public string Name { get; } = name;
}
