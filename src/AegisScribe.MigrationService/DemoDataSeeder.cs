using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.MigrationService;

// Fictional demo content for local/dev environments only — two tenants (tenancy.md: every isolation
// test from here on depends on there being more than one), users, realms, characters with full gear,
// a guild roster, and a small crafting catalog. No real Blizzard identifiers, no real player data.
public static class DemoDataSeeder
{
    // Sign-in credential shared by every seeded demo account, chosen only to satisfy Identity's
    // default complexity policy. Not a secret — it's public, documented, and only ever exists in a
    // locally-seeded dev database, never anywhere real accounts live.
    private const string DemoAccountCredential = "AegisDemo1!";

    public static async Task SeedAsync(IServiceProvider services, ILogger logger)
    {
        var db = services.GetRequiredService<AegisScribeDbContext>();

        // This dataset is fixed fictional content, not config that can legitimately change between
        // runs (unlike OpenIddictClientSeeder's per-client upsert) — one existence gate, not a
        // find-or-create per row, since aspire run restarts this service constantly in local dev.
        var alreadySeeded = await db.Tenants.AnyAsync(t => t.Slug == "ashes-of-dawn")
            && await db.Tenants.AnyAsync(t => t.Slug == "emberwatch");
        if (alreadySeeded)
        {
            logger.LogInformation("Demo data already seeded; skipping.");
            return;
        }

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        await SeedTenantAsync(db, userManager, "Ashes of Dawn", "ashes-of-dawn", "America/New_York",
        [
            ("elara.dawnbringer@example.com", "Elara Dawnbringer", TenantRole.Owner),
            ("borin.stonefist@example.com", "Borin Stonefist", TenantRole.Officer),
        ]);

        await SeedTenantAsync(db, userManager, "Emberwatch", "emberwatch", "Europe/London",
        [
            ("kael.ashveil@example.com", "Kael Ashveil", TenantRole.Owner),
            ("seraphine.emberlyn@example.com", "Seraphine Emberlyn", TenantRole.Officer),
        ]);

        var duskwatch = SeedRealm(db, "Duskwatch", "duskwatch", "us", 1001);
        var ironveil = SeedRealm(db, "Ironveil", "ironveil", "eu", 2001);

        var duskwatchCharacters = SeedCharacters(db, duskwatch, CharacterFaction.Alliance,
        [
            ("Aldric Sunblade", CharacterClass.Warrior, "Protection", 80, 620),
            ("Brynhild Frostmourne", CharacterClass.Paladin, "Holy", 80, 615),
            ("Corvin Shadowstep", CharacterClass.Rogue, "Assassination", 80, 610),
            ("Delphine Moonshadow", CharacterClass.Priest, "Discipline", 79, 600),
            ("Grimjaw Bloodfist", CharacterClass.DeathKnight, "Blood", 80, 618),
            ("Ithralas Windrunner", CharacterClass.Hunter, "Marksmanship", 80, 605),
        ]);

        SeedCharacters(db, ironveil, CharacterFaction.Horde,
        [
            ("Kaelthas Emberweave", CharacterClass.Mage, "Fire", 80, 622),
            ("Lyanna Starweaver", CharacterClass.Warlock, "Affliction", 78, 590),
            ("Magnus Stonebark", CharacterClass.Druid, "Restoration", 80, 612),
            ("Nyxara Duskblade", CharacterClass.DemonHunter, "Havoc", 80, 617),
            ("Orin Cinderfall", CharacterClass.Monk, "Mistweaver", 79, 598),
            ("Pyra Ashenveil", CharacterClass.Shaman, "Elemental", 80, 608),
        ]);

        SeedGuild(db, duskwatch, duskwatchCharacters);
        SeedCraftingCatalog(db);

        await db.SaveChangesAsync();
        logger.LogInformation("Demo data seeded.");
    }

