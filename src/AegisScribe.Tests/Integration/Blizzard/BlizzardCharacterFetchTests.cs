using System.Net;
using System.Threading.RateLimiting;
using AegisScribe.Domain.Integration.Blizzard;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace AegisScribe.Tests.Integration.Blizzard;

// 6.3 — FetchCharacterAsync and FetchEquipmentAsync (add-external-sync skill, step 10).
//
// The whole pipeline is under test here, not just the gateway class: BlizzardAuthHandler and
// BlizzardRateLimitHandler are wired in exactly as BlizzardServiceCollectionExtensions wires them, because
// "every gateway method sends a bearer header and takes a lease" is a claim about the composition, and a
// test of the gateway alone would prove nothing about it.
//
// Response bodies come from Integration/Blizzard/Fixtures — see the README there for what they are and
// how to replace them with live captures.
public class BlizzardCharacterFetchTests
{
    private const string ClientId = "test-client-id";
    private const string Secret = "test-client-secret-value";
    private const string Token = "test-bearer-token";

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-13T12:00:00Z");

    [Fact]
    public async Task FetchCharacter_RequestsTheProfileNamespaceAndLocale()
    {
        // The namespace is the assertion that matters most in this file. profile- is right; static- would
        // return a 404 that this gateway would faithfully report as "no such character", which
        // references/blizzard-endpoints.md calls the single most confusing failure mode in the
        // integration. Locale is a query parameter too — Blizzard ignores Accept-Language.
        var harness = Harness.Returning(HttpStatusCode.OK, Fixture("character-summary.json"));

        await harness.Gateway.FetchCharacterAsync("argent-dawn", "Aldric", CancellationToken.None);

        Assert.Equal(
            "https://us.api.blizzard.com/profile/wow/character/argent-dawn/aldric?namespace=profile-us&locale=en_US",
            harness.Handler.SingleRequest.AbsoluteUri);
    }

    [Fact]
    public async Task FetchEquipment_RequestsTheEquipmentSegmentWithTheProfileNamespace()
    {
        var harness = Harness.Returning(HttpStatusCode.OK, Fixture("character-equipment.json"));

        await harness.Gateway.FetchEquipmentAsync("argent-dawn", "Aldric", CancellationToken.None);

        Assert.Equal(
            "https://us.api.blizzard.com/profile/wow/character/argent-dawn/aldric/equipment" +
            "?namespace=profile-us&locale=en_US",
            harness.Handler.SingleRequest.AbsoluteUri);
    }

    [Fact]
    public async Task FetchRealm_RequestsTheDynamicNamespace()
    {
        // dynamic-, not static-, and not profile- either: realms move between connected-realm groups.
        // A static- namespace here 404s, which this gateway reports as "no such realm" — and a lookup on
        // a realm we do not hold then cannot resolve one, so the character behind it disappears too.
        var harness = Harness.Returning(HttpStatusCode.OK, Fixture("realm.json"));

        await harness.Gateway.FetchRealmAsync("us", "Argent-Dawn", CancellationToken.None);

        Assert.Equal(
            "https://us.api.blizzard.com/data/wow/realm/argent-dawn?namespace=dynamic-us&locale=en_US",
            harness.Handler.SingleRequest.AbsoluteUri);
    }

    [Fact]
    public async Task FetchRealm_ReadsTheConnectedRealmIdOutOfItsLink()
    {
        // The connected-realm id is not a field on this document — it exists only as the last path
        // segment of connected_realm.href. Reading it is not the same as following it: no second
        // request is made, which is what "don't chase hrefs at request time" is actually about.
        var harness = Harness.Returning(HttpStatusCode.OK, Fixture("realm.json"));

        var realm = await harness.Gateway.FetchRealmAsync("us", "argent-dawn", CancellationToken.None);

        Assert.NotNull(realm);
        Assert.Equal("argent-dawn", realm.Slug);
        Assert.Equal("Argent Dawn", realm.Name);
        Assert.Equal(1092L, realm.BlizzardConnectedRealmId);
        Assert.Equal(Now, realm.LastSyncedAt);

        // The caller's short region code, not Blizzard's prose "North America" — that is what every
        // realm lookup keys on.
        Assert.Equal("us", realm.Region);
    }

