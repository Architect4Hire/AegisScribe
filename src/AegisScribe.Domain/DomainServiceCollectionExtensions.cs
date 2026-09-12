using AegisScribe.Domain.Business;
using AegisScribe.Domain.Context;
using AegisScribe.Domain.Data;
using AegisScribe.Domain.Facade;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AegisScribe.Domain;

public static class DomainServiceCollectionExtensions
{
    // Every layer of the stack, registered once for every host that serves requests through it. The
    // host still owns what is host-shaped: the DbContext (Aspire's AddSqlServerDbContext), Identity
    // (AddIdentityCore + AddSignInManager — the user repository needs both) and an ICurrentUser
    // implementation. Under ValidateOnBuild a host that calls this without those fails at startup,
    // which is the point.
    public static IServiceCollection AddAegisScribeDomain(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        // TenantContext is registered both as itself (so the tenant-resolution middleware can call
        // SetTenant) and as ITenantContext (the read-only view everything else depends on). Same scoped
        // instance either way; ITenantContext has no setter, so code that only holds the interface
        // physically cannot populate or override the resolved tenant.
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

        // Stamps/validates TenantId on SaveChanges (tenancy.md) — the host wires it into the
        // DbContext's AddInterceptors(...) since the DbContext itself stays host-owned.
        services.AddScoped<TenantStampingInterceptor>();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserDataLayer, UserDataLayer>();
        services.AddScoped<IAuthBusiness, AuthBusiness>();
        services.AddScoped<IAuthFacade, AuthFacade>();
        services.AddScoped<IMeBusiness, MeBusiness>();
        services.AddScoped<IMeFacade, MeFacade>();

        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<ITenantDataLayer, TenantDataLayer>();
        services.AddScoped<ITenantBusiness, TenantBusiness>();
        services.AddScoped<ITenantResolutionFacade, TenantResolutionFacade>();
        services.AddScoped<ITenantFacade, TenantFacade>();

        services.AddScoped<ICharacterRepository, CharacterRepository>();
        services.AddScoped<ICharacterDataLayer, CharacterDataLayer>();
        services.AddScoped<ICharacterBusiness, CharacterBusiness>();
        services.AddScoped<ICharacterFacade, CharacterFacade>();

        services.AddValidatorsFromAssembly(typeof(DomainServiceCollectionExtensions).Assembly);

        return services;
    }
}
