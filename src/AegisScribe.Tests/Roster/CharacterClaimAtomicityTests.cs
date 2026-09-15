using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Tests.Auth;
using AegisScribe.Tests.Character;
using AegisScribe.Tests.Gateway;
using AegisScribe.Tests.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Tests.Roster;

// The officer clear is this vertical's one composed write: remove the claim AND record who removed it,
// or neither. A mocked transaction only proves a commit was ASKED for, so this runs against a real
// database and asserts a mid-composition failure leaves the store untouched.
//
// A half-committed clear produces a claim that vanished with no record of who took it — precisely the
// untraceable officer action the audit table exists to prevent.
[Collection("AegisScribe API")]
public class CharacterClaimAtomicityTests(AegisScribeAppFixture fixture)
{
    [Fact]
    public async Task Clear_CommitsTheRemovalAndTheAuditRowTogether()
    {
        var context = await SeedClaimAsync();

        await using (var db = await TenantSeeding.OpenDbContextAsync(fixture, context.TenantId))
        {
            var dataLayer = CreateDataLayer(db);
            var claim = await db.CharacterClaims.SingleAsync(c => c.CharacterId == context.CharacterId);

            await dataLayer.ClearAsync(claim, AuditEntry(context, actorUserId: "officer-1"), CancellationToken.None);
        }

        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, context.TenantId);
        Assert.False(await verify.CharacterClaims.AnyAsync(c => c.CharacterId == context.CharacterId));
        Assert.True(await verify.AuditLogs.AnyAsync(a => a.TargetId == context.CharacterId));
    }

    [Fact]
    public async Task Clear_WhenTheAuditRowCannotBeWritten_LeavesTheClaimInPlace()
    {
        var context = await SeedClaimAsync();

        await using (var db = await TenantSeeding.OpenDbContextAsync(fixture, context.TenantId))
        {
            var dataLayer = CreateDataLayer(db);
            var claim = await db.CharacterClaims.SingleAsync(c => c.CharacterId == context.CharacterId);

            // An audit row whose TargetType exceeds its column. A contrived failure, deliberately —
            // what is under test is the transaction boundary, not this particular way of tripping it,
            // and it has to fail at SaveChanges (inside the unit) rather than earlier.
            var doomed = AuditEntry(context, actorUserId: "officer-1");
            doomed.TargetType = new string('x', 500);

            await Assert.ThrowsAnyAsync<Exception>(
                () => dataLayer.ClearAsync(claim, doomed, CancellationToken.None));
        }

        // The claim survived. Without the transaction the removal would have committed and the member
        // would have lost their claim with nothing recording why.
        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, context.TenantId);
        Assert.True(await verify.CharacterClaims.AnyAsync(c => c.CharacterId == context.CharacterId));
        Assert.False(await verify.AuditLogs.AnyAsync(a => a.TargetId == context.CharacterId));
    }

    private static CharacterClaimDataLayer CreateDataLayer(AegisScribeDbContext db) =>
        // Real repositories over one real DbContext — the arrangement that makes the two writes share a
        // SaveChanges is exactly what is under test, so substituting either repository would prove
        // nothing.
        new(new CharacterClaimRepository(db), new CharacterRepository(db), new AuditLogRepository(db));

    private static AuditLog AuditEntry(ClaimContext context, string actorUserId) => new()
    {
        Id = Guid.NewGuid(),
        ActorUserId = actorUserId,
        SubjectUserId = context.UserId,
        Action = AuditAction.CharacterClaimCleared,
        TargetType = nameof(CharacterClaim),
        TargetId = context.CharacterId,
        Before = $"Claimed by {context.UserId}",
        After = "Unclaimed",
        OccurredAt = DateTimeOffset.UtcNow,
    };

    private async Task<ClaimContext> SeedClaimAsync()
    {
        var tenant = await TenantSeeding.CreateTenantAsync(fixture);
        var (_, userId) = await GatewayLoginFlow.RegisterUserWithIdAsync(fixture);
        await TenantSeeding.AddMembershipAsync(fixture, tenant.Id, userId, TenantRole.Member);

        var realm = await CharacterSeeding.CreateRealmAsync(fixture);
        var character = await CharacterSeeding.CreateCharacterAsync(fixture, realm.Id);

        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenant.Id);
        db.CharacterClaims.Add(new CharacterClaim
        {
            Id = Guid.NewGuid(),
            CharacterId = character.Id,
            UserId = userId,
            ClaimedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        return new ClaimContext(tenant.Id, character.Id, userId);
    }

    private sealed record ClaimContext(Guid TenantId, Guid CharacterId, string UserId);
}
