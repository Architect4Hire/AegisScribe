using AegisScribe.Domain.Context;
using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Models.Identity;
using AegisScribe.Tests.Tenancy;
using Aspire.Hosting.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AegisScribe.Tests.Auth;

// Repository: against the real SQL Server the AppHost runs, with Identity wired the way the API wires
// it — so a broken lookup fails here, at the layer that owns it, not three layers up in an endpoint
// test. No HTTP at all, so this costs the collection's anonymous rate-limit budget nothing.
[Collection("AegisScribe API - Me")]
public class UserRepositoryTests(AegisScribeAppFixture fixture) : IAsyncLifetime
{
    // Satisfies Identity's default policy; deliberately not a realistic credential.
    private const string Pw = "Aa1!aegis";

    private ServiceProvider _services = null!;
    private AsyncServiceScope _scope;
    private UserRepository _repository = null!;

    public async Task InitializeAsync()
    {
        var connectionString = await fixture.App.GetConnectionStringAsync("aegisscribedb");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddAuthentication();
        services.AddDbContext<AegisScribeDbContext>(options => options.UseSqlServer(connectionString));
        // AegisScribeDbContext now requires ITenantContext (2.4); Identity/User tables aren't
        // tenant-scoped, so an always-unresolved TenantContext is correct here.
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddIdentityCore<ApplicationUser>(options => options.User.RequireUniqueEmail = true)
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AegisScribeDbContext>()
            .AddSignInManager();

        _services = services.BuildServiceProvider();
        _scope = _services.CreateAsyncScope();
        _repository = new UserRepository(
            _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
            _scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>());
    }

    public async Task DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _services.DisposeAsync();
    }

    private static ApplicationUser NewUser()
    {
        var email = $"repo-{Guid.NewGuid():N}@example.com";
        return new ApplicationUser { UserName = email, Email = email, CreatedAt = DateTimeOffset.UtcNow };
    }

    [Fact]
    public async Task Create_ThenFindByEmailAndById_RoundTrips()
    {
        var user = NewUser();

        var created = await _repository.CreateAsync(user, Pw, CancellationToken.None);

        Assert.True(created.Succeeded);
        Assert.Equal(user.Id, (await _repository.FindByEmailAsync(user.Email!, CancellationToken.None))?.Id);
        Assert.Equal(user.Email, (await _repository.FindByIdAsync(user.Id, CancellationToken.None))?.Email);
        Assert.Null(await _repository.FindByEmailAsync($"missing-{Guid.NewGuid():N}@example.com", CancellationToken.None));
    }

    [Fact]
    public async Task Create_DuplicateEmail_IsAFailedResult_NotAnException()
    {
        var user = NewUser();
        await _repository.CreateAsync(user, Pw, CancellationToken.None);

        var duplicate = await _repository.CreateAsync(
            new ApplicationUser { UserName = $"other-{Guid.NewGuid():N}", Email = user.Email }, Pw, CancellationToken.None);

        Assert.False(duplicate.Succeeded);
        Assert.Contains(duplicate.Errors, e => e.Code == nameof(IdentityErrorDescriber.DuplicateEmail));
    }

    [Fact]
    public async Task SignInChecks_AgainstTheStoredHash()
    {
        var user = NewUser();
        await _repository.CreateAsync(user, Pw, CancellationToken.None);

        Assert.True(await _repository.CheckPasswordSignInAsync(user, Pw, CancellationToken.None));
        Assert.False(await _repository.CheckPasswordSignInAsync(user, "wrong", CancellationToken.None));
        Assert.True(await _repository.CanSignInAsync(user, CancellationToken.None));
        Assert.Empty(await _repository.GetRolesAsync(user, CancellationToken.None));
    }

    [Fact]
    public async Task ChangePassword_WrongCurrent_IsPasswordMismatch()
    {
        var user = NewUser();
        await _repository.CreateAsync(user, Pw, CancellationToken.None);

        var result = await _repository.ChangePasswordAsync(user, "wrong", "Bb2@aegis", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch));
    }
}
