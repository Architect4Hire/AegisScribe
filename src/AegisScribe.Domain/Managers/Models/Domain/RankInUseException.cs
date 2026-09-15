namespace AegisScribe.Domain.Managers.Models.Domain;

// Deleting a rank roster entries still hold is refused rather than performed: clearing it off every
// holder is the silent kind of damage, un-ranking forty raiders with nothing in the response saying so.
//
// The count travels with it because "reassign these first" is only actionable if the officer knows how
// many there are.
public class RankInUseException(int holderCount)
    : Exception($"{holderCount} roster entries still hold this rank.")
{
    public int HolderCount { get; } = holderCount;
}
