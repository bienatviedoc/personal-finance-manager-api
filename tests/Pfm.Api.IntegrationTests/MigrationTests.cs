using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pfm.Domain;
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

    public static TheoryData<Currency> EveryCurrency => [.. Enum.GetValues<Currency>()];

    /// <summary>
    /// The database shares a split's converted amount among its lines rounded to the base currency's decimals, which it
    /// reads from its function <c>CurrencyDecimals</c>: it must answer what the domain does for every currency, so a
    /// currency added to one and not the other fails here.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryCurrency))]
    public async Task The_database_gives_each_currency_the_decimals_the_domain_gives_it(Currency currency)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PfmDbContext>();

        var decimals = await db.Database
            .SqlQuery<int?>($"SELECT \"CurrencyDecimals\"({currency.ToString()}) AS \"Value\"")
            .SingleAsync();

        Assert.Equal(currency.DecimalPlaces(), decimals);
    }

    [Theory]
    [InlineData("Transactions", """btree ("UserId", "Date" DESC, "Id" DESC)""")]
    [InlineData("Transactions", """gin ("Payee" gin_trgm_ops, "Note" gin_trgm_ops)""")]
    [InlineData("TransactionLines", """gin ("Note" gin_trgm_ops)""")]
    public async Task The_transaction_list_and_its_search_are_backed_by_the_indexes_the_specification_names(string table, string index)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PfmDbContext>();

        var indexes = await db.Database
            .SqlQuery<string>($"SELECT indexdef AS \"Value\" FROM pg_indexes WHERE tablename = {table}")
            .ToListAsync();

        Assert.Contains(indexes, definition => definition.EndsWith($"USING {index}", StringComparison.Ordinal));
    }
}
