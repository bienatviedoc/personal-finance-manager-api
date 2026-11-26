using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Identity;
using Pfm.Application.Contracts.Transactions;
using Pfm.Application.Identity;
using Pfm.Domain;
using Pfm.Domain.Accounts;
using Pfm.Domain.Budgets;
using Pfm.Domain.Transactions;
using Pfm.Infrastructure.Persistence;

namespace Pfm.Infrastructure.SampleData;

/// <summary>
/// Fills a local environment with a year of sample data for one new user (spec section 13): institutions,
/// accounts, the default categories, a budget on the main expense categories in every month, and a year of
/// transactions up to today, with income, bills, transfers, splits and refunds among everyday spending. The data
/// is made from a fixed seed, so two runs on the same day make the same year.
/// <para>
/// It is for development and load tests, not for users: it registers the user with their email already
/// confirmed and writes the rest straight through the model, as the user, in large batches. Every entity is
/// still built by its domain type, so nothing breaks a rule the app keeps.
/// </para>
/// </summary>
public static class SampleDataSeeder
{
    /// <summary>The fewest transactions a seeded year holds: its monthly income, bills and transfers.</summary>
    public const int FewestTransactions = 160;

    private const int BatchSize = 2_000;

    public static async Task<SeededSampleData> SeedSampleDataAsync(
        this IServiceProvider services, SampleDataOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Transactions is < FewestTransactions)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Transactions, $"Seed at least {FewestTransactions} transactions.");
        }

        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var userId = await RegisterAsync(provider, EmailAddress.Normalize(options.Email), options.Password, cancellationToken);

        using var _ = provider.GetRequiredService<IActForUser>().ActFor(userId);
        var db = provider.GetRequiredService<PfmDbContext>();
        var now = provider.GetRequiredService<TimeProvider>().GetUtcNow();
        var today = await provider.GetRequiredService<IUserCalendar>().TodayAsync(cancellationToken);
        var baseCurrency = await provider.GetRequiredService<IBaseCurrency>().OfCurrentUserAsync(cancellationToken);
        var categories = await db.Set<Category>().AsNoTracking().ToListAsync(cancellationToken);

        var year = new SampleYear(today, baseCurrency, now, categories);
        db.AddRange(year.Institutions);
        db.AddRange(year.Accounts);
        db.AddRange(year.Budgets);
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();

        var recorded = 0;
        foreach (var batch in year.Transactions(options.Transactions).Chunk(BatchSize))
        {
            db.AddRange(batch);
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
            recorded += batch.Length;
        }

        return new SeededSampleData(userId, options.Email, year.Accounts.Count, year.Budgets.Count, recorded, year.From, today);
    }

    /// <summary>Registers the user with the default categories, as registration does, and confirms their email.</summary>
    private static async Task<Guid> RegisterAsync(IServiceProvider provider, string email, string password, CancellationToken cancellationToken)
    {
        var users = provider.GetRequiredService<IUsers>();
        if (await users.FindByEmailAsync(email) is not null)
        {
            throw new InvalidOperationException($"{email} already has a user. Seed another email, or start from an empty database.");
        }

        var unitOfWork = provider.GetRequiredService<IUnitOfWork>();
        Guid userId;
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var created = await users.CreateAsync(email, password, Locale.En);
            if (created.User is not { } user)
            {
                throw new InvalidOperationException($"No user could be made for {email}: {string.Join(" ", created.Errors)}");
            }

            userId = user.Id;
            await provider.GetRequiredService<IDefaultCategories>().CreateForNewUserAsync(userId, Locale.En, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await users.ConfirmEmailAsync(userId, await users.CreateEmailConfirmationTokenAsync(userId));
        return userId;
    }
}
