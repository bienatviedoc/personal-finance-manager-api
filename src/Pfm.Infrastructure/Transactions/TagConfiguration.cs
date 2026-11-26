using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pfm.Domain.Transactions;

namespace Pfm.Infrastructure.Transactions;

internal sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    /// <summary>
    /// A user has one tag of each name. Two requests creating the same tag at once break it, and the one that
    /// saves second is made again, finding the tag (<c>PfmDbContext</c> reports it as a concurrent change).
    /// </summary>
    public const string OneTagOfEachName = "IX_Tags_UserId_Name";

    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("Tags");
        builder.Property(tag => tag.Id).ValueGeneratedNever();
        builder.Property(tag => tag.Name).HasMaxLength(Tag.MaxNameLength);
        builder.HasIndex(tag => new { tag.UserId, tag.Name }).IsUnique().HasDatabaseName(OneTagOfEachName);
    }
}
