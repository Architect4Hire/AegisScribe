namespace AegisScribe.Domain.Managers.Models.ViewModels;

// Its own PUT rather than a field on the rename PATCH: JSON cannot distinguish "omitted" from "set to
// false", so a rename sent by a form that knows nothing about this setting would silently close the
// door (api-contract.md — the same reasoning that splits the roster's rank and note).
public class SetJoinPolicyViewModel
{
    public bool AcceptsJoinRequests { get; set; }
}
