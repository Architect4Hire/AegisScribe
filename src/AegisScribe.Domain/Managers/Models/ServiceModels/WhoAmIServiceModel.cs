namespace AegisScribe.Domain.Managers.Models.ServiceModels;

// Echoes the claims the resource server accepted — isolates "did the token authenticate" from any
// user lookup, which /me does and which a client-credentials token would fail.
public class WhoAmIServiceModel
{
    public string? Sub { get; set; }
    public bool IsPlatformAdmin { get; set; }
}
