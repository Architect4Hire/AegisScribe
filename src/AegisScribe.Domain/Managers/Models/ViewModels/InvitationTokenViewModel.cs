namespace AegisScribe.Domain.Managers.Models.ViewModels;

// The token, in a request BODY rather than in the URL — and that is a security decision, not a REST
// preference.
//
// ASP.NET Core opens a logging scope carrying RequestPath on every request, so a token in the path is
// stamped onto every log entry the request produces, and onto OpenTelemetry's url.path with it. That
// is the whole log pipeline, not merely an access log: a live invitation would sit in the dashboard
// and in whatever log store the deployment exports to. A body is in none of them.
//
// It makes the preview a POST for what is really a read. Worth it, and it buys something else on the
// way: a POST is not cached, not bookmarked, and not replayed by a browser's history.
//
// The token is still in the SPA's own /join/:token URL and in the gateway's returnUrl, because a link
// somebody clicks has to be a link. What answers that is the short expiry, the single use and the
// revoke — this removes the sink that mattered most.
public class InvitationTokenViewModel
{
    public string Token { get; set; } = string.Empty;
}
