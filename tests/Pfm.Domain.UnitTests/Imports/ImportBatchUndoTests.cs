using Pfm.Domain.Imports;

namespace Pfm.Domain.UnitTests.Imports;

/// <summary>
/// Undoing an import batch (spec IMP-5) removes every transaction it created, which Transactions does; the batch itself
/// says it is undone, and stops being imported if the worker had not finished.
/// </summary>
public class ImportBatchUndoTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddHours(2);
    private static readonly DateOnly Today = new(2026, 11, 27);

    private static readonly StatementMapping Mapping = new(
        Separator.Comma, HasHeaderRow: true, DateColumn: 0, DescriptionColumn: 1, AmountColumn: 2, DateFormat: DateFormat.DayMonthYear);

    private static ImportBatch Committing()
    {
        var batch = ImportBatch.Upload(Guid.CreateVersion7(), "statement.csv", "Date,Details,Amount\n02/08/2026,A,-1000\n03/08/2026,B,-2000\n", Now);
        batch.Commit(Mapping, batch.Read(Mapping, Currency.VND, Today), [2, 3], new HashSet<int>(), Guid.CreateVersion7(), null, Today, Now);
        return batch;
    }

    [Fact]
    public void An_imported_batch_is_undone_and_keeps_its_counts()
    {
        var batch = Committing();
        batch.Imported(2, Now);

        batch.Undo(Later);

        Assert.Equal((ImportBatchStatus.Undone, Later, 2), (batch.Status, batch.UndoneAt, batch.ImportedCount));
    }

    [Fact]
    public void A_batch_still_being_imported_is_undone_which_stops_it_and_lets_its_file_go()
    {
        var batch = Committing();
        batch.Imported(1, Now);

        batch.Undo(Later);

        Assert.Equal((ImportBatchStatus.Undone, 1), (batch.Status, batch.ImportedCount));
        Assert.Null(batch.Content);
        Assert.Throws<InvalidOperationException>(() => batch.Imported(1, Later));
    }

    [Fact]
    public void A_failed_batch_is_undone_too()
    {
        var batch = Committing();
        batch.Fail(ImportFailure.CategoryArchived, Now);

        batch.Undo(Later);

        Assert.Equal(ImportBatchStatus.Undone, batch.Status);
    }

    [Fact]
    public void A_batch_never_committed_has_nothing_to_undo()
    {
        var batch = ImportBatch.Upload(Guid.CreateVersion7(), "statement.csv", "Date,Details,Amount\n02/08/2026,A,-1000\n", Now);

        Assert.Throws<InvalidOperationException>(() => batch.Undo(Later));
        Assert.Equal(ImportBatchStatus.Uploaded, batch.Status);
    }

    [Fact]
    public void Undoing_a_batch_again_changes_nothing()
    {
        var batch = Committing();
        batch.Undo(Now);

        batch.Undo(Later);

        Assert.Equal(Now, batch.UndoneAt);
    }
}
