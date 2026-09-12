namespace AegisScribe.Domain.Managers.Models.Domain;

// The operation needs an Identity user and the caller isn't one — anonymous, a machine token, or a
// token whose user no longer exists. The global exception handler maps it to a bare 401, the same
// response [Authorize] gives, so the two are indistinguishable to a client.
public class AuthenticationRequiredException()
    : Exception("This operation requires a signed-in user.");
