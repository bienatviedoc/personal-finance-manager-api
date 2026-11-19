using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pfm.Application.Abstractions;
using Pfm.Infrastructure.Email;
using Pfm.Infrastructure.Identity;
using Pfm.Infrastructure.Persistence;
using Pfm.Infrastructure.Transactions;
using Pfm.Application.Transactions.Categories;

namespace Pfm.Infrastructure;

public static class InfrastructureServices
{
    public const string ConnectionStringName = "Pfm";

    /// <summary>Tag on health checks that decide whether the host can serve requests.</summary>
    public const string ReadyTag = "ready";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Hosts that sign people in replace this with the signed-in user.
        services.TryAddScoped<ICurrentUser, NoCurrentUser>();
        services.AddScoped<ActingUser>();
        services.AddScoped<IActForUser>(provider => provider.GetRequiredService<ActingUser>());

        // Read lazily so a host can replace the connection string after registration (tests do).
        services.AddDbContext<PfmDbContext>((provider, options) =>
        {
            var connectionString = provider.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName)
                ?? throw new InvalidOperationException(
                    $"Connection string '{ConnectionStringName}' is not configured. Set ConnectionStrings__{ConnectionStringName}.");
            options.UseNpgsql(connectionString);
        });

        services.AddHealthChecks().AddDbContextCheck<PfmDbContext>("database", tags: [ReadyTag]);

        var app = services.AddOptions<AppOptions>()
            .BindConfiguration(AppOptions.Section)
            .Validate(options => Uri.TryCreate(options.PublicUrl, UriKind.Absolute, out _), "App:PublicUrl must be the SPA's absolute address.");

        // Fail at start-up, not at the first email. Building the OpenAPI document also starts the
        // host, with no configuration, so it is exempt.
        if (!IsGeneratingOpenApiDocument)
        {
            app.ValidateOnStart();
        }
        services.AddOptions<SmtpOptions>().BindConfiguration(SmtpOptions.Section);
        services.AddTransient<IEmailSender, SmtpEmailSender>();

        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<ICategoryUsage, NoTransactionsYet>();

        return services;
    }

    private static bool IsGeneratingOpenApiDocument =>
        System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

    /// <summary>
    /// Registers users and their store. Hosts that sign people in add the sign-in manager and
    /// token providers on the returned builder.
    /// </summary>
    public static IdentityBuilder AddUsers(this IServiceCollection services) =>
        services
            .AddIdentityCore<User>(options =>
            {
                options.User.RequireUniqueEmail = true;
                // The user name is the email address, validated as one; any character an address may hold is allowed.
                options.User.AllowedUserNameCharacters = "";
                options.SignIn.RequireConfirmedEmail = true;

                // Length is what makes a password strong; composition rules are not (NIST SP 800-63B).
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;
            })
            .AddEntityFrameworkStores<PfmDbContext>()
            .AddIdentityEmails();

    private static IdentityBuilder AddIdentityEmails(this IdentityBuilder builder)
    {
        builder.Services.AddTransient<IdentityEmails>();
        return builder;
    }

    /// <summary>Applies pending migrations. Run as its own deploy step, never at host start-up.</summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PfmDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
