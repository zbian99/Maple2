using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Model;

internal class AccountExtraInfo {
    public long AccountId { get; set; }
    public required string QqNumber { get; set; }
    public required string PhoneNumber { get; set; }
    public DateTime ExpireAt { get; set; }

    public static void Configure(EntityTypeBuilder<AccountExtraInfo> builder) {
        builder.ToTable("account-extra-info");
        builder.HasKey(info => info.AccountId);
        builder.HasOne<Account>()
            .WithOne()
            .HasForeignKey<AccountExtraInfo>(info => info.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(info => info.QqNumber)
            .IsRequired()
            .HasMaxLength(32)
            .HasColumnType("varchar(32)");
        builder.Property(info => info.PhoneNumber)
            .IsRequired()
            .HasMaxLength(32)
            .HasColumnType("varchar(32)");
        builder.Property(info => info.ExpireAt)
            .IsRequired()
            .HasColumnType("datetime(6)");
    }
}
