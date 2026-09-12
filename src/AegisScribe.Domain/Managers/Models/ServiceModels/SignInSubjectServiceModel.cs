namespace AegisScribe.Domain.Managers.Models.ServiceModels;

// Who a token is about to be minted for. Consumed by the OpenIddict connect/* controller, which turns
// it into claims — never serialized onto the wire. sub and roles only, nothing tenant-shaped (auth.md).
public class SignInSubjectServiceModel
{
    public string UserId { get; set; } = string.Empty;
    public IReadOnlyList<string> Roles { get; set; } = [];
}
