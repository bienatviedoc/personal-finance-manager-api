using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Pfm.Infrastructure;
using Testcontainers.PostgreSql;

namespace Pfm.Api.IntegrationTests;

/// <summary>
/// The API running in-process against a real, migrated PostgreSQL in a container.
/// Shared by every test class in the <see cref="ApiCollection"/>.
/// </summary>
public sealed class PfmApi : IAsyncLifetime
{
    /// <summary>A route that exists only in tests, for exercising the unhandled-exception path.</summary>
    public const string ThrowingPath = "/__tests/throw";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private WebApplicationFactory<Program>? _factory;

    public IServiceProvider Services => Factory.Services;

    private WebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException("The API has not been started.");

    public HttpClient CreateClient() => Factory.CreateClient();

    /// <summary>A second copy of the API whose database cannot be reached.</summary>
    public WebApplicationFactory<Program> WithUnreachableDatabase() =>
        CreateFactory("Host=127.0.0.1;Port=1;Database=pfm;Username=pfm;Password=pfm;Timeout=2");

    public async Task InitializeAsync()
    {
        await _database.StartAsync();
        _factory = CreateFactory(_database.GetConnectionString());
        await _factory.Services.MigrateDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _database.DisposeAsync();
    }

    private static WebApplicationFactory<Program> CreateFactory(string connectionString) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting($"ConnectionStrings:{InfrastructureServices.ConnectionStringName}", connectionString);
            builder.ConfigureServices(services => services.AddTransient<IStartupFilter, ThrowingRouteFilter>());
        });

    private sealed class ThrowingRouteFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Path == ThrowingPath)
                {
                    throw new InvalidOperationException("secret internal detail");
                }

                await nextMiddleware(context);
            });
        };
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<PfmApi>
{
    public const string Name = "API";
}
