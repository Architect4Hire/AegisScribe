namespace AegisScribe.Domain.Managers.Models.Domain;

// Deleting a rank that roster entries still hold is refused rather than performed (7.2). The
// alternative — clearing the rank off every holder — is the silent kind of damage: an officer tidying
// up a ladder would un-rank forty raiders with nothing in the response saying so.
//
// Thrown by TenantRankBusiness.DeleteAsync after counting holders, which is a rule by the
// add-endpoint skill's test: delete the check and an action that should have been refused happens.
// The count travels with it because "reassign these first" is only actionable if the officer knows
// how many there are.
public class RankInUseException(int holderCount)
    : Exception($"{holderCount} roster entries still hold this rank.")
{
    public int HolderCount { get; } = holderCount;
}