    [Fact]
    public async Task FetchRealm_WhenBlizzardSays404_ReturnsNull()
    {
        var harness = Harness.Returning(
            HttpStatusCode.NotFound,
            """{"code":404,"type":"BLZWEBAPI00000404","detail":"Not Found"}""");

        Assert.Null(await harness.Gateway.FetchRealmAsync("us", "no-such-realm", CancellationToken.None));
    }

    [Theory]
    [InlineData("""{"id":1092,"name":"Argent Dawn","slug":"argent-dawn"}""")]
    [InlineData("""{"id":1092,"name":"Argent Dawn","slug":"argent-dawn","connected_realm":{"href":"not a url"}}""")]
    [InlineData("""{"id":1092,"name":"Argent Dawn","slug":"argent-dawn","connected_realm":{"href":"https://us.api.blizzard.com/data/wow/connected-realm/index?namespace=dynamic-us"}}""")]
    public async Task FetchRealm_WithAConnectedRealmLinkItCannotRead_ThrowsRatherThanStoringZero(string body)
    {
        // A realm row is the anchor every character on it hangs from. A source id of 0 shared by every
        // realm we failed to parse is the kind of corruption that is only noticed once it is in every
        // row, so this fails loudly at the boundary instead.
        var harness = Harness.Returning(HttpStatusCode.OK, body);

        await Assert.ThrowsAsync<BlizzardUnavailableException>(
            () => harness.Gateway.FetchRealmAsync("us", "argent-dawn", CancellationToken.None));
    }

    [Fact]
    public async Task FetchConnectedRealmIds_ReadsTheIdsOutOfTheIndexLinks()
    {
        // The index gives hrefs and nothing else, so the catalogue pass has to read ids out of them —
        // the same reading-not-following as the single-realm document's connected_realm link.
        var harness = Harness.Returning(HttpStatusCode.OK, Fixture("connected-realm-index.json"));

        var ids = await harness.Gateway.FetchConnectedRealmIdsAsync("us", CancellationToken.None);

        Assert.Equal([4L, 1092L, 3676L], ids);

        Assert.Equal(
            "https://us.api.blizzard.com/data/wow/connected-realm/index?namespace=dynamic-us&locale=en_US",
            harness.Handler.SingleRequest.AbsoluteUri);
    }

    [Fact]
    public async Task FetchConnectedRealm_ReturnsEveryRealmInTheGroupCarryingTheGroupId()
    {
        // The reason 6.4b goes through connected realms rather than /data/wow/realm/index: one document
        // yields several realms AND the connected-realm id, as a field rather than a link. The realm
        // index is a single call but omits connected_realm, costing a follow-up per realm.
        var harness = Harness.Returning(HttpStatusCode.OK, Fixture("connected-realm.json"));

        var realms = await harness.Gateway.FetchConnectedRealmAsync("us", 1092, CancellationToken.None);

        Assert.Equal(2, realms.Count);
        Assert.All(realms, realm =>
        {
            Assert.Equal("us", realm.Region);
            Assert.Equal(1092L, realm.BlizzardConnectedRealmId);
            Assert.Equal(Now, realm.LastSyncedAt);
        });

        var argentDawn = Assert.Single(realms, realm => realm.Slug == "argent-dawn");
        Assert.Equal("Argent Dawn", argentDawn.Name);

        // Its own id, distinct from the group id above — this is what lets a renamed realm update in
        // place rather than appearing as a second realm.
        Assert.Equal(1092L, argentDawn.BlizzardRealmId);

        var bleedingHollow = Assert.Single(realms, realm => realm.Slug == "bleeding-hollow");
        Assert.Equal(1175L, bleedingHollow.BlizzardRealmId);
        Assert.Equal(1092L, bleedingHollow.BlizzardConnectedRealmId);

        Assert.Equal(
            "https://us.api.blizzard.com/data/wow/connected-realm/1092?namespace=dynamic-us&locale=en_US",
            harness.Handler.SingleRequest.AbsoluteUri);
    }

