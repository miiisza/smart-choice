using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartChoice.Domain.Entities;

namespace SmartChoice.Data.Configurations;

public sealed class InviteConfiguration : IEntityTypeConfiguration<Invite>
{
    public void Configure(EntityTypeBuilder<Invite> builder)
    {
        builder.ToTable("invites", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_invites_max_uses", "`max_uses` > 0");
            tableBuilder.HasCheckConstraint("ck_invites_used_count", "`used_count` >= 0 AND `used_count` <= `max_uses`");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.Code)
            .HasColumnName("code")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("datetime(6)")
            .IsRequired();

        builder.Property(x => x.MaxUses)
            .HasColumnName("max_uses")
            .IsRequired();

        builder.Property(x => x.UsedCount)
            .HasColumnName("used_count")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.CreatedByUserId)
            .HasColumnName("created_by_user_id");

        builder.Property(x => x.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("datetime(6)")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("datetime(6)");

        builder.HasOne(x => x.CreatedByUser)
            .WithMany(x => x.InvitesCreated)
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.Code)
            .IsUnique()
            .HasDatabaseName("ux_invites_code");

        builder.HasIndex(x => new { x.IsActive, x.ExpiresAt })
            .HasDatabaseName("ix_invites_active_expires_at");
    }
}
