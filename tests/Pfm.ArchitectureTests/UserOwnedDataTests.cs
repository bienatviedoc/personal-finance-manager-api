namespace Pfm.ArchitectureTests;

/// <summary>
/// The context keeps every query on user-owned data to the signed-in user (README, "User-owned data").
/// <c>IgnoreQueryFilters()</c> and raw SQL step outside that, so each use of one is listed here, with why it
/// may. A new use fails these tests until it is argued for in the README and added.
/// </summary>
public class UserOwnedDataTests
{
    /// <summary>
    /// The five queries that look past the user filter, a file listed once for each. A job that runs for nobody
    /// asks which users have something for it to do: statements committed and waiting to be imported, recurring
    /// rules with an occurrence due, transactions flagged not converted, any transaction for the integrity check to
    /// verify, or transactions deleted more than 30 days ago for the purge to remove. Each answers user ids and no
    /// row; the job then acts for each user in turn, inside the filter.
    /// </summary>
    private static readonly string[] MayIgnoreQueryFilters =
    [
        "src/Pfm.Infrastructure/Imports/ImportBatchRepository.cs",
        "src/Pfm.Infrastructure/Transactions/RecurringRuleRepository.cs",
        "src/Pfm.Infrastructure/Transactions/TransactionRepository.cs",
        "src/Pfm.Infrastructure/Transactions/TransactionRepository.cs",
        "src/Pfm.Infrastructure/Transactions/TransactionRepository.cs",
    ];

    /// <summary>Exchange rates belong to no user; storing them is one INSERT … ON CONFLICT DO NOTHING.</summary>
    private static readonly string[] MayUseRawSql = ["src/Pfm.Infrastructure/ExchangeRates/ExchangeRateRepository.cs"];

    private static readonly string[] RawSqlCalls = ["FromSql", "SqlQuery", "ExecuteSql", "ExecuteSqlRaw", "FromSqlRaw", "SqlQueryRaw"];

    [Fact]
    public void Only_the_queries_that_answer_which_users_a_job_has_work_for_ignore_the_user_filter()
    {
        var uses = Layout.SourceFiles()
            .SelectMany(file => Enumerable.Repeat(file.Key, Occurrences(file.Value, ".IgnoreQueryFilters(")))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(MayIgnoreQueryFilters, uses);
    }

    [Fact]
    public void Raw_SQL_is_used_only_on_data_that_belongs_to_no_user()
    {
        var uses = Layout.SourceFiles()
            .Where(file => RawSqlCalls.Any(call => file.Value.Contains($".{call}(", StringComparison.Ordinal) || file.Value.Contains($".{call}<", StringComparison.Ordinal)))
            .Select(file => file.Key)
            .ToArray();

        Assert.Equal(MayUseRawSql, uses);
    }

    private static int Occurrences(string text, string of) => text.Split(of).Length - 1;
}
