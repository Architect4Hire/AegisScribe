using AegisScribe.Domain.Business;
using AegisScribe.Domain.Context;
using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace AegisScribe.Tests.Roster;

// Business: mocked data layer. This class holds the add-endpoint skill's two named rules — one claim
// per character, and "this claim belongs to another user" — plus the one that matters most and is
// hardest to see: the claimant comes from ICurrentUser and never from the request.
public class CharacterClaimBusinessTests
{
    private const string MeUserId = "user-me";
    private const string SomebodyElseUserId = "user-somebody-else";

    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly ICharacterClaimDataLayer _dataLayer = Substitute.For<ICharacterClaimDataLayer>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly CharacterClaimBusiness _business;

    public CharacterClaimBusinessTests()
    {
        _currentUser.UserId.Returns(MeUserId);
        _dataLayer.CharacterExistsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        _business = new CharacterClaimBusiness(
            _dataLayer, _currentUser, new FakeTimeProvider(Now));
    }

    [Fact]
    public async Task Claim_TakesTheUserFromTheCallerAndNeverFromTheRequest()
    {
        // The assertion this whole file exists for. external.md makes a claim the proof of ownership
        // behind a user-triggered global erasure, so a claim that could name somebody else
        // would become a way to delete their data. There is no field on the view model to supply one,
        // and this pins that the written row uses ICurrentUser.
        var characterId = Guid.NewGuid();
        CharacterClaim? saved = null;
        _dataLayer
            .When(layer => layer.AddAsync(Arg.Any<CharacterClaim>(), Arg.Any<CancellationToken>()))
            .Do(call => saved = call.Arg<CharacterClaim>());

        await _business.ClaimAsync(new ClaimCharacterViewModel { CharacterId = characterId }, CancellationToken.None);

        Assert.NotNull(saved);
        Assert.Equal(MeUserId, saved!.UserId);
        Assert.Equal(characterId, saved.CharacterId);
        Assert.Equal(Now, saved.ClaimedAt);

        // Left at Guid.Empty on purpose — the interceptor stamps it (tenancy.md).
        Assert.Equal(Guid.Empty, saved.TenantId);
    }

    [Fact]
    public async Task Claim_UnknownCharacter_ReturnsNull_AndNeverWrites()
    {
        _dataLayer.CharacterExistsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await _business.ClaimAsync(
            new ClaimCharacterViewModel { CharacterId = Guid.NewGuid() }, CancellationToken.None);

        // The controller turns this into the 404 the foreign key would otherwise have made a 500.
        Assert.Null(result);
        await _dataLayer.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Claim_AlreadyHeldBySomebodyElse_Throws_AndNeverWrites()
    {
        var characterId = Guid.NewGuid();
        _dataLayer.FindByCharacterAsync(characterId, Arg.Any<CancellationToken>()).Returns(
            new CharacterClaimServiceModel
            {
                CharacterId = characterId,
                ClaimedByUserId = SomebodyElseUserId,
                ClaimedByDisplayName = "Thornwake",
                ClaimedAt = Now,
            });

        var ex = await Assert.ThrowsAsync<CharacterAlreadyClaimedException>(
            () => _business.ClaimAsync(new ClaimCharacterViewModel { CharacterId = characterId }, CancellationToken.None));

        // The name the 409 body surfaces, so the conflict dialog can say who.
        Assert.Equal("Thornwake", ex.ClaimedByDisplayName);
        await _dataLayer.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Claim_AlreadyHeldByTheCaller_ReturnsItWithoutWritingAgain()
    {
        var characterId = Guid.NewGuid();
        var existing = new CharacterClaimServiceModel
        {
            CharacterId = characterId,
            ClaimedByUserId = MeUserId,
            ClaimedAt = Now,
        };
        _dataLayer.FindByCharacterAsync(characterId, Arg.Any<CancellationToken>()).Returns(existing);

        var result = await _business.ClaimAsync(
            new ClaimCharacterViewModel { CharacterId = characterId }, CancellationToken.None);

        // A retried POST is a satisfied intent, not a conflict.
        Assert.Same(existing, result);
        await _dataLayer.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Release_SomebodyElsesClaim_Throws_AndNeverRemoves()
    {
        var characterId = Guid.NewGuid();
        _dataLayer.FindEntityByCharacterAsync(characterId, Arg.Any<CancellationToken>()).Returns(
            new CharacterClaim { Id = Guid.NewGuid(), CharacterId = characterId, UserId = SomebodyElseUserId });

        await Assert.ThrowsAsync<ClaimNotYoursException>(
            () => _business.ReleaseAsync(characterId, CancellationToken.None));

        await _dataLayer.DidNotReceiveWithAnyArgs().RemoveAsync(default!, default);
    }

    [Fact]
    public async Task Release_NoClaim_IsANoOpRatherThanAnError()
    {
        // DELETE is idempotent, and this also covers a claim held in another community — which the
        // query filter makes indistinguishable from absent, correctly.
        await _business.ReleaseAsync(Guid.NewGuid(), CancellationToken.None);

        await _dataLayer.DidNotReceiveWithAnyArgs().RemoveAsync(default!, default);
    }

    [Fact]
    public async Task Clear_RecordsWhoActedAndWhoWasActedUpon()
    {
        var characterId = Guid.NewGuid();
        var claim = new CharacterClaim { Id = Guid.NewGuid(), CharacterId = characterId, UserId = SomebodyElseUserId };
        _dataLayer.FindEntityByCharacterAsync(characterId, Arg.Any<CancellationToken>()).Returns(claim);

        AuditLog? written = null;
        _dataLayer
            .When(layer => layer.ClearAsync(Arg.Any<CharacterClaim>(), Arg.Any<AuditLog>(), Arg.Any<CancellationToken>()))
            .Do(call => written = call.Arg<AuditLog>());

        await _business.ClearAsync(characterId, CancellationToken.None);

        Assert.NotNull(written);
        Assert.Equal(AuditAction.CharacterClaimCleared, written!.Action);
        // The officer who acted, and the member acted upon — the two halves of "who did this to whom"
        // that make the row worth keeping.
        Assert.Equal(MeUserId, written.ActorUserId);
        Assert.Equal(SomebodyElseUserId, written.SubjectUserId);
        Assert.Equal(characterId, written.TargetId);
        Assert.Equal(Now, written.OccurredAt);
        // Freed, never reassigned.
        Assert.Equal("Unclaimed", written.After);
    }

    [Fact]
    public async Task Clear_NoClaim_WritesNoAuditRow()
    {
        await _business.ClearAsync(Guid.NewGuid(), CancellationToken.None);

        // An audit table that recorded things which did not happen would be worse than one with gaps.
        await _dataLayer.DidNotReceiveWithAnyArgs().ClearAsync(default!, default!, default);
    }

    [Fact]
    public async Task AClientCredentialsToken_CannotClaim()
    {
        // A machine token names no Identity user. Failing loudly beats writing a claim with an empty
        // owner — which, being the erasure proof-of-ownership, is not a row to be relaxed about.
        _currentUser.UserId.Returns((string?)null);

        await Assert.ThrowsAsync<AuthenticationRequiredException>(
            () => _business.ClaimAsync(new ClaimCharacterViewModel { CharacterId = Guid.NewGuid() }, CancellationToken.None));
    }
}
