using Pfm.Domain.Imports;

namespace Pfm.Domain.UnitTests.Imports;

/// <summary>
/// An import batch is one uploaded statement file and the transactions created from it. Committing chooses the
/// rows to import; the worker then imports them a few at a time, and the batch says how far it has got.
/// </summary>
public class ImportBatchTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 27);
    private static readonly Guid Account = Guid.CreateVersion7();
    private static readonly Guid Groceries = Guid.CreateVersion7();
    private static readonly Guid Salary = Guid.CreateVersion7();

    private const string File = "Date,Details,Amount\n02/08/2026,A,-1000\n03/08/2026,B,-2000\nbad,C,-3\n05/08/2026,SALARY,5000\n06/08/2026,D,-4000\n";

    private static readonly StatementMapping Mapping = new(
        Separator.Comma, HasHeaderRow: true, DateColumn: 0, DescriptionColumn: 1, AmountColumn: 2, DateFormat: DateFormat.DayMonthYear);

    private static (ImportBatch Batch, Statement Statement) Uploaded()
    {
        var batch = ImportBatch.Upload(Account, "statement.csv", File, Now);
        return (batch, batch.Read(Mapping, Currency.VND, Today));
    }

    [Fact]
    public void A_new_batch_holds_the_file_until_it_is_committed()
    {
        var (batch, statement) = Uploaded();

        Assert.Equal((ImportBatchStatus.Uploaded, "statement.csv", Account), (batch.Status, batch.FileName, batch.AccountId));
        Assert.Equal(4, statement.Entries.Count());
    }

    [Fact]
    public void Committing_counts_the_rows_the_likely_duplicates_left_out_and_the_rows_that_failed_and_queues_the_chosen_rows()
    {
        var (batch, statement) = Uploaded();

        batch.Commit(Mapping, statement, [2, 5, 6], likelyDuplicates: new HashSet<int> { 3 }, Groceries, Salary, Today, Now);

        Assert.Equal(ImportBatchStatus.Committing, batch.Status);
        Assert.Equal((5, 3, 0, 1, 1), (batch.RowCount, batch.SelectedCount, batch.ImportedCount, batch.DuplicateCount, batch.FailedCount));
        Assert.Equal(Mapping, batch.Mapping);
    }

    [Fact]
    public void A_likely_duplicate_the_user_ticks_is_imported_and_not_counted_as_skipped()
    {
        var (batch, statement) = Uploaded();

        batch.Commit(Mapping, statement, [2, 3], likelyDuplicates: new HashSet<int> { 3 }, Groceries, null, Today, Now);

        Assert.Equal((2, 0), (batch.SelectedCount, batch.DuplicateCount));
    }

    [Theory]
    [InlineData(new[] { 4 })]
    [InlineData(new[] { 1 })]
    [InlineData(new[] { 99 })]
    [InlineData(new int[0])]
    public void Only_rows_that_were_read_can_be_chosen_and_at_least_one(int[] rows)
    {
        var (batch, statement) = Uploaded();

        var violation = Assert.Throws<ImportRuleViolation>(() => batch.Commit(Mapping, statement, rows, new HashSet<int>(), Groceries, Salary, Today, Now));

        Assert.Equal(nameof(ImportBatch.SelectedRows), violation.Property);
        Assert.Equal(ImportBatchStatus.Uploaded, batch.Status);
    }

    [Fact]
    public void Money_coming_in_needs_an_income_category_and_money_going_out_an_expense_one()
    {
        var (batch, statement) = Uploaded();

        Assert.Equal("IncomeCategoryId", Assert.Throws<ImportRuleViolation>(() => batch.Commit(Mapping, statement, [5], new HashSet<int>(), Groceries, null, Today, Now)).Property);
        Assert.Equal("ExpenseCategoryId", Assert.Throws<ImportRuleViolation>(() => batch.Commit(Mapping, statement, [2], new HashSet<int>(), null, Salary, Today, Now)).Property);
    }

    [Fact]
    public void A_batch_is_committed_once()
    {
        var (batch, statement) = Uploaded();
        batch.Commit(Mapping, statement, [2], new HashSet<int>(), Groceries, null, Today, Now);

        Assert.Throws<ImportRuleViolation>(() => batch.Commit(Mapping, statement, [2], new HashSet<int>(), Groceries, null, Today, Now));
    }

    [Fact]
    public void The_chosen_rows_are_imported_a_few_at_a_time_in_file_order_and_once_all_are_the_file_is_let_go()
    {
        var (batch, statement) = Uploaded();
        batch.Commit(Mapping, statement, [6, 2, 5], new HashSet<int>(), Groceries, Salary, Today, Now);

        Assert.Equal(["A", "SALARY", "D"], batch.ChosenEntries(Currency.VND).Select(entry => entry.Description));
        batch.Imported(2, Now);
        Assert.Equal((2, ImportBatchStatus.Committing), (batch.ImportedCount, batch.Status));

        batch.Imported(1, Now);
        Assert.Equal((3, ImportBatchStatus.Committed, Now), (batch.ImportedCount, batch.Status, batch.CommittedAt));
        Assert.Null(batch.Content);
    }

    [Fact]
    public void Committing_keeps_the_users_today_and_the_chosen_rows_are_read_as_of_it_until_they_are_imported()
    {
        var batch = ImportBatch.Upload(Account, "statement.csv", "Date,Details,Amount\n27/11/2026,A,-1000\n", Now);
        batch.Commit(Mapping, batch.Read(Mapping, Currency.VND, Today), [2], new HashSet<int>(), Groceries, null, Today, Now);

        Assert.Equal(Today, batch.CommittedOn);
        Assert.Equal(["A"], batch.ChosenEntries(Currency.VND).Select(entry => entry.Description));
    }

    [Fact]
    public void A_batch_whose_rows_cannot_be_imported_stops_with_the_reason_and_keeps_what_it_imported()
    {
        var (batch, statement) = Uploaded();
        batch.Commit(Mapping, statement, [2, 6], new HashSet<int>(), Groceries, null, Today, Now);
        batch.Imported(1, Now);

        batch.Fail(ImportFailure.CategoryArchived, Now);

        Assert.Equal((ImportBatchStatus.Failed, 1, ImportFailure.CategoryArchived), (batch.Status, batch.ImportedCount, batch.Failure));
        Assert.Null(batch.Content);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("statement.csv", true)]
    public void A_file_needs_a_name(string name, bool valid) =>
        Assert.Equal(valid, ImportBatch.FileNameFrom(name) is not null);
}
