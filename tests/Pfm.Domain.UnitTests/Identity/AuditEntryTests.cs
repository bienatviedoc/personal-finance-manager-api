using Pfm.Domain.Identity;

namespace Pfm.Domain.UnitTests.Identity;

/// <summary>What an audit entry keeps, and the summaries written in each locale (spec section 8, Auditability).</summary>
public class AuditEntryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_entry_keeps_what_was_done_to_what_and_when_with_an_id_made_from_that_moment()
    {
        var batch = Guid.CreateVersion7();

        var entry = AuditEntry.Record(AuditAction.ImportUndo, AuditEntityType.ImportBatch, batch, "Undid it.", Now);

        Assert.Equal((AuditAction.ImportUndo, AuditEntityType.ImportBatch, (Guid?)batch, Now, "Undid it."), (entry.Action, entry.EntityType, entry.EntityId, entry.At, entry.Summary));
        Assert.True(entry.Id.CompareTo(AuditEntry.Record(AuditAction.SignIn, AuditEntityType.User, null, "Later.", Now.AddMilliseconds(1)).Id) < 0);
    }

    [Fact]
    public void A_summary_too_long_to_keep_is_cut_short_and_an_empty_one_is_refused()
    {
        var entry = AuditEntry.Record(AuditAction.ImportCommit, AuditEntityType.ImportBatch, null, new string('a', 600), Now);

        Assert.Equal(AuditEntry.MaxSummaryLength, entry.Summary.Length);
        Assert.EndsWith("…", entry.Summary);
        Assert.Throws<ArgumentException>(() => AuditEntry.Record(AuditAction.SignIn, AuditEntityType.User, null, " ", Now));
    }

    [Fact]
    public void Undoing_an_import_says_which_statement_and_how_many_transactions_it_removed()
    {
        Assert.Equal("Undid the import of the statement august.csv: 12 transactions removed.", AuditSummaries.ImportUndo(Locale.En, "august.csv", 12));
        Assert.Equal("Undid the import of the statement august.csv: 1 transaction removed.", AuditSummaries.ImportUndo(Locale.En, "august.csv", 1));
        Assert.Equal("Đã hoàn tác lần nhập sao kê august.csv: đã xóa 12 giao dịch.", AuditSummaries.ImportUndo(Locale.Vi, "august.csv", 12));
    }

    [Fact]
    public void Summaries_in_Vietnamese_write_amounts_the_Vietnamese_way()
    {
        Assert.Equal("Đã xóa một giao dịch 45.000\u00A0₫ ngày 2026-11-02.", AuditSummaries.TransactionDeleted(Locale.Vi, 45000m, Currency.VND, new DateOnly(2026, 11, 2)));
        Assert.Equal("Đã xóa một chuyển khoản 200.000\u00A0₫ ngày 2026-11-04.", AuditSummaries.TransferDeleted(Locale.Vi, 200000m, Currency.VND, new DateOnly(2026, 11, 4)));
        Assert.Equal("Đã xác nhận nhập sao kê august.csv: 2 dòng sẽ được nhập.", AuditSummaries.ImportCommit(Locale.Vi, "august.csv", 2));
        Assert.Equal("Đã gắn nhãn tet-2027 cho 2 giao dịch.", AuditSummaries.TagAdded(Locale.Vi, 2, "tet-2027"));
    }
}
