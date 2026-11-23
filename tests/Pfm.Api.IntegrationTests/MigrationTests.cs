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

    [Theory]
    [InlineData("""btree ("UserId", "Date" DESC, "Id" DESC)""")]
    [InlineData("""gin ("Payee" gin_trgm_ops, "Note" gin_trgm_ops)""")]
    public async Task The_transaction_list_and_its_search_are_backed_by_the_indexes_the_specification_names(string index)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PfmDbContext>();

        var indexes = await db.Database
            .SqlQuery<string>($"SELECT indexdef AS \"Value\" FROM pg_indexes WHERE tablename = 'Transactions'")
            .ToListAsync();

        Assert.Contains(indexes, definition => definition.EndsWith($"USING {index}", StringComparison.Ordinal));
    }
}
