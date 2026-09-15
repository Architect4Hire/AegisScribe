namespace AegisScribe.Domain.Managers.Models.Domain;

// The race the pre-check in TenantBusiness.CreateAsync cannot close: two callers check the same free
// slug, both pass, and the unique index refuses the second write. Mapped to 409, NOT to the 400 the
// pre-check produces — they are different conditions. "That name is taken" is something the form shows
// inline next to the field; this one means the answer changed underneath a request that had already
// been accepted. The 400 is a shipped promise (contract/openapi.v1.json, and
// TenantEndpointTests.Create_DuplicateSlug_IsAValidationProblemOnTheSlugField) and does not change:
// changing an error's status is breaking (api-contract.md).
public class SlugTakenException() : Exception("The tenant slug was taken by a concurrent request.");