    [Fact]
    public async Task FetchConnectedRealm_WhenTheGroupIsGone_ReturnsEmptyRatherThanThrowing()
    {
        // A group can be dissolved between the index call and this one. That is a normal answer for a
        // pass that walks ~100 of them, not a failure of the pass.
        var harness = Harness.Returning(
            HttpStatusCode.NotFound,
            """{"code":404,"type":"BLZWEBAPI00000404","detail":"Not Found"}""");

        Assert.Empty(await harness.Gateway.FetchConnectedRealmAsync("us", 1092, CancellationToken.None));
    }

    [Fact]
    public async Task FetchConnectedRealmIds_WhenBlizzardIsThrottling_ThrowsRatherThanReportingAnEmptyCatalogue()
    {
        // An empty list from here would look like "this region has no realms" and the pass would write
        // nothing while reporting success.
        var harness = Harness.Returning(HttpStatusCode.TooManyRequests);

        await Assert.ThrowsAsync<BlizzardUnavailableException>(
            () => harness.Gateway.FetchConnectedRealmIdsAsync("us", CancellationToken.None));
    }

    [Fact]
    public async Task FetchGuildRoster_UsesTheProfileNamespaceDespiteTheDataWowPath()
    {
        // The trap this whole integration keeps stepping on. Guild endpoints sit under /data/wow/ like
        // Game Data but take profile-, and a static- or dynamic- namespace here 404s — which this
        // gateway reports as "no such guild", so the wrong namespace looks exactly like a typo in the
        // guild name.
        var harness = Harness.Returning(HttpStatusCode.OK, Fixture("guild-roster.json"));

        await harness.Gateway.FetchGuildRosterAsync("Argent-Dawn", "Aegis-Vigil", CancellationToken.None);

        Assert.Equal(
            "https://us.api.blizzard.com/data/wow/guild/argent-dawn/aegis-vigil/roster" +
            "?namespace=profile-us&locale=en_US",
            harness.Handler.SingleRequest.AbsoluteUri);
    }

