namespace AegisScribe.Domain.Managers.Models.Domain;

// A request that passed shape validation but was refused by a domain or store rule (duplicate email,
// password policy, wrong current password). The global exception handler maps it to the same
// ValidationProblemDetails 400 a FluentValidation failure produces, keyed per field/code.
public class DomainValidationException(IDictionary<string, string[]> errors)
    : Exception("One or more domain rules rejected the request.")
{
    public IDictionary<string, string[]> Errors { get; } = errors;
}
