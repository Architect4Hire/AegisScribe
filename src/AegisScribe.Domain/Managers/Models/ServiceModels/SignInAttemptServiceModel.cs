namespace AegisScribe.Domain.Managers.Models.ServiceModels;

// The outcome of one connect/authorize sign-in attempt. Subject is set on success; otherwise the form
// re-renders, and IsLockedOut says which of the two failure messages it shows — "incorrect email or
// password" (unknown email or wrong password, deliberately indistinguishable) or "account locked".
public class SignInAttemptServiceModel
{
    public SignInSubjectServiceModel? Subject { get; set; }
    public bool IsLockedOut { get; set; }
}
