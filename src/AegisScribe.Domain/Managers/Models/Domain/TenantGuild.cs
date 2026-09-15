namespace AegisScribe.Domain.Managers.Models.Domain;

// Which in-game guilds a community claims as its own — the piece that keeps Guild itself global. The
// guild is a fact about the world; WHICH guilds a community follows is a fact about that community.
//
// Many-to-many on purpose: a community may span two guilds or run two teams inside one, and a single
// GuildId on Tenant would have to be migrated away from the first time either happened.
public class TenantGuild : ITenantScoped
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    // The FK direction that is allowed: tenant-scoped holding a reference INTO the global zone. A
    // global entity must never hold one back the other way (tenancy.md).
    public Guid GuildId { get; set; }

    public Guild Guild { get; set; } = null!;

    public DateTimeOffset LinkedAt { get; set; }
}
