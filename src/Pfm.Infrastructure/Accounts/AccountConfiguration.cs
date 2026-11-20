using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pfm.Domain;
using Pfm.Domain.Accounts;

namespace Pfm.Infrastructure.Accounts;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> account)
    {
        account.ToTable("Accounts");
        account.HasKey(row => row.Id);
        account.Property(row => row.Id).ValueGeneratedNever();
        account.Property(row => row.Name).HasMaxLength(Names.MaxLength);
        account.Property(row => row.Type).HasConversion<string>().HasMaxLength(20);
        account.Property(row => row.Currency).HasConversion<string>().HasMaxLength(3);
        account.Property(row => row.OpeningBalance).HasPrecision(19, 4);
        account.Property(row => row.Last4)
            .HasConversion(last4 => last4!.Digits, digits => Last4Digits.Parse(digits))
            .HasMaxLength(Last4Digits.Length).IsFixedLength();

        // A credit card's terms are columns of the account, null for every other type.
        account.ComplexProperty(row => row.CreditCard, terms =>
        {
            terms.Property(card => card.CreditLimit).HasColumnName(nameof(CreditCardTerms.CreditLimit)).HasPrecision(19, 4);
            terms.Property(card => card.StatementDay).HasColumnName(nameof(CreditCardTerms.StatementDay));
            terms.Property(card => card.PaymentDueDay).HasColumnName(nameof(CreditCardTerms.PaymentDueDay));
        });

        // PostgreSQL's own row version (xmin): it changes with every update, so a stale edit matches no row.
        account.Property(row => row.RowVersion).IsRowVersion();
        account.Ignore(row => row.AmountOwed);

        account.HasOne<Institution>().WithMany().HasForeignKey(row => row.InstitutionId).OnDelete(DeleteBehavior.Restrict);
        account.HasIndex(row => row.UserId);
    }
}
