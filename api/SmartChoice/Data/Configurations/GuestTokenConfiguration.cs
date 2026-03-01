using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartChoice.Domain.Entities;

namespace SmartChoice.Data.Configurations;

public sealed class GuestTokenConfiguration : IEntityTypeConfiguration<GuestToken>
{
    public void Configure(EntityTypeBuilder<GuestToken> builder)
    {
        builder.ToTable("guest_tokens");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.TokenHash)
            .HasColumnName("token_hash")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.InviteId)
            .HasColumnName("invite_id");

        builder.Property(x => x.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("datetime(6)");

        builder.Property(x => x.IsRevoked)
            .HasColumnName("is_revoked")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("datetime(6)")
            .IsRequired();

        builder.Property(x => x.LastUsedAt)
            .HasColumnName("last_used_at")
            .HasColumnType("datetime(6)");

        builder.HasOne(x => x.Invite)
            .WithMany(x => x.GuestTokens)
            .HasForeignKey(x => x.InviteId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.TokenHash)
            .IsUnique()
            .HasDatabaseName("ux_guest_tokens_token_hash");

        builder.HasIndex(x => new { x.IsRevoked, x.ExpiresAt })
            .HasDatabaseName("ix_guest_tokens_revoked_expires_at");

        builder.HasIndex(x => new { x.InviteId, x.CreatedAt })
            .HasDatabaseName("ix_guest_tokens_invite_created_at");
    }
}
