using Pfm.Domain.Imports;

namespace Pfm.Domain.UnitTests.Imports;

/// <summary>
/// A chosen row may have a category of its own (spec IMP-4), such as one a categorisation rule gave it; the batch's
/// expense and income categories are for the rows without one.
/// </summary>
public class ImportBatchRowCategoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 27);
    private static readonly Guid Transport = Guid.CreateVersion7();
    private static readonly Guid Groceries = Guid.CreateVersion7();

    private const string File = "Date,Details,Amount\n02/08/2026,GRAB,-1000\n03/08/2026,COOPMART,-2000\n05/08/2026,SALARY,5000\n";

    private static readonly StatementMapping Mapping = new(
        Separator.Comma, HasHeaderRow: true, DateColumn: 0, DescriptionColumn: 1, AmountColumn: 2, DateFormat: DateFormat.DayMonthYear);

    private static (ImportBatch Batch, Statement Statement) Uploaded()
    {
        var batch = ImportBatch.Upload(Guid.CreateVersion7(), "statement.csv", File, Now);
        return (batch, batch.Read(Mapping, Currency.VND, Today));
    }

    [Fact]
    public void A_row_with_a_category_of_its_own_keeps_it_and_the_rest_take_the_batchs()
    {
        var (batch, statement) = Uploaded();

        batch.Commit(Mapping, statement, [2, 3], new HashSet<int>(), Groceries, null, Today, Now, new Dictionary<int, Guid> { [2] = Transport });

        Assert.Equal((Transport, (Guid?)null), (batch.CategoryOf(2)!.Value, batch.CategoryOf(3)));
        Assert.Equal(Groceries, batch.ExpenseCategoryId);
    }

    [Fact]
    public void When_every_chosen_row_has_a_category_of_its_own_the_batch_needs_none()
    {
        var (batch, statement) = Uploaded();

        batch.Commit(Mapping, statement, [2, 4], new HashSet<int>(), null, null, Today, Now, new Dictionary<int, Guid> { [2] = Transport, [4] = Groceries });

        Assert.Equal(ImportBatchStatus.Committing, batch.Status);
        Assert.Equal((null, null), (batch.ExpenseCategoryId, batch.IncomeCategoryId));
    }

    [Fact]
    public void A_category_for_a_row_not_chosen_is_refused()
    {
        var (batch, statement) = Uploaded();

        var violation = Assert.Throws<ImportRuleViolation>(() =>
            batch.Commit(Mapping, statement, [2], new HashSet<int>(), Groceries, null, Today, Now, new Dictionary<int, Guid> { [3] = Transport }));

        Assert.Equal(nameof(ImportBatch.RowCategories), violation.Property);
        Assert.Equal(ImportBatchStatus.Uploaded, batch.Status);
    }
}
