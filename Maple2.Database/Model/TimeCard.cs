using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Model;

internal class TimeCard {
    public long Id { get; set; }
    public required string CardCode { get; set; }
    public bool IsUsed { get; set; }
    public DateTime? UsedAt { get; set; }
    public long? UsedByAccountId { get; set; }
    public string? UsedByUsername { get; set; }

    public static void Configure(EntityTypeBuilder<TimeCard> builder) {
        builder.ToTable("time-card");
        builder.HasKey(card => card.Id);
        builder.HasIndex(card => card.CardCode).IsUnique();

        builder.Property(card => card.CardCode)
            .IsRequired()
            .HasMaxLength(64)
            .HasColumnType("varchar(64)");
        builder.Property(card => card.IsUsed)
            .IsRequired()
            .HasColumnType("tinyint(1)");
        builder.Property(card => card.UsedAt)
            .HasColumnType("datetime(6)");
        builder.Property(card => card.UsedByAccountId)
            .HasColumnType("bigint");
        builder.Property(card => card.UsedByUsername)
            .HasMaxLength(255)
            .HasColumnType("varchar(255)");
    }
}