    [Fact]
    public async Task FetchGuildRoster_ReturnsTheGuildAndEveryMemberFromOneCall()
    {
        // The reason 6.6b is one call rather than two: the roster response carries the guild object
        // alongside the members, so linking a guild never needs a separate guild-summary request.
        var harness = Harness.Returning(HttpStatusCode.OK, Fixture("guild-roster.json"));

        var snapshot = await harness.Gateway.FetchGuildRosterAsync("argent-dawn", "aegis-vigil", CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal("Aegis Vigil", snapshot.Guild.Name);
        Assert.Equal("aegis vigil", snapshot.Guild.NameLower);
        Assert.Equal(88213714L, snapshot.Guild.BlizzardGuildId);
        Assert.Equal(CharacterFaction.Alliance, snapshot.Guild.Faction);
        Assert.Equal(Now, snapshot.Guild.LastSyncedAt);

        // RealmId is left for the caller, exactly as on a fetched character — the gateway has no store.
        Assert.Equal(Guid.Empty, snapshot.Guild.RealmId);

        var aldric = Assert.Single(snapshot.Members, m => m.Character.Name == "Aldric");
        Assert.Equal(0, aldric.BlizzardRank);
        Assert.Equal(CharacterClass.Warrior, aldric.Character.Class);
        Assert.Equal(80, aldric.Character.Level);
        Assert.Equal("argent-dawn", aldric.RealmSlug);

        // Connected realms share a roster, so members legitimately sit on other realms — which is why
        // the caller resolves a realm per distinct slug rather than assuming the guild's own.
        var brynhild = Assert.Single(snapshot.Members, m => m.Character.Name == "Brynhild");
        Assert.Equal("bleeding-hollow", brynhild.RealmSlug);
        Assert.Equal(1, brynhild.BlizzardRank);
    }

    [Fact]
    public async Task FetchGuildRoster_CarriesNoItemLevelOrGear()
    {
        // Asserted rather than assumed, because the whole cost model depends on it: membership is one
        // call, gear is two per member, and nothing about this endpoint changes that. A future reader
        // tempted to "just fill in the item levels here" should meet this test first.
        var harness = Harness.Returning(HttpStatusCode.OK, Fixture("guild-roster.json"));

        var snapshot = await harness.Gateway.FetchGuildRosterAsync("argent-dawn", "aegis-vigil", CancellationToken.None);

        Assert.All(snapshot!.Members, member =>
        {
            Assert.Equal(0, member.Character.ItemLevel);
            Assert.Null(member.Character.Spec);
            Assert.Null(member.Character.Equipment);
        });
    }

    [Fact]
    public async Task FetchGuildRoster_DropsAMemberItCannotRepresentRatherThanFailingTheRoster()
    {
        // The fixture contains a class id 99 that does not exist. For a single character fetch that is
        // a hard failure, and rightly so. For a batch it must cost one member, not the other three —
        // otherwise one unrepresentable member makes a 400-person guild permanently unsyncable.
        var harness = Harness.Returning(HttpStatusCode.OK, Fixture("guild-roster.json"));

        var snapshot = await harness.Gateway.FetchGuildRosterAsync("argent-dawn", "aegis-vigil", CancellationToken.None);

        Assert.Equal(3, snapshot!.Members.Count);
        Assert.DoesNotContain(snapshot.Members, m => m.Character.Name == "Tinkerer");
    }

    [Fact]
    public async Task FetchGuildRoster_WhenBlizzardSays404_ReturnsNull()
    {
        var harness = Harness.Returning(
            HttpStatusCode.NotFound,
            """{"code":404,"type":"BLZWEBAPI00000404","detail":"Not Found"}""");

        Assert.Null(await harness.Gateway.FetchGuildRosterAsync("argent-dawn", "no-such-guild", CancellationToken.None));
    }

    [Fact]
    public async Task FetchCharacter_ForAnotherRegion_CarriesThatRegionsHostAndNamespace()
    {
        var harness = Harness.Returning(
            HttpStatusCode.OK,
            Fixture("character-summary.json"),
            options => options.Region = "eu");

        await harness.Gateway.FetchCharacterAsync("silvermoon", "Aldric", CancellationToken.None);

        Assert.StartsWith(
            "https://eu.api.blizzard.com/profile/wow/character/silvermoon/aldric?namespace=profile-eu",
            harness.Handler.SingleRequest.AbsoluteUri);
    }

    [Fact]
    public async Task FetchCharacter_LowercasesAndPercentEncodesTheName()
    {
        // Blizzard wants the name lowercased, and plenty of EU characters have an accent in theirs. An
        // unencoded one is a malformed URL, not a 404, so it fails in a way that looks nothing like the
        // cause.
        var harness = Harness.Returning(HttpStatusCode.OK, Fixture("character-summary.json"));

        await harness.Gateway.FetchCharacterAsync("Argent-Dawn", "Amélie", CancellationToken.None);

        Assert.Contains("/argent-dawn/am%C3%A9lie?", harness.Handler.SingleRequest.AbsoluteUri);
    }

    [Fact]
    public async Task FetchCharacter_SendsTheTokenAsABearerHeaderAndNeverInTheQueryString()
    {
        // Blizzard disallowed the query-parameter form in 2024 and the repo secret-guard hook blocks it
        // outright (external.md). The header comes from BlizzardAuthHandler, which is why that handler is
        // in this pipeline rather than stubbed out.
        var harness = Harness.Returning(HttpStatusCode.OK, Fixture("character-summary.json"));

        await harness.Gateway.FetchCharacterAsync("argent-dawn", "Aldric", CancellationToken.None);

        var request = harness.Handler.SingleRequest;

        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.Equal(Token, request.AuthorizationParameter);
        Assert.DoesNotContain("access_token", request.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FetchCharacter_MapsTheCapturedSummaryToADomainEntity()
    {
        var harness = Harness.Returning(HttpStatusCode.OK, Fixture("character-summary.json"));

        var character = await harness.Gateway.FetchCharacterAsync("argent-dawn", "Aldric", CancellationToken.None);

        Assert.NotNull(character);
        Assert.Equal("Aldric", character.Name);
        Assert.Equal("aldric", character.NameLower);
        Assert.Equal(80, character.Level);
        Assert.Equal(CharacterClass.Warrior, character.Class);
        Assert.Equal("Protection", character.Spec);
        Assert.Equal(CharacterFaction.Alliance, character.Faction);
        Assert.Equal(176946929L, character.BlizzardCharacterId);
        Assert.Equal(Now, character.LastSyncedAt);
    }

    [Fact]
    public async Task FetchCharacter_TakesTheEquippedItemLevelNotTheAverage()
    {
        // Blizzard reports both. average_item_level counts what is sitting in the bags; equipped_item_level
        // is the number an armory shows and the one a raid leader means. The fixture has 623 and 620
        // precisely so a mix-up cannot pass.
        var harness = Harness.Returning(HttpStatusCode.OK, Fixture("character-summary.json"));

        var character = await harness.Gateway.FetchCharacterAsync("argent-dawn", "Aldric", CancellationToken.None);

        Assert.Equal(620, character!.ItemLevel);
    }

    [Fact]
    public async Task FetchCharacter_LeavesTheRealmAndEquipmentForTheCallerToResolve()
    {
        // The gateway has no store, so it cannot turn a realm slug into a local row — and it will not
        // fabricate Realm.BlizzardConnectedRealmId, which the erasure routine keys on. Equipment is a
        // second endpoint. Both are the DataLayer job in 6.4.
        var harness = Harness.Returning(HttpStatusCode.OK, Fixture("character-summary.json"));

        var character = await harness.Gateway.FetchCharacterAsync("argent-dawn", "Aldric", CancellationToken.None);

        Assert.Equal(Guid.Empty, character!.RealmId);
        Assert.Null(character.Equipment);
    }

    [Fact]
    public async Task FetchCharacter_WithNoGuildAndNoActiveSpec_StillMaps()
    {
        // "Fields are more optional than they look" — a character below the specialization level has no
        // active_spec key at all, and an unguilded one has no guild key. Neither is null; both are absent.
        var harness = Harness.Returning(HttpStatusCode.OK, Fixture("character-summary-unspecced.json"));

        var character = await harness.Gateway.FetchCharacterAsync("argent-dawn", "Delphine", CancellationToken.None);

        Assert.NotNull(character);
        Assert.Null(character.Spec);
        Assert.Equal(CharacterClass.Priest, character.Class);
        Assert.Equal(CharacterFaction.Horde, character.Faction);
        Assert.Equal(7, character.Level);
        Assert.Equal(16, character.ItemLevel);
    }

    [Fact]
    public async Task FetchEquipment_MapsSlotsAndQualitiesFromTheInvariantTypeTokens()
    {
        var harness = Harness.Returning(HttpStatusCode.OK, Fixture("character-equipment.json"));

        var equipment = await harness.Gateway.FetchEquipmentAsync("argent-dawn", "Aldric", CancellationToken.None);

        Assert.NotNull(equipment);

        var head = Assert.Single(equipment.EquippedItems, item => item.Slot == EquipmentSlot.Head);
        Assert.Equal(212010L, head.BlizzardItemId);
        Assert.Equal("Cyclopean Cage Helm", head.ItemName);
        Assert.Equal(ItemQuality.Epic, head.Quality);
        Assert.Equal(639, head.ItemLevel);

        var neck = Assert.Single(equipment.EquippedItems, item => item.Slot == EquipmentSlot.Neck);
        Assert.Equal(ItemQuality.Legendary, neck.Quality);
        Assert.Equal(645, neck.ItemLevel);

        // The reason ItemQuality grew a Heirloom member: Blizzard sends HEIRLOOM on any levelling alt, and
        // the design system has had a --q-heirloom token all along.
        var shoulder = Assert.Single(equipment.EquippedItems, item => item.Slot == EquipmentSlot.Shoulder);
        Assert.Equal(ItemQuality.Heirloom, shoulder.Quality);

        // MAIN_HAND and OFF_HAND are the underscored forms, as are FINGER_1 and TRINKET_2. Getting one
        // wrong silently drops a slot rather than failing.
        Assert.Single(equipment.EquippedItems, item => item.Slot == EquipmentSlot.MainHand);
        Assert.Single(equipment.EquippedItems, item => item.Slot == EquipmentSlot.OffHand);

        Assert.Equal(Now, equipment.LastSyncedAt);
    }

    [Fact]
    public async Task FetchEquipment_DropsSlotsThisApplicationDoesNotModel()
    {
        // SHIRT and TABARD are in the captured body and have no EquipmentSlot member: they carry no item
        // level and nothing in the design reference renders them. Dropping them is the decision, so it is
        // asserted rather than left to be rediscovered.
        var harness = Harness.Returning(HttpStatusCode.OK, Fixture("character-equipment.json"));

        var equipment = await harness.Gateway.FetchEquipmentAsync("argent-dawn", "Aldric", CancellationToken.None);

        Assert.Equal(5, equipment!.EquippedItems.Count);
        Assert.DoesNotContain(equipment.EquippedItems, item => item.ItemName == "Guild Tabard");
        Assert.DoesNotContain(equipment.EquippedItems, item => item.ItemName == "Rich Purple Silk Shirt");
    }

    [Fact]
    public async Task FetchEquipment_LeavesIconNamesUnset()
    {
        // The equipment response carries a media href, not a filename. Resolving it is one
        // /data/wow/media/item/{id} call per item, which would turn one character fetch into seventeen —
        // icon sync has its own dedupe story and belongs with the item catalogue.
        var harness = Harness.Returning(HttpStatusCode.OK, Fixture("character-equipment.json"));

        var equipment = await harness.Gateway.FetchEquipmentAsync("argent-dawn", "Aldric", CancellationToken.None);

        Assert.All(equipment!.EquippedItems, item => Assert.Null(item.IconName));
    }

    [Fact]
    public async Task FetchEquipment_WithNothingEquipped_ReturnsAnEmptySnapshotRatherThanNull()
    {
        // An empty list is a real answer about a real character, and it is not the same as a 404.
        var harness = Harness.Returning(HttpStatusCode.OK, Fixture("character-equipment-empty.json"));

        var equipment = await harness.Gateway.FetchEquipmentAsync("argent-dawn", "Delphine", CancellationToken.None);

        Assert.NotNull(equipment);
        Assert.Empty(equipment.EquippedItems);
        Assert.Equal(Guid.Empty, equipment.CharacterId);
    }

    [Fact]
    public async Task FetchCharacter_WhenBlizzardSays404_ReturnsNull()
    {
        // A character that does not exist is a normal answer, not an exception. Blizzard sends a JSON error
        // body with it, which this path must not try to map.
        var harness = Harness.Returning(
            HttpStatusCode.NotFound,
            """{"code":404,"type":"BLZWEBAPI00000404","detail":"Not Found"}""");

        Assert.Null(await harness.Gateway.FetchCharacterAsync("argent-dawn", "Nobody", CancellationToken.None));
    }

    [Fact]
    public async Task FetchEquipment_WhenBlizzardSays404_ReturnsNull()
    {
        var harness = Harness.Returning(
            HttpStatusCode.NotFound,
            """{"code":404,"type":"BLZWEBAPI00000404","detail":"Not Found"}""");

        Assert.Null(await harness.Gateway.FetchEquipmentAsync("argent-dawn", "Nobody", CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task FetchCharacter_WhenBlizzardRefuses_ThrowsRatherThanReportingTheCharacterMissing(
        HttpStatusCode statusCode)
    {
        // The distinction BlizzardUnavailableException exists for. If these returned null, 6.4 would cache
        // an outage as "no such character" and hide a live character behind an empty page.
        var harness = Harness.Returning(statusCode);

        await Assert.ThrowsAsync<BlizzardUnavailableException>(
            () => harness.Gateway.FetchCharacterAsync("argent-dawn", "Aldric", CancellationToken.None));
    }

    [Fact]
    public async Task FetchCharacter_WhenBlizzardIsUnreachable_ThrowsUnavailable()
    {
        var harness = Harness.For(
            new StubHttpMessageHandler((_, _) => throw new HttpRequestException("no route to host")));

        await Assert.ThrowsAsync<BlizzardUnavailableException>(
            () => harness.Gateway.FetchCharacterAsync("argent-dawn", "Aldric", CancellationToken.None));
    }

    [Fact]
    public async Task FetchCharacter_WhenBlizzardReturnsSomethingThatIsNotJson_ThrowsUnavailable()
    {
        // A proxy in front of Blizzard serving an HTML error page with a 200 is the case here. It must not
        // surface as a raw JsonException from somewhere deep in the DataLayer.
        var harness = Harness.Returning(HttpStatusCode.OK, "<html><body>502 Bad Gateway</body></html>");

        await Assert.ThrowsAsync<BlizzardUnavailableException>(
            () => harness.Gateway.FetchCharacterAsync("argent-dawn", "Aldric", CancellationToken.None));
    }

    [Fact]
    public async Task FetchCharacter_WithNoCredentials_ThrowsWithoutCallingBlizzardAtAll()
    {
        // Missing credentials degrade rather than crash the app (external.md), and the caller degrades by
        // falling back to stored data — the same way it handles an outage. What it must not do is spend a
        // request discovering there is no token.
        var harness = Harness.Returning(HttpStatusCode.OK, Fixture("character-summary.json"), options =>
        {
            options.ClientId = null;
            options.ClientSecret = null;
        });

        await Assert.ThrowsAsync<BlizzardUnavailableException>(
            () => harness.Gateway.FetchCharacterAsync("argent-dawn", "Aldric", CancellationToken.None));

        Assert.Equal(0, harness.Handler.RequestCount);
    }

    [Fact]
    public async Task FetchCharacter_WhenBlizzardSays429_BacksOffAndDoesNotSpendAnotherCall()
    {
        // The 429 path, end to end through the real limiter. Two things are being asserted: a throttled
        // response is never mistaken for a missing character, and the retry hint actually stops the next
        // call from leaving. Retry-After is 30 seconds against a MaxThrottleWait of 5, so the second
        // caller is refused a lease rather than made to wait it out.
        var handler = StubHttpMessageHandler.Scripted((_, _) =>
        {
            var response = StubHttpMessageHandler.Respond(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter =
                new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));

            return response;
        });

        var harness = Harness.For(handler);

        await Assert.ThrowsAsync<BlizzardUnavailableException>(
            () => harness.Gateway.FetchCharacterAsync("argent-dawn", "Aldric", CancellationToken.None));

        Assert.Equal(1, handler.RequestCount);

        await Assert.ThrowsAsync<BlizzardUnavailableException>(
            () => harness.Gateway.FetchCharacterAsync("argent-dawn", "Aldric", CancellationToken.None));

        // Still one. The second call never reached the wire.
        Assert.Equal(1, handler.RequestCount);

        // Once the window Blizzard named has passed, calls resume.
        harness.Time.Advance(TimeSpan.FromSeconds(31));

        await Assert.ThrowsAsync<BlizzardUnavailableException>(
            () => harness.Gateway.FetchCharacterAsync("argent-dawn", "Aldric", CancellationToken.None));

        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task FetchCharacter_WithAFactionThisApplicationCannotRepresent_ThrowsRatherThanGuessing()
    {
        // NEUTRAL is real — a Pandaren who has not picked a side — and CharacterFaction has no member for
        // it because such a character has no roster, no faction colour and nothing this app displays.
        // Mutated from the captured body rather than hand-written, so only the field under test differs.
        var body = Fixture("character-summary.json")
            .Replace(@"""type"": ""ALLIANCE""", @"""type"": ""NEUTRAL""");

        var harness = Harness.Returning(HttpStatusCode.OK, body);

        await Assert.ThrowsAsync<BlizzardUnavailableException>(
            () => harness.Gateway.FetchCharacterAsync("argent-dawn", "Aldric", CancellationToken.None));
    }

    [Fact]
    public async Task FetchCharacter_WithAClassThisApplicationCannotRepresent_ThrowsRatherThanGuessing()
    {
        // Hand-written on purpose: this is the shape of a future expansion adding a class id we have no
        // member for, which no capture taken today can contain. Failing the fetch means the caller falls
        // back to the stored row; guessing would persist a character as the wrong class, and
        // CharacterMappers.ClassColorHex would then throw somewhere the cause is invisible.
        const string body = """
            {
              "id": 176946929,
              "name": "Aldric",
              "level": 80,
              "equipped_item_level": 620,
              "faction": { "type": "ALLIANCE", "name": "Alliance" },
              "character_class": { "id": 99, "name": "Tinker" },
              "realm": { "id": 1092, "name": "Argent Dawn", "slug": "argent-dawn" }
            }
            """;

        var harness = Harness.Returning(HttpStatusCode.OK, body);

        await Assert.ThrowsAsync<BlizzardUnavailableException>(
            () => harness.Gateway.FetchCharacterAsync("argent-dawn", "Aldric", CancellationToken.None));
    }

    private static string Fixture(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Integration", "Blizzard", "Fixtures", fileName));

    // The gateway with the handler chain BlizzardServiceCollectionExtensions gives it in production —
    // auth outermost, rate limiting innermost, immediately before the wire.
    private sealed record Harness(BlizzardGateway Gateway, StubHttpMessageHandler Handler, FakeTimeProvider Time)
    {
        public static Harness Returning(
            HttpStatusCode statusCode,
            string? content = null,
            Action<BlizzardOptions>? configure = null) =>
            For(StubHttpMessageHandler.Returning(statusCode, content), configure);

        public static Harness For(StubHttpMessageHandler handler, Action<BlizzardOptions>? configure = null)
        {
            var settings = new BlizzardOptions
            {
                ClientId = ClientId,
                ClientSecret = Secret,
            };

            configure?.Invoke(settings);

            var options = new OptionsWrapper<BlizzardOptions>(settings);
            var time = new FakeTimeProvider(Now);

            var tokenProvider = Substitute.For<IBlizzardTokenProvider>();
            tokenProvider.GetTokenAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(Token));

            var rateLimiter = new BlizzardRateLimiter(
                // Auto-replenishment off: nothing here needs a refill, and a live timer in a test that
                // advances a FakeTimeProvider by half a minute is a race waiting to turn flaky.
                new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
                {
                    TokenLimit = settings.BurstCallsPerSecond,
                    TokensPerPeriod = settings.SustainedCallsPerSecond,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                    QueueLimit = settings.MaxQueuedCalls,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    AutoReplenishment = false,
                }),
                options,
                time,
                NullLogger<BlizzardRateLimiter>.Instance);

            var rateLimitHandler = new BlizzardRateLimitHandler(
                rateLimiter,
                time,
                NullLogger<BlizzardRateLimitHandler>.Instance)
            {
                InnerHandler = handler,
            };

            var authHandler = new BlizzardAuthHandler(tokenProvider) { InnerHandler = rateLimitHandler };

            var httpClient = new HttpClient(authHandler) { BaseAddress = settings.ResolveApiBaseAddress() };

            var gateway = new BlizzardGateway(
                httpClient,
                options,
                new BlizzardAvailabilityCache(time),
                time,
                NullLogger<BlizzardGateway>.Instance);

            return new Harness(gateway, handler, time);
        }
    }
}
