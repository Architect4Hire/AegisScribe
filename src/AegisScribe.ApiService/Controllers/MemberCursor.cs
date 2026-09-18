using System.Globalization;
using System.Text;
using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.ApiService.Controllers;

// The member list's keyset cursor: the last row's JoinedAt and user id, base64'd.
//
// Opaque to the client but still client-supplied, so a malformed value restarts the page rather than
// failing the request, and there is nothing tenant-shaped in it to tamper with (api-contract.md).
internal static class MemberCursor
{
    public static string Encode(TenantMemberServiceModel last) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{last.JoinedAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)}|{last.UserId}"));

    public static (DateTimeOffset? AfterJoinedAt, string? AfterId) Decode(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            return (null, null);
        }

        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|', 2);

            if (parts.Length == 2
                && long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var epochMs))
            {
                return (DateTimeOffset.FromUnixTimeMilliseconds(epochMs), parts[1]);
            }
        }
        catch (FormatException)
        {
            // Fall through to the unpositioned page below.
        }

        return (null, null);
    }
}
