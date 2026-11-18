using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pfm.Infrastructure.Persistence;

namespace Pfm.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class MigrationTests(PfmApi api)
{
    [Fact]
    public async Task Migrating_leaves_no_pending_migrations()
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PfmDbContext>();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Every_model_change_has_a_migration()
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PfmDbContext>();

        Assert.False(db.Database.HasPendingModelChanges(), "The model has changes with no migration. Run `dotnet ef migrations add`.");
    }

    [Fact]
    public async Task The_trigram_extension_used_by_search_is_installed()
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PfmDbContext>();

        var installed = await db.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_extension WHERE extname = 'pg_trgm'")
            .SingleAsync();

        Assert.Equal(1, installed);
    }
}
