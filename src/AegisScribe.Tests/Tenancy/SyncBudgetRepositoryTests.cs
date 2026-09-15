using AegisScribe.Domain.Data;
using AegisScribe.Tests.Auth;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Tests.Tenancy;

// The atomicity claim, against real SQL Server — which is the only place it can be tested. The whole
// reason the budget lives in a table rather than IDistributedCache is that a read-then-write would let
// concurrent callers each see the budget as available and each spend it, and no in-memory provider
// reproduces the row locking that makes the conditional UPDATE correct.
[Collection("AegisScribe API")]
public class SyncBudgetRepositoryTests(AegisScribeAppFixture fixture)
{
    private const int Limit = 20;

    [Fact]
    public async Task ConcurrentConsumes_GrantExactlyTheBudgetAndNoMore()
    {
        // Twenty callers racing for a budget of twenty, four calls each: exactly five can win. A
        // read-then-write implementation passes this test occasionally and fails it under load, which
        // is why the assertion is on the exact count rather than "roughly".
        var tenant = await TenantSeeding.CreateTenantAsync(fixture);
        var now = DateTimeOffset.UtcNow;

        var attempts = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            // A context per caller: this is simulating separate requests, and sharing one would
            // serialise them through the change tracker and prove nothing.
            await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenant.Id);
            return await new SyncBudgetRepository(db)
                .TryConsumeAsync(tenant.Id, calls: 4, Limit, now.AddDays(-1), now, CancellationToken.None);
        }));

        Assert.Equal(5, attempts.Count(granted => granted));

        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, tenant.Id);
        var window = await verify.TenantSyncBudgetWindows.SingleAsync(w => w.TenantId == tenant.Id);

        // And the stored total agrees with the number of winners — no lost updates in either direction.
        Assert.Equal(Limit, window.CallsConsumed);
    }

    [Fact]
    public async Task ConcurrentFirstConsumes_ProduceOneWindowNotSeveral()
    {
        // The insert race specifically. Every caller finds no row and tries to create one; the unique
        // index on TenantId is what turns the losers into retries instead of extra windows, each of
        // which would have granted its own full budget.
        var tenant = await TenantSeeding.CreateTenantAsync(fixture);
        var now = DateTimeOffset.UtcNow;

        await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenant.Id);
            await new SyncBudgetRepository(db)
                .TryConsumeAsync(tenant.Id, calls: 1, Limit, now.AddDays(-1), now, CancellationToken.None);
        }));

        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, tenant.Id);
        var windows = await verify.TenantSyncBudgetWindows.Where(w => w.TenantId == tenant.Id).ToListAsync();

        Assert.Single(windows);
        Assert.Equal(8, windows[0].CallsConsumed);
    }

    [Fact]
    public async Task AnExpiredWindow_ResetsRatherThanStayingExhausted()
    {
        var tenant = await TenantSeeding.CreateTenantAsync(fixture);
        var now = DateTimeOffset.UtcNow;

        await ConsumeAsync(tenant.Id, calls: Limit, windowStart: now.AddDays(-1), now);
        Assert.False(await ConsumeAsync(tenant.Id, calls: 1, windowStart: now.AddDays(-1), now));

        // A day later the window has rolled over. The reset happens inside the same UPDATE as the
        // spend, so there is no moment where the row reads as empty but has not been charged.
        var later = now.AddDays(1).AddMinutes(1);
        Assert.True(await ConsumeAsync(tenant.Id, calls: 1, windowStart: later.AddDays(-1), later));

        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, tenant.Id);
        var window = await verify.TenantSyncBudgetWindows.SingleAsync(w => w.TenantId == tenant.Id);

        Assert.Equal(1, window.CallsConsumed);
    }

    [Fact]
    public async Task ASpendLargerThanTheWholeWindow_IsRefusedRatherThanCreatingAnOverdraft()
    {
        var tenant = await TenantSeeding.CreateTenantAsync(fixture);
        var now = DateTimeOffset.UtcNow;

        Assert.False(await ConsumeAsync(tenant.Id, calls: Limit + 1, windowStart: now.AddDays(-1), now));

        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, tenant.Id);
        Assert.False(await verify.TenantSyncBudgetWindows.AnyAsync(w => w.TenantId == tenant.Id));
    }

    private async Task<bool> ConsumeAsync(Guid tenantId, int calls, DateTimeOffset windowStart, DateTimeOffset now)
    {
        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenantId);
        return await new SyncBudgetRepository(db)
            .TryConsumeAsync(tenantId, calls, Limit, windowStart, now, CancellationToken.None);
    }
}
