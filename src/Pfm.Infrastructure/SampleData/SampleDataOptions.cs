namespace Pfm.Infrastructure.SampleData;

/// <summary>Who the sample data is for, and how much of it to make.</summary>
/// <param name="Email">The new user's email address; it must not have a user yet.</param>
/// <param name="Password">The new user's password, which the password policy must accept.</param>
/// <param name="Transactions">
/// How many transactions to record over the year, each leg of a transfer counting as one; at least
/// <see cref="SampleDataSeeder.FewestTransactions"/>. Left out, a realistic year of everyday spending: a few
/// transactions a day.
/// </param>
public sealed record SampleDataOptions(string Email, string Password, int? Transactions = null)
{
    /// <summary>The user <c>Pfm.Api seed</c> makes when it is not told who: for a local environment only.</summary>
    public const string DefaultEmail = "sample@example.com";

    /// <summary>The password of <see cref="DefaultEmail"/>.</summary>
    public const string DefaultPassword = "sample data password";
}

/// <summary>What was seeded: the new user, and how much they have.</summary>
public sealed record SeededSampleData(Guid UserId, string Email, int Accounts, int Budgets, int Transactions, DateOnly From, DateOnly To);
