using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pfm.Application.Abstractions;
using Pfm.Application.Budgets;
using Pfm.Application.Accounts;
using Pfm.Application.Accounts.Institutions;
using Pfm.Application.Contracts.Transactions;
using Pfm.Application.Identity;
using Pfm.Application.Transactions;
using Pfm.Application.Transactions.Attachments;
using Pfm.Application.Transactions.Categories;
using Pfm.Infrastructure.Accounts;
using Pfm.Infrastructure.Budgets;
using Pfm.Infrastructure.Email;
using Pfm.Infrastructure.ExchangeRates;
using Pfm.Infrastructure.Identity;
using Pfm.Infrastructure.Persistence;
using Pfm.Infrastructure.Storage;
using Pfm.Infrastructure.Transactions;

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

        services.AddScoped<IUserProfiles, UserProfiles>();
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<PfmDbContext>());
        services.AddScoped<IInstitutionRepository, InstitutionRepository>();
        services.AddScoped<IAccountRepository, AccountRepository>();

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
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<IQuickAddHistory, QuickAddHistory>();
        services.AddScoped<IAttachmentRepository, AttachmentRepository>();

        // Like the SPA's address: a host fails at start-up, not at the first upload.
        services.AddObjectStorage(validateOnStart: !IsGeneratingOpenApiDocument);
        services.AddScoped<ICategoryUsage, TransactionTotals>();
        services.AddScoped<IAccountTransactions, TransactionTotals>();
        services.AddScoped<ICategorySpending, TransactionTotals>();
        services.AddScoped<IBudgetRepository, BudgetRepository>();
        services.AddExchangeRates();

        return services;
    }

    private static bool IsGeneratingOpenApiDocument =>
        System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

    /// <summary>
    /// Registers users, their store and what the Identity module's use cases need. Hosts that sign
    /// people in add <see cref="AddSignIn"/> on the returned builder.
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

                // Five wrong passwords or two-factor codes in a row lock sign-in for the user for fifteen minutes:
                // long enough to make guessing hopeless, short enough that someone locking another user out on
                // purpose costs them little. A password reset lifts it.
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddEntityFrameworkStores<PfmDbContext>()
            .AddSignInPorts();

    /// <summary>
    /// For a host that signs people in: Identity's sign-in manager and token providers, password reset links
    /// that last an hour, and <see cref="ISessions"/> as its cookie. The host configures the cookie itself.
    /// </summary>
    public static IdentityBuilder AddSignIn(this IdentityBuilder builder)
    {
        builder.AddSignInManager().AddDefaultTokenProviders()
            .AddTokenProvider<PasswordResetTokenProvider>(PasswordResetTokenProviderOptions.ProviderName);
        builder.Services.Configure<IdentityOptions>(options => options.Tokens.PasswordResetTokenProvider = PasswordResetTokenProviderOptions.ProviderName);
        builder.Services.Replace(ServiceDescriptor.Scoped<ISessions, CookieSessions>());
        return builder;
    }

    private static IdentityBuilder AddSignInPorts(this IdentityBuilder builder)
    {
        // Only a host that signs people in replaces this, with AddSignIn().
        builder.Services.TryAddScoped<ISessions, NoSessions>();
        builder.Services.AddScoped<IUsers, IdentityUsers>();
        builder.Services.AddScoped<ITwoFactor, IdentityTwoFactor>();
        builder.Services.AddScoped<IPasswordPolicy, IdentityPasswordPolicy>();
        builder.Services.AddTransient<IIdentityEmails, IdentityEmails>();
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
