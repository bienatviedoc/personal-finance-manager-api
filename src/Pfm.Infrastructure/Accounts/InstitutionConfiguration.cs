using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pfm.Domain.Accounts;
using Pfm.Infrastructure.Identity;

namespace Pfm.Infrastructure.Accounts;

internal sealed class InstitutionConfiguration : IEntityTypeConfiguration<Institution>
{
    public void Configure(EntityTypeBuilder<Institution> institution)
    {
        institution.ToTable("Institutions");
        institution.HasKey(row => row.Id);
        institution.Property(row => row.Id).ValueGeneratedNever();
        institution.Property(row => row.Name).HasMaxLength(Institution.MaxNameLength);
        institution.Property(row => row.Color).HasMaxLength(7);
        institution.Property(row => row.LogoKey).HasMaxLength(32);
        institution.HasIndex(row => row.UserId);

        // A user's institutions go when the user does.
        institution.HasOne<User>().WithMany().HasForeignKey(row => row.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
