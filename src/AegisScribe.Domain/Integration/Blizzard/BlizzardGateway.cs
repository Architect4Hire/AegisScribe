using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AegisScribe.Domain.Managers.Models.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AegisScribe.Domain.Integration.Blizzard;

// A typed HttpClient, and therefore transient — anything that must survive between calls lives in a
// singleton (BlizzardAvailabilityCache, BlizzardTokenProvider, BlizzardRateLimiter).
//
// The bearer header and the rate-limit lease are deliberately absent from every method here: both are
// handlers on this client, so they apply to every call without anyone having to remember them. The
// rate-limit handler sits innermost, so it also covers the resilience handler's retries.
public sealed class BlizzardGateway(
    HttpClient httpClient,
    IOptions<BlizzardOptions> options,
    BlizzardAvailabilityCache availabilityCache,
    TimeProvider timeProvider,
    ILogger<BlizzardGateway> logger) : IBlizzardGateway
{
    public bool IsConfigured => options.Value.IsConfigured;

    public async Task<BlizzardAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        // Before a request is built, so an unconfigured deployment costs nothing: no HttpClient, no
        // resilience pipeline, no synthetic response for the circuit breaker to count.
        if (!options.Value.IsConfigured)
        {
            return BlizzardAvailability.NotConfigured;
        }

        if (availabilityCache.TryGet(out var cached))
        {
            return cached;
        }

        var availability = await ProbeAsync(options.Value, cancellationToken);
        availabilityCache.Set(availability);

        return availability;
    }

    public async Task<Realm?> FetchRealmAsync(string region, string realmSlug, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var slug = Uri.EscapeDataString(realmSlug.ToLowerInvariant());

        // dynamic-, not static-. A static- namespace here 404s, and a 404 from this method reads as
        // "no such realm".
        var payload = await GetAsync<BlizzardRealmResponse>(
            $"/data/wow/realm/{slug}?namespace={settings.DynamicNamespace}&locale={settings.Locale}",
            cancellationToken);

        return payload?.ToRealm(region, timeProvider.GetUtcNow());
    }

    public async Task<IReadOnlyList<long>> FetchConnectedRealmIdsAsync(
        string region,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;

        var payload = await GetAsync<BlizzardConnectedRealmIndexResponse>(
            $"/data/wow/connected-realm/index?namespace={settings.DynamicNamespace}&locale={settings.Locale}",
            cancellationToken);

        return payload?.ToConnectedRealmIds() ?? [];
    }

    public async Task<IReadOnlyList<Realm>> FetchConnectedRealmAsync(
        string region,
        long connectedRealmId,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;

        var payload = await GetAsync<BlizzardConnectedRealmResponse>(
            $"/data/wow/connected-realm/{connectedRealmId}" +
            $"?namespace={settings.DynamicNamespace}&locale={settings.Locale}",
            cancellationToken);

        return payload?.ToRealms(region, timeProvider.GetUtcNow()) ?? [];
    }

    public async Task<GuildRosterSnapshot?> FetchGuildRosterAsync(
        string realmSlug,
        string guildNameSlug,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var realm = Uri.EscapeDataString(realmSlug.ToLowerInvariant());
        var guild = Uri.EscapeDataString(guildNameSlug.ToLowerInvariant());

        // The namespace trap, and the reason this method exists rather than a caller building the URL:
        // the path sits under /data/wow/ like Game Data, but the namespace is profile-. A static- or
        // dynamic- namespace returns a 404 that reads as "no such guild".
        var payload = await GetAsync<BlizzardGuildRosterResponse>(
            $"/data/wow/guild/{realm}/{guild}/roster" +
            $"?namespace={settings.ProfileNamespace}&locale={settings.Locale}",
            cancellationToken);

        return payload?.ToRosterSnapshot(timeProvider.GetUtcNow());
    }

    public async Task<Character?> FetchCharacterAsync(
        string realmSlug,
        string characterName,
        CancellationToken cancellationToken)
    {
        var payload = await GetAsync<BlizzardCharacterSummaryResponse>(
            BuildCharacterRequestUri(realmSlug, characterName, segment: null),
            cancellationToken);

        if (payload is null)
        {
            return null;
        }

        WarnIfRealmDiffers(realmSlug, payload.Realm?.Slug, characterName);

        return payload.ToCharacter(timeProvider.GetUtcNow());
    }

    public async Task<CharacterEquipment?> FetchEquipmentAsync(
        string realmSlug,
        string characterName,
        CancellationToken cancellationToken)
    {
        var payload = await GetAsync<BlizzardEquipmentResponse>(
            BuildCharacterRequestUri(realmSlug, characterName, segment: "equipment"),
            cancellationToken);

        return payload?.ToCharacterEquipment(timeProvider.GetUtcNow());
    }

    private async Task<BlizzardAvailability> ProbeAsync(BlizzardOptions settings, CancellationToken cancellationToken)
    {
        // The realm index: always present, and needs no id to look up. Realms move, so they are
        // dynamic- data — a static- namespace would 404 and read like "no realms exist".
        var requestUri = $"/data/wow/realm/index?namespace={settings.DynamicNamespace}&locale={settings.Locale}";

        try
        {
            // Headers only. This asks "would Blizzard answer us", and the realm list is a sizeable
            // payload we have no use for here.
            using var response = await httpClient.GetAsync(
                requestUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return BlizzardAvailability.Available;
            }

            logger.LogWarning(
                "The Blizzard availability probe returned {StatusCode}. Blizzard-backed data is unavailable.",
                (int)response.StatusCode);

            return BlizzardAvailability.Unavailable;
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "The Blizzard availability probe could not reach Blizzard.");
            return BlizzardAvailability.Unavailable;
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Timed out, as opposed to our caller giving up — that one propagates.
            logger.LogWarning(exception, "The Blizzard availability probe timed out.");
            return BlizzardAvailability.Unavailable;
        }
    }

    // Three things here are load-bearing and none are obvious from the path:
    //
    //  - the namespace is profile-{region}, NOT static-. Getting it wrong returns a 404, which the
    //    caller would faithfully report as "this character does not exist" — the single most confusing
    //    failure mode in this integration, which is why it is computed and asserted in tests.
    //  - locale is a query parameter. Blizzard ignores Accept-Language entirely.
    //  - the name is lowercased AND percent-encoded. Plenty of EU characters have an accent in their
    //    name, and an unencoded one produces a malformed URL rather than a 404.
    private string BuildCharacterRequestUri(string realmSlug, string characterName, string? segment)
    {
        var settings = options.Value;
        var realm = Uri.EscapeDataString(realmSlug.ToLowerInvariant());
        var name = Uri.EscapeDataString(characterName.ToLowerInvariant());
        var suffix = segment is null ? string.Empty : $"/{segment}";

        return $"/profile/wow/character/{realm}/{name}{suffix}" +
            $"?namespace={settings.ProfileNamespace}&locale={settings.Locale}";
    }

    // One request shape for every profile read: null on 404, BlizzardUnavailableException on anything
    // else. Callers above stay free of HTTP entirely.
    private async Task<TPayload?> GetAsync<TPayload>(string requestUri, CancellationToken cancellationToken)
        where TPayload : class
    {
        // Throws rather than returning null because "we have no credentials" is not "this character
        // does not exist" — a 503 read as a 404 is exactly the confusion this method exists to prevent.
        if (!options.Value.IsConfigured)
        {
            throw new BlizzardUnavailableException("Blizzard credentials are not configured.");
        }

        HttpResponseMessage response;

        try
        {
            response = await httpClient.GetAsync(requestUri, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new BlizzardUnavailableException("Blizzard could not be reached.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Timed out, as opposed to our caller giving up — that one propagates untouched.
            throw new BlizzardUnavailableException("The Blizzard request timed out.", exception);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                // 429 and the rate limiter's synthetic 503 both land here, which is the point: being
                // throttled means we could not ask, so the caller falls back to the stored row rather
                // than concluding the character is gone.
                logger.LogWarning(
                    "Blizzard returned {StatusCode} for a profile read. Blizzard-backed data is unavailable.",
                    (int)response.StatusCode);

                throw new BlizzardUnavailableException(
                    $"Blizzard returned {(int)response.StatusCode} for a profile read.");
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<TPayload>(BlizzardJson.Options, cancellationToken);
            }
            catch (JsonException exception)
            {
                throw new BlizzardUnavailableException(
                    "Blizzard returned a response that could not be read.",
                    exception);
            }
        }
    }

    // Blizzard answers a profile request for any realm in a connected-realm group and reports the
    // character's actual realm in the response. Keying the row on the slug we asked for would create a
    // second row for a character we already hold. Nothing here can fix it — the gateway has no store —
    // so it says so loudly instead.
    private void WarnIfRealmDiffers(string requestedSlug, string? reportedSlug, string characterName)
    {
        if (reportedSlug is null || string.Equals(requestedSlug, reportedSlug, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        logger.LogWarning(
            "Blizzard resolved {CharacterName} on {RequestedRealm} to realm {ReportedRealm}. The caller " +
            "should persist it under the realm Blizzard reported, not the one requested.",
            characterName,
            requestedSlug,
            reportedSlug);
    }
}
