using Pfm.Domain.Transactions;

namespace Pfm.Domain.UnitTests.Transactions;

public class AttachmentTests
{
    private static readonly DateTimeOffset Monday = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Lunch = Guid.CreateVersion7();
    private static readonly Guid Alice = Guid.Parse("0193a6c0-0000-7000-8000-0000000a11ce");

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Pdf = "%PDF-1.7"u8.ToArray();

    [Fact]
    public void An_attachment_holds_what_the_app_knows_about_a_file_attached_to_a_transaction()
    {
        var attachment = Attachment.To(Lunch, Alice, "lunch.jpg", Jpeg, 48_213, Monday);

        Assert.Equal((Lunch, "lunch.jpg", "image/jpeg", 48_213L), (attachment.TransactionId, attachment.FileName, attachment.ContentType, attachment.Size));
        Assert.Equal((Monday, Monday), (attachment.CreatedAt, attachment.UpdatedAt));
        Assert.Equal(7, attachment.Id.Version);
    }

    [Fact]
    public void Its_file_is_stored_under_its_users_prefix_and_its_own_id_and_nothing_the_user_wrote()
    {
        var attachment = Attachment.To(Lunch, Alice, "../../etc/passwd lunch.jpg", Jpeg, 10, Monday);

        Assert.Equal($"users/0193a6c00000700080000000000a11ce/attachments/{attachment.Id:N}", attachment.StorageKey);
    }

    [Fact]
    public void A_file_is_what_its_first_bytes_say_it_is()
    {
        Assert.Equal("image/jpeg", AttachmentFormat.ContentTypeOf(Jpeg));
        Assert.Equal("image/png", AttachmentFormat.ContentTypeOf(Png));
        Assert.Equal("application/pdf", AttachmentFormat.ContentTypeOf(Pdf));
    }

    [Theory]
    [InlineData("GIF89a..")]
    [InlineData("PK\u0003\u0004....")]
    [InlineData("<svg xmlns")]
    [InlineData("%PDF")]
    [InlineData(" %PDF-1.7")]
    [InlineData("")]
    public void Anything_but_a_JPEG_a_PNG_or_a_PDF_cannot_be_attached(string start)
    {
        var bytes = System.Text.Encoding.Latin1.GetBytes(start);

        Assert.Null(AttachmentFormat.ContentTypeOf(bytes));
        var violation = Assert.Throws<AttachmentRuleViolation>(() => Attachment.To(Lunch, Alice, "receipt.pdf", bytes, 10, Monday));
        Assert.Equal("Attach a JPEG, PNG or PDF file.", violation.Message);
    }

    [Fact]
    public void A_truncated_signature_is_not_a_file_of_that_type()
    {
        Assert.Null(AttachmentFormat.ContentTypeOf(Png.AsSpan(0, 7)));
        Assert.Null(AttachmentFormat.ContentTypeOf(Jpeg.AsSpan(0, 2)));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(5 * 1024 * 1024, true)]
    [InlineData(5 * 1024 * 1024 + 1, false)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    public void A_file_holds_something_and_at_most_5_MB(long size, bool canBeAttached)
    {
        Assert.Equal(canBeAttached, Attachment.IsValidSize(size));
        if (!canBeAttached)
        {
            var violation = Assert.Throws<AttachmentRuleViolation>(() => Attachment.To(Lunch, Alice, "receipt.pdf", Pdf, size, Monday));
            Assert.Equal("Attach a file of at most 5 MB.", violation.Message);
        }
    }

    [Theory]
    [InlineData("lunch.jpg", "lunch.jpg")]
    [InlineData("  lunch.jpg ", "lunch.jpg")]
    [InlineData(@"C:\Users\me\Pictures\lunch.jpg", "lunch.jpg")]
    [InlineData("/home/me/Hoá đơn tháng 11.pdf", "Hoá đơn tháng 11.pdf")]
    [InlineData("../../lunch.jpg", "lunch.jpg")]
    public void A_file_is_kept_under_its_own_name_without_any_folder(string written, string kept)
    {
        Assert.Equal(kept, Attachment.FileNameFrom(written));
        Assert.Equal(kept, Attachment.To(Lunch, Alice, written, Pdf, 10, Monday).FileName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("folder/")]
    [InlineData("lunch\r\n.jpg")]
    public void A_file_needs_a_name_to_be_kept_under(string? written)
    {
        Assert.Null(Attachment.FileNameFrom(written));
        Assert.Throws<ArgumentException>(() => Attachment.To(Lunch, Alice, written!, Pdf, 10, Monday));
    }

    [Fact]
    public void A_file_name_holds_at_most_255_characters()
    {
        Assert.NotNull(Attachment.FileNameFrom(new string('n', 251) + ".pdf"));
        Assert.Null(Attachment.FileNameFrom(new string('n', 252) + ".pdf"));
    }
}
