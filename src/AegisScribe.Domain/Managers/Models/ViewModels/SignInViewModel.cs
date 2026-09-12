namespace AegisScribe.Domain.Managers.Models.ViewModels;

// The credential posted to connect/authorize's sign-in form. Deliberately no validator: a malformed
// credential is just a failed sign-in that re-renders the form, never a 400.
public class SignInViewModel
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
