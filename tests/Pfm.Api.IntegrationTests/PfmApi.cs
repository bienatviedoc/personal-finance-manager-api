using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Pfm.Application.Abstractions;
using Pfm.Infrastructure;
using Pfm.Infrastructure.Identity;
using Pfm.Infrastructure.Observability;
using Serilog.Core;
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

    /// <summary>
    /// A route that exists only in tests: answers with the user the request's ICurrentUser resolves
    /// to, which is who user-owned data is filtered by.
    /// </summary>
    public const string CurrentUserPath = "/__tests/current-user";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>Where emails' links point: the SPA's address.</summary>
    public const string PublicUrl = "https://pfm.test";

    private WebApplicationFactory<Program>? _factory;

    public IServiceProvider Services => Factory.Services;

    /// <summary>The test database server, for tests that need a database of their own on it.</summary>
    public string ConnectionString => _database.GetConnectionString();

    /// <summary>Every email the API has sent, instead of delivering them.</summary>
    public CapturedEmails Emails { get; } = new();

    /// <summary>Every span and log event the API exported, as well as sending them on.</summary>
    public CapturedTelemetry Telemetry { get; } = new();

    private WebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException("The API has not been started.");

    public HttpClient CreateClient() => Factory.CreateClient();

    /// <summary>A client that behaves like one browser: it talks HTTPS and keeps its cookies.</summary>
    public Browser CreateBrowser() => CreateBrowser(Factory);

    /// <summary>The password every test user registers with.</summary>
    public const string Password = "correct horse battery staple";

    public static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    /// <summary>The last link emailed to an address.</summary>
    public Uri LastLinkTo(string email) => CapturedEmails.Links(Emails.To(email)[^1]).Single();

    /// <summary>What the SPA's verify-email screen posts: the user id and token from the emailed link.</summary>
    public static object VerificationFrom(Uri link)
    {
        var query = System.Web.HttpUtility.ParseQueryString(link.Query);
        return new { userId = query["userId"], token = query["token"] };
    }

    /// <summary>A browser (a new one, or the given one) for a new user who has registered and confirmed their email, not yet signed in.</summary>
    public async Task<(Browser Browser, string Email)> RegisteredAndVerifiedAsync(Browser? browser = null, string? locale = null)
    {
        browser ??= CreateBrowser();
        var email = NewEmail();
        object registration = locale is null ? new { email, password = Password } : new { email, password = Password, locale };
        (await browser.PostAsync("/api/v1/auth/register", registration)).EnsureSuccessStatusCode();
        (await browser.PostAsync("/api/v1/auth/verify-email", VerificationFrom(LastLinkTo(email)))).EnsureSuccessStatusCode();
        return (browser, email);
    }

    /// <summary>A browser signed in as a new user: a new one on this API, or the given one (perhaps on another copy of the API).</summary>
    public async Task<(Browser Browser, string Email)> SignedInAsync(Browser? browser = null, string? locale = null)
    {
        (browser, var email) = await RegisteredAndVerifiedAsync(browser, locale);
        (await browser.PostAsync("/api/v1/auth/login", new { email, password = Password })).EnsureSuccessStatusCode();
        return (browser, email);
    }

    /// <summary>
    /// Turns two-factor sign-in on for the user signed in on this browser, the way the security screen does: set up,
    /// then confirm with a code. Answers the authenticator's key (for <see cref="Totp"/>) and the recovery codes.
    /// </summary>
    public async Task<(string Key, string[] RecoveryCodes)> TurnOnTwoFactorAsync(Browser browser)
    {
        var setup = await (await browser.PostAsync("/api/v1/me/two-factor/setup")).Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var key = setup.GetProperty("key").GetString()!;
        var enabled = await browser.PostAsync("/api/v1/me/two-factor/enable", new { code = Totp.CodeFor(key) });
        enabled.EnsureSuccessStatusCode();
        var codes = (await enabled.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("recoveryCodes");
        return (key, [.. codes.EnumerateArray().Select(code => code.GetString()!)]);
    }

    /// <summary>
    /// A second copy of the API in the Development environment, used over plain HTTP the way the
    /// local stack is (no TLS in front of it).
    /// </summary>
    public Browser CreateDevelopmentBrowserOverHttp()
    {
        var development = CreateFactory(ConnectionString, Emails, environment: "Development");
        _extraFactories.Add(development);
        return new Browser(development.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost"), HandleCookies = true }));
    }

    private readonly List<WebApplicationFactory<Program>> _extraFactories = [];

    /// <summary>A second copy of the API whose database cannot be reached.</summary>
    public WebApplicationFactory<Program> WithUnreachableDatabase() =>
        CreateFactory("Host=127.0.0.1;Port=1;Database=pfm;Username=pfm;Password=pfm;Timeout=2", new CapturedEmails());

    /// <summary>A second copy of the API that reports errors to the given error tracker.</summary>
    public WebApplicationFactory<Program> ReportingErrorsTo(FakeErrorTracker tracker) =>
        CreateFactory(ConnectionString, Emails, settings: new() { [ObservabilitySetup.ErrorTrackerDsnSetting] = tracker.Dsn });

    /// <summary>A second copy of the API whose password reset links have expired by the time anyone follows them.</summary>
    public WebApplicationFactory<Program> WhereResetLinksExpireAtOnce()
    {
        var factory = CreateFactory(ConnectionString, Emails, configure: services =>
            services.Configure<PasswordResetTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.Zero));
        _extraFactories.Add(factory);
        return factory;
    }

    /// <summary>A second copy of the API where a sign-in lock lasts only this long.</summary>
    public WebApplicationFactory<Program> WhereSignInLocksFor(TimeSpan time)
    {
        var factory = CreateFactory(ConnectionString, Emails, configure: services =>
            services.Configure<Microsoft.AspNetCore.Identity.IdentityOptions>(options => options.Lockout.DefaultLockoutTimeSpan = time));
        _extraFactories.Add(factory);
        return factory;
    }

    /// <summary>
    /// A second copy of the API where a client gets only this many attempts at the auth endpoints in each window.
    /// The copy the tests share allows so many that no test meets the limit by accident.
    /// </summary>
    public WebApplicationFactory<Program> WithAuthRateLimit(int attempts, TimeSpan window)
    {
        var factory = CreateFactory(ConnectionString, Emails, settings: new()
        {
            ["RateLimiting:Auth:PermitLimit"] = attempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RateLimiting:Auth:WindowSeconds"] = ((int)window.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture),
        });
        _extraFactories.Add(factory);
        return factory;
    }

    /// <summary>The header a test browser names its network address in; see <see cref="ClientAddressFilter"/>.</summary>
    private const string ClientAddressHeader = "X-Test-Client-Address";

    /// <summary>A browser like <see cref="CreateBrowser"/>, on another copy of the API, perhaps calling from a network address of its own.</summary>
    public static Browser CreateBrowser(WebApplicationFactory<Program> on, string? clientAddress = null)
    {
        var client = on.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        if (clientAddress is not null)
        {
            client.DefaultRequestHeaders.Add(ClientAddressHeader, clientAddress);
        }

        return new Browser(client);
    }

    public async Task InitializeAsync()
    {
        await _database.StartAsync();
        _factory = CreateFactory(_database.GetConnectionString(), Emails, Telemetry);
        await _factory.Services.MigrateDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        foreach (var factory in _extraFactories)
        {
            await factory.DisposeAsync();
        }

        await _database.DisposeAsync();
    }

    private static WebApplicationFactory<Program> CreateFactory(string connectionString, CapturedEmails emails, CapturedTelemetry? telemetry = null, string environment = "Production", Dictionary<string, string>? settings = null, Action<IServiceCollection>? configure = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting($"ConnectionStrings:{InfrastructureServices.ConnectionStringName}", connectionString);
            builder.UseSetting("App:PublicUrl", PublicUrl);
            // Every test calls from the same address, so the real limit would have them refuse each other.
            builder.UseSetting("RateLimiting:Auth:PermitLimit", "1000000");
            foreach (var (key, value) in settings ?? [])
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureServices(services =>
            {
                services.AddTransient<IStartupFilter, ThrowingRouteFilter>();
                services.AddTransient<IStartupFilter, ClientAddressFilter>();
                // Check the session against the user's security stamp on every request, not every few minutes.
                services.Configure<Microsoft.AspNetCore.Identity.SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(emails);
                if (telemetry is not null)
                {
                    // Added to the API's own pipelines, so only what they export is captured.
                    services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddProcessor(new SimpleActivityExportProcessor(telemetry.SpanExporter)));
                    services.AddSingleton(telemetry.LogSink);
                }

                configure?.Invoke(services);
            });
        });

    /// <summary>The in-memory test server has no network, so a browser says which address it calls from.</summary>
    private sealed class ClientAddressFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(ClientAddressHeader, out var address))
                {
                    context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(address.ToString());
                }

                await nextMiddleware(context);
            });
            next(app);
        };
    }

    private sealed class ThrowingRouteFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Path == ThrowingPath)
                {
                    throw new InvalidOperationException("secret internal detail") { Data = { ["payee"] = "Corner Bakery", ["sentry:context:payment"] = "Corner Bakery", ["sentry:tag:payee"] = "Corner Bakery" } };
                }

                if (context.Request.Path == CurrentUserPath)
                {
                    var currentUser = context.RequestServices.GetRequiredService<ICurrentUser>();
                    await context.Response.WriteAsync(currentUser.UserId?.ToString() ?? "");
                    return;
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
