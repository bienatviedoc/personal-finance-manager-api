using Microsoft.EntityFrameworkCore;

namespace Pfm.Infrastructure.Persistence;

public sealed class PfmDbContext(DbContextOptions<PfmDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Trigram indexes back free-text search on payee and note.
        modelBuilder.HasPostgresExtension("pg_trgm");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PfmDbContext).Assembly);
    }
}