    private static async Task SeedTenantAsync(
        AegisScribeDbContext db, UserManager<ApplicationUser> userManager,
        string name, string slug, string timeZoneId,
        (string Email, string DisplayName, TenantRole Role)[] members)
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = name, Slug = slug, TimeZoneId = timeZoneId };
        db.Tenants.Add(tenant);

        foreach (var (email, displayName, role) in members)
        {
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = displayName,
                CreatedAt = DateTimeOffset.UtcNow,
                LastTenantId = tenant.Id,
            };

            var result = await userManager.CreateAsync(user, DemoAccountCredential);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to seed demo user '{email}': {string.Join("; ", result.Errors.Select(e => e.Description))}");
            }

            db.TenantMemberships.Add(new TenantMembership
            {
                TenantId = tenant.Id,
                UserId = user.Id,
                Role = role,
                JoinedAt = DateTimeOffset.UtcNow,
            });
        }
    }

    private static Realm SeedRealm(AegisScribeDbContext db, string name, string slug, string region, long blizzardConnectedRealmId)
    {
        var realm = new Realm
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = slug,
            Region = region,
            BlizzardConnectedRealmId = blizzardConnectedRealmId,
            LastSyncedAt = DateTimeOffset.UtcNow,
        };
        db.Realms.Add(realm);
        return realm;
    }

    private static long _nextBlizzardCharacterId = 900400;
    private static long _nextBlizzardItemId = 900500;

    private static List<Character> SeedCharacters(
        AegisScribeDbContext db, Realm realm, CharacterFaction faction,
        (string Name, CharacterClass Class, string Spec, int Level, int ItemLevel)[] characters)
    {
        var created = new List<Character>();

        foreach (var (name, characterClass, spec, level, itemLevel) in characters)
        {
            var character = new Character
            {
                Id = Guid.NewGuid(),
                RealmId = realm.Id,
                Name = name,
                NameLower = name.ToLowerInvariant(),
                Level = level,
                Class = characterClass,
                Spec = spec,
                ItemLevel = itemLevel,
                Faction = faction,
                BlizzardCharacterId = _nextBlizzardCharacterId++,
                LastSyncedAt = DateTimeOffset.UtcNow,
            };
            db.Characters.Add(character);

            var equipment = new CharacterEquipment
            {
                Id = Guid.NewGuid(),
                CharacterId = character.Id,
                LastSyncedAt = DateTimeOffset.UtcNow,
            };
            db.CharacterEquipments.Add(equipment);

            var rng = new Random(name.GetHashCode());
            var firstName = name.Split(' ')[0];
            foreach (EquipmentSlot slot in Enum.GetValues<EquipmentSlot>())
            {
                var quality = rng.NextDouble() < 0.7 ? ItemQuality.Epic : ItemQuality.Rare;
                var slotItemLevel = itemLevel + rng.Next(-5, 6);

                db.EquippedItems.Add(new EquippedItem
                {
                    Id = Guid.NewGuid(),
                    CharacterEquipmentId = equipment.Id,
                    Slot = slot,
                    BlizzardItemId = _nextBlizzardItemId++,
                    ItemName = $"{firstName}'s {SlotNoun(slot)}",
                    Quality = quality,
                    ItemLevel = slotItemLevel,
                    IconName = $"inv_{SlotIconPrefix(slot)}_{(int)quality:D2}",
                });
            }

            created.Add(character);
        }

        return created;
    }

    private static string SlotNoun(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.Head => "Helm",
        EquipmentSlot.Neck => "Necklace",
        EquipmentSlot.Shoulder => "Shoulderguards",
        EquipmentSlot.Back => "Cloak",
        EquipmentSlot.Chest => "Chestguard",
        EquipmentSlot.Wrist => "Bracers",
        EquipmentSlot.Hands => "Gloves",
        EquipmentSlot.Waist => "Belt",
        EquipmentSlot.Legs => "Legguards",
        EquipmentSlot.Feet => "Boots",
        EquipmentSlot.Finger1 or EquipmentSlot.Finger2 => "Signet Ring",
        EquipmentSlot.Trinket1 or EquipmentSlot.Trinket2 => "Trinket",
        EquipmentSlot.MainHand => "Blade",
        EquipmentSlot.OffHand => "Offhand",
        _ => "Gear",
    };

    private static string SlotIconPrefix(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.Head => "helmet",
        EquipmentSlot.Neck => "necklace",
        EquipmentSlot.Shoulder => "shoulder",
        EquipmentSlot.Back => "cloak",
        EquipmentSlot.Chest => "chest",
        EquipmentSlot.Wrist => "bracer",
        EquipmentSlot.Hands => "gauntlets",
        EquipmentSlot.Waist => "belt",
        EquipmentSlot.Legs => "pants",
        EquipmentSlot.Feet => "boots",
        EquipmentSlot.Finger1 or EquipmentSlot.Finger2 => "ring",
        EquipmentSlot.Trinket1 or EquipmentSlot.Trinket2 => "trinket",
        EquipmentSlot.MainHand => "sword",
        EquipmentSlot.OffHand => "shield",
        _ => "misc",
    };

    private static void SeedGuild(AegisScribeDbContext db, Realm realm, List<Character> roster)
    {
        var guild = new Guild
        {
            Id = Guid.NewGuid(),
            RealmId = realm.Id,
            Name = "The Duskwatch Vanguard",
            NameLower = "the duskwatch vanguard",
            Faction = CharacterFaction.Alliance,
            BlizzardGuildId = 900001,
            LastSyncedAt = DateTimeOffset.UtcNow,
        };
        db.Guilds.Add(guild);

        // Aldric leads (rank 0); the rest fill out the roster at descending rank.
        var ranks = new Dictionary<string, int>
        {
            ["Aldric Sunblade"] = 0,
            ["Brynhild Frostmourne"] = 1,
            ["Delphine Moonshadow"] = 1,
            ["Grimjaw Bloodfist"] = 2,
            ["Ithralas Windrunner"] = 3,
            ["Corvin Shadowstep"] = 3,
        };

        foreach (var character in roster)
        {
            db.GuildMembers.Add(new GuildMember
            {
                Id = Guid.NewGuid(),
                GuildId = guild.Id,
                CharacterId = character.Id,
                BlizzardRank = ranks[character.Name],
            });
        }
    }

    private static void SeedCraftingCatalog(AegisScribeDbContext db)
    {
        var blacksmithing = new Profession
        {
            Id = Guid.NewGuid(),
            BlizzardProfessionId = 900001,
            Name = "Blacksmithing",
            LastSyncedAt = DateTimeOffset.UtcNow,
        };
        var alchemy = new Profession
        {
            Id = Guid.NewGuid(),
            BlizzardProfessionId = 900002,
            Name = "Alchemy",
            LastSyncedAt = DateTimeOffset.UtcNow,
        };
        db.Professions.AddRange(blacksmithing, alchemy);

        var ironveinOre = CreateItem(db, 900101, "Ironvein Ore", ItemQuality.Common, slot: null, itemLevel: 1);
        var duskleafHerb = CreateItem(db, 900102, "Duskleaf Herb", ItemQuality.Common, slot: null, itemLevel: 1);
        var emberroot = CreateItem(db, 900103, "Emberroot", ItemQuality.Uncommon, slot: null, itemLevel: 1);

        var legplates = CreateItem(db, 900201, "Ironclad Vanguard Legplates", ItemQuality.Rare, EquipmentSlot.Legs, itemLevel: 600);
        var greatsword = CreateItem(db, 900202, "Duskforged Greatsword", ItemQuality.Rare, EquipmentSlot.MainHand, itemLevel: 605);
        var duskElixir = CreateItem(db, 900203, "Elixir of Dusk Focus", ItemQuality.Uncommon, slot: null, itemLevel: 1);
        var emberDraught = CreateItem(db, 900204, "Draught of Ember Vigor", ItemQuality.Uncommon, slot: null, itemLevel: 1);

        var legplatesRecipe = new Recipe
        {
            Id = Guid.NewGuid(),
            BlizzardRecipeId = 900301,
            Name = "Plans: Ironclad Vanguard Legplates",
            ProfessionId = blacksmithing.Id,
            CraftedItemId = legplates.Id,
            LastSyncedAt = DateTimeOffset.UtcNow,
        };
        var greatswordRecipe = new Recipe
        {
            Id = Guid.NewGuid(),
            BlizzardRecipeId = 900302,
            Name = "Plans: Duskforged Greatsword",
            ProfessionId = blacksmithing.Id,
            CraftedItemId = greatsword.Id,
            LastSyncedAt = DateTimeOffset.UtcNow,
        };
        var duskElixirRecipe = new Recipe
        {
            Id = Guid.NewGuid(),
            BlizzardRecipeId = 900303,
            Name = "Formula: Elixir of Dusk Focus",
            ProfessionId = alchemy.Id,
            CraftedItemId = duskElixir.Id,
            LastSyncedAt = DateTimeOffset.UtcNow,
        };
        var emberDraughtRecipe = new Recipe
        {
            Id = Guid.NewGuid(),
            BlizzardRecipeId = 900304,
            Name = "Formula: Draught of Ember Vigor",
            ProfessionId = alchemy.Id,
            CraftedItemId = emberDraught.Id,
            LastSyncedAt = DateTimeOffset.UtcNow,
        };
        db.Recipes.AddRange(legplatesRecipe, greatswordRecipe, duskElixirRecipe, emberDraughtRecipe);

        db.ReagentSlots.AddRange(
            new ReagentSlot { Id = Guid.NewGuid(), RecipeId = legplatesRecipe.Id, ItemId = ironveinOre.Id, Quantity = 4 },
            new ReagentSlot { Id = Guid.NewGuid(), RecipeId = legplatesRecipe.Id, ItemId = emberroot.Id, Quantity = 1 },
            new ReagentSlot { Id = Guid.NewGuid(), RecipeId = greatswordRecipe.Id, ItemId = ironveinOre.Id, Quantity = 6 },
            new ReagentSlot { Id = Guid.NewGuid(), RecipeId = duskElixirRecipe.Id, ItemId = duskleafHerb.Id, Quantity = 3 },
            new ReagentSlot { Id = Guid.NewGuid(), RecipeId = duskElixirRecipe.Id, ItemId = emberroot.Id, Quantity = 1 },
            new ReagentSlot { Id = Guid.NewGuid(), RecipeId = emberDraughtRecipe.Id, ItemId = duskleafHerb.Id, Quantity = 2 });
    }

    private static Item CreateItem(
        AegisScribeDbContext db, long blizzardItemId, string name, ItemQuality quality, EquipmentSlot? slot, int itemLevel)
    {
        var item = new Item
        {
            Id = Guid.NewGuid(),
            BlizzardItemId = blizzardItemId,
            Name = name,
            Quality = quality,
            Slot = slot,
            ItemLevel = itemLevel,
            LastSyncedAt = DateTimeOffset.UtcNow,
        };
        item.SearchText = item.ComposeSearchText();
        db.Items.Add(item);
        return item;
    }
}
