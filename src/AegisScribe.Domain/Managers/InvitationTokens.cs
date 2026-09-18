using System.Security.Cryptography;

namespace AegisScribe.Domain.Managers;

// Minting and recognising an invitation token.
//
// A live token grants membership in a community to whoever holds it, so it is handled like a password
// rather than like an id: generated from a CSPRNG, stored only as a hash, and compared by hash. The
// plaintext exists exactly once — in the response that created it.
public static class InvitationTokens
{
    // 256 bits. The tokens are guessed at over the network against a unique index, so the only real
    // requirement is that enumerating them is hopeless; 32 bytes is far past that and costs nothing.
    private const int TokenBytes = 32;

    public static string Create() =>
        // Base64url: it rides in a path segment (/join/:token) and in a redirect's return URL, so it
        // must survive both without escaping.
        Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes));

    /// <summary>SHA-256 of the token, as stored in <c>TenantInvitation.TokenHash</c>.</summary>
    /// <remarks>
    /// Unsalted and unstretched, deliberately, unlike a password hash: this input is 256 bits of
    /// uniform randomness rather than something a person chose, so there is no dictionary to attack
    /// and nothing for a salt to defeat. What the hash buys is that a leaked database row cannot be
    /// replayed as an invitation — and a fast hash is required, because the lookup is by hash.
    /// </remarks>
    public static byte[] Hash(string token) =>
        SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
