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

        // Scoped alongside ICharacterRepository, and that matters: both resolve the same scoped
        // AegisScribeDbContext, which is what lets CharacterDataLayer stage a realm write and a
        // character write inside one repository's transaction callback.
        services.AddScoped<IRealmRepository, RealmRepository>();

        services.AddScoped<ICharacterRepository, CharacterRepository>();
        services.AddScoped<ICharacterDataLayer, CharacterDataLayer>();
        services.AddScoped<ICharacterBusiness, CharacterBusiness>();
        services.AddScoped<ICharacterFacade, CharacterFacade>();

        // The per-tenant sync budget (6.6) and its first caller. Scoped like everything else in the
        // request stack: ITenantSyncBudget reads the tenant id from its caller, never from ambient
        // state, so a background job could use it too without a resolved tenant context.
        // Guilds (6.6b). Two repositories because two zones: IGuildRepository writes the global Guild
        // and GuildMember, ITenantGuildRepository writes the tenant-scoped link between them.
        services.AddScoped<IGuildRepository, GuildRepository>();
        services.AddScoped<ITenantGuildRepository, TenantGuildRepository>();
        services.AddScoped<IGuildSyncDataLayer, GuildSyncDataLayer>();
        services.AddScoped<IGuildBusiness, GuildBusiness>();
        services.AddScoped<IGuildFacade, GuildFacade>();

        // Ranks (7.1) — the community's own rank ladder, and the first caller of the tenant-prefixed
        // cache key convention.
        services.AddScoped<ITenantRankRepository, TenantRankRepository>();
        services.AddScoped<ITenantRankDataLayer, TenantRankDataLayer>();
        services.AddScoped<ITenantRankBusiness, TenantRankBusiness>();
        services.AddScoped<ITenantRankFacade, TenantRankFacade>();

        // The roster (7.2). IRosterEntryRepository has two consumers: its own data layer, and
        // TenantRankDataLayer — which is what lets a rank deletion ask how many entries still hold the
        // rank without TenantRankRepository querying a table it doesn't own.
        services.AddScoped<IRosterEntryRepository, RosterEntryRepository>();
        services.AddScoped<IRosterEntryDataLayer, RosterEntryDataLayer>();
        services.AddScoped<IRosterBusiness, RosterBusiness>();
        services.AddScoped<IRosterFacade, RosterFacade>();

        // Character claims (7.2b). IAuditLogRepository is registered alongside rather than in its own
        // vertical because it has no vertical: it is write-only, it stages into whichever operation is
        // being audited, and its reader arrives in 14.2. Scoped like everything else here, which is
        // what lets an audit row and the action it records share one SaveChanges.
        // What this community calls the guilds' in-game ranks (7.5). Its own vertical because it is
        // its own tenant-scoped entity, even though the roster read is its busiest consumer.
        services.AddScoped<IGuildRankNameRepository, GuildRankNameRepository>();
        services.AddScoped<IGuildRankNameDataLayer, GuildRankNameDataLayer>();
        services.AddScoped<IGuildRankNameBusiness, GuildRankNameBusiness>();
        services.AddScoped<IGuildRankNameFacade, GuildRankNameFacade>();

        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<ICharacterClaimRepository, CharacterClaimRepository>();
        services.AddScoped<ICharacterClaimDataLayer, CharacterClaimDataLayer>();
        services.AddScoped<ICharacterClaimBusiness, CharacterClaimBusiness>();
        services.AddScoped<ICharacterClaimFacade, CharacterClaimFacade>();

        services.AddScoped<ISyncBudgetRepository, SyncBudgetRepository>();
        services.AddScoped<ISyncBudgetDataLayer, SyncBudgetDataLayer>();
        services.AddScoped<ITenantSyncBudget, TenantSyncBudget>();
        services.AddScoped<ISyncBusiness, SyncBusiness>();
        services.AddScoped<ISyncFacade, SyncFacade>();

        services.AddValidatorsFromAssembly(typeof(DomainServiceCollectionExtensions).Assembly);

        return services;
    }
}
