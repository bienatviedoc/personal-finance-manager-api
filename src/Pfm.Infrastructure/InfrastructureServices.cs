using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pfm.Infrastructure.Persistence;

namespace Pfm.Infrastructure;

public static class InfrastructureServices
{
    public const string ConnectionStringName = "Pfm";

    /// <summary>Tag on health checks that decide whether the host can serve requests.</summary>
    public const string ReadyTag = "ready";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Read lazily so a host can replace the connection string after registration (tests do).
        services.AddDbContext<PfmDbContext>((provider, options) =>
        {
            var connectionString = provider.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName)
                ?? throw new InvalidOperationException(
                    $"Connection string '{ConnectionStringName}' is not configured. Set ConnectionStrings__{ConnectionStringName}.");
            options.UseNpgsql(connectionString);
        });

        services.AddHealthChecks().AddDbContextCheck<PfmDbContext>("database", tags: [ReadyTag]);

        return services;
    }

    /// <summary>Applies pending migrations. Run as its own deploy step, never at host start-up.</summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PfmDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
