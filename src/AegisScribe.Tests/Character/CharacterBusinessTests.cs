using AegisScribe.Domain.Business;
using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using NSubstitute;

namespace AegisScribe.Tests.Character;

// Business: mocked data layer only (4.3, add-endpoint skill). Covers the entity->ServiceModel
// mapping on the detail read and the pass-through on the list read.
public class CharacterBusinessTests
{
    private readonly ICharacterDataLayer _dataLayer = Substitute.For<ICharacterDataLayer>();
    private readonly CharacterBusiness _business;

    public CharacterBusinessTests()
    {
        _business = new CharacterBusiness(_dataLayer);
    }

    [Fact]
    public async Task GetCharacter_MapsTheEntityToTheDetailServiceModel_IncludingEquipment()
    {
        var lastSynced = new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
        var equipmentId = Guid.NewGuid();
        var character = new Domain.Managers.Models.Domain.Character
        {
            Id = Guid.NewGuid(),
            Name = "Thrall",
            NameLower = "thrall",
            Level = 80,
            Class = CharacterClass.Shaman,
            Spec = "Enhancement",
            ItemLevel = 620,
            Faction = CharacterFaction.Horde,
            LastSyncedAt = lastSynced,
            Realm = new Realm { Slug = "emberfall", Name = "Emberfall", Region = "us" },
            Equipment = new CharacterEquipment
            {
                Id = equipmentId,
                LastSyncedAt = lastSynced,
                EquippedItems =
                [
                    new EquippedItem
                    {
                        CharacterEquipmentId = equipmentId,
                        Slot = EquipmentSlot.MainHand,
                        BlizzardItemId = 999,
                        ItemName = "Doomhammer",
                        Quality = ItemQuality.Legendary,
                        ItemLevel = 640,
                    },
                ],
            },
        };
        _dataLayer.GetCharacterAsync("us", "emberfall", "thrall", Arg.Any<CancellationToken>())
            .Returns(CharacterReadResult.Current(character));

        var result = await _business.GetCharacterAsync("us", "emberfall", "thrall", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(character.Id, result!.Id);
        Assert.Equal("emberfall", result.RealmSlug);
        Assert.Equal("Thrall", result.Name);
        Assert.Equal(80, result.Level);
        Assert.Equal(CharacterClass.Shaman, result.Class);
        Assert.Equal("Enhancement", result.Spec);
        Assert.Equal(620, result.ItemLevel);
        Assert.Equal(CharacterFaction.Horde, result.Faction);
        Assert.Equal(lastSynced, result.LastSyncedAt);

        var item = Assert.Single(result.Equipment);
        Assert.Equal(EquipmentSlot.MainHand, item.Slot);
        Assert.Equal("Doomhammer", item.ItemName);
        Assert.Equal(ItemQuality.Legendary, item.Quality);
        Assert.Equal(640, item.ItemLevel);
    }

    [Fact]
    public async Task GetCharacter_ReturnsNull_WhenTheDataLayerFindsNothing()
    {
        _dataLayer.GetCharacterAsync("us", "emberfall", "nobody", Arg.Any<CancellationToken>())
            .Returns(CharacterReadResult.NotFound);

        var result = await _business.GetCharacterAsync("us", "emberfall", "nobody", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCharacter_MapsEmptyEquipment_WhenNoEquipmentSnapshotExists()
    {
        var character = new Domain.Managers.Models.Domain.Character
        {
            Id = Guid.NewGuid(),
            Name = "Jaina",
            NameLower = "jaina",
            Realm = new Realm { Slug = "emberfall", Name = "Emberfall", Region = "us" },
            Equipment = null,
        };
        _dataLayer.GetCharacterAsync("us", "emberfall", "jaina", Arg.Any<CancellationToken>())
            .Returns(CharacterReadResult.Current(character));

        var result = await _business.GetCharacterAsync("us", "emberfall", "jaina", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result!.Equipment);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetCharacter_CarriesTheDataLayersDegradedFlagToTheServiceModel(bool isDegraded)
    {
        // The flag exists so the character screen can say "this is what we have, and it is old"
        // instead of rendering stale gear as current. Business is where it crosses from a
        // persistence fact into the outbound shape, so this is the only place it can be dropped.
        var character = new Domain.Managers.Models.Domain.Character
        {
            Id = Guid.NewGuid(),
            Name = "Thrall",
            NameLower = "thrall",
            Realm = new Realm { Slug = "emberfall", Name = "Emberfall", Region = "us" },
        };

        _dataLayer.GetCharacterAsync("us", "emberfall", "thrall", Arg.Any<CancellationToken>())
            .Returns(new CharacterReadResult(character, isDegraded));

        var result = await _business.GetCharacterAsync("us", "emberfall", "thrall", CancellationToken.None);

        Assert.Equal(isDegraded, result!.IsDegraded);
    }

    [Fact]
    public async Task SearchCharacters_PassesThroughTheDataLayersProjectedSummaries()
    {
        var afterId = Guid.NewGuid();
        var summaries = new List<CharacterSummaryServiceModel> { new() { Name = "Thrall" } };
        _dataLayer.SearchAsync("us", "emberfall", "thra", "thrall", afterId, 25, Arg.Any<CancellationToken>())
            .Returns(summaries);

        var result = await _business.SearchCharactersAsync("us", "emberfall", "thra", "thrall", afterId, 25, CancellationToken.None);

        Assert.Same(summaries, result);
    }
}
