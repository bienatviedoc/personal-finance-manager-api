using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pfm.Domain;

namespace Pfm.Infrastructure.Identity;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users", table => table.HasCheckConstraint(
            "CK_Users_BudgetWarningPercent_1_To_100", "\"BudgetWarningPercent\" BETWEEN 1 AND 100"));
        builder.Property(user => user.BudgetWarningPercent).HasDefaultValue(Pfm.Domain.Identity.ProfileSettings.DefaultBudgetWarningPercent);
        builder.Property(user => user.EmailBudgetNotifications).HasDefaultValue(true);
        builder.Property(user => user.BaseCurrency).HasConversion<string>().HasMaxLength(3);
        builder.Property(user => user.TimeZone).HasMaxLength(64);
        builder.Property(user => user.Locale)
            .HasConversion(new ValueConverter<Locale, string>(locale => locale.Tag(), tag => ParseLocale(tag)))
            .HasMaxLength(8);

        // The deactivated users, which the erasure job asks for by how long ago they were deactivated.
        builder.HasIndex(user => user.DeactivatedAt).HasFilter("\"DeactivatedAt\" IS NOT NULL");
    }

    private static Locale ParseLocale(string tag) =>
        Locales.TryParse(tag, out var locale) ? locale : throw new InvalidOperationException($"Unknown locale tag '{tag}' in the database.");
}

internal sealed class UserClaimConfiguration : IEntityTypeConfiguration<IdentityUserClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserClaim<Guid>> builder) => builder.ToTable("UserClaims");
}

internal sealed class UserLoginConfiguration : IEntityTypeConfiguration<IdentityUserLogin<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserLogin<Guid>> builder) => builder.ToTable("UserLogins");
}

internal sealed class UserTokenConfiguration : IEntityTypeConfiguration<IdentityUserToken<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserToken<Guid>> builder) => builder.ToTable("UserTokens");
}
