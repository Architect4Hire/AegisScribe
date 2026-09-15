namespace AegisScribe.Domain.Managers.Models.Domain;

// The race TenantBusiness.CreateAsync's pre-check cannot close: two callers check the same free slug,
// both pass, and the unique index refuses the second write.
//
// Mapped to 409, NOT the 400 the pre-check produces — different conditions. The 400 says "that name is
// taken" next to the field; this says the answer changed underneath an accepted request. The 400 is a
// shipped promise and does not change, because changing an error's status is breaking (api-contract.md).
public class SlugTakenException() : Exception("The tenant slug was taken by a concurrent request.");
