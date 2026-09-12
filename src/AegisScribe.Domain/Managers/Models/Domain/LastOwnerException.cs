namespace AegisScribe.Domain.Managers.Models.Domain;

// Placeholder for the future demote/remove membership work — nothing throws this yet. auth.md: "a
// tenant must always have at least one Owner. The demote and remove paths both refuse the last one.
// This is a domain rule and lives in Business." Mapped now so that future work only adds the check.
public class LastOwnerException()
    : Exception("A tenant must always have at least one Owner.");
