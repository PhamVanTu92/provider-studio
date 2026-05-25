using Microsoft.EntityFrameworkCore;
using ProviderStudio.Data.Entities;

namespace ProviderStudio.Data;

public sealed class StudioDbContext : DbContext
{
    public DbSet<ProviderEntity>      Providers      => Set<ProviderEntity>();
    public DbSet<DbConnectionEntity>  DbConnections  => Set<DbConnectionEntity>();
    public DbSet<OperationEntity>     Operations     => Set<OperationEntity>();
    public DbSet<ParamMappingEntity>  ParamMappings  => Set<ParamMappingEntity>();

    public StudioDbContext(DbContextOptions<StudioDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder model)
    {
        // ── Providers ─────────────────────────────────────────────────────────
        model.Entity<ProviderEntity>(e =>
        {
            e.HasIndex(p => p.ClientId).IsUnique();
            e.HasMany(p => p.DbConnections)
             .WithOne(c => c.Provider)
             .HasForeignKey(c => c.ProviderId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(p => p.Operations)
             .WithOne(o => o.Provider)
             .HasForeignKey(o => o.ProviderId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── DbConnections ─────────────────────────────────────────────────────
        model.Entity<DbConnectionEntity>(e =>
        {
            e.HasMany(c => c.Operations)
             .WithOne(o => o.DbConnection)
             .HasForeignKey(o => o.DbConnectionId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Operations ────────────────────────────────────────────────────────
        model.Entity<OperationEntity>(e =>
        {
            e.HasIndex(o => new { o.ProviderId, o.Pattern }).IsUnique();
            e.HasMany(o => o.ParamMappings)
             .WithOne(p => p.Operation)
             .HasForeignKey(p => p.OperationId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── ParamMappings ─────────────────────────────────────────────────────
        model.Entity<ParamMappingEntity>(e =>
        {
            e.HasIndex(p => new { p.OperationId, p.JsonPath });
        });
    }

    /// <summary>Auto-migrate + enable WAL mode on startup.</summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await Database.MigrateAsync(ct);
        await Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
    }
}
