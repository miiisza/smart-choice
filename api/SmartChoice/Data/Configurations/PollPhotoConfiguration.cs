using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartChoice.Domain.Entities;

namespace SmartChoice.Data.Configurations;

public sealed class PollPhotoConfiguration : IEntityTypeConfiguration<PollPhoto>
{
    public void Configure(EntityTypeBuilder<PollPhoto> builder)
    {
        builder.ToTable("poll_photos", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_poll_photos_display_order", "`display_order` >= 1 AND `display_order` <= 4");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.PollId)
            .HasColumnName("poll_id")
            .IsRequired();

        builder.Property(x => x.PhotoUrl)
            .HasColumnName("photo_url")
            .HasMaxLength(1024)
            .IsRequired();

        builder.Property(x => x.DisplayOrder)
            .HasColumnName("display_order")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("datetime(6)")
            .IsRequired();

        builder.HasOne(x => x.Poll)
            .WithMany(x => x.Photos)
            .HasForeignKey(x => x.PollId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Votes)
            .WithOne(x => x.PollPhoto)
            .HasForeignKey(x => x.PollPhotoId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.PollId, x.DisplayOrder })
            .IsUnique()
            .HasDatabaseName("ux_poll_photos_poll_order");

        builder.HasIndex(x => x.PollId)
            .HasDatabaseName("ix_poll_photos_poll_id");
    }
}
