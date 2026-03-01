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

        builder.Property(x => x.ThumbnailUrl)
            .HasColumnName("thumbnail_url")
            .HasMaxLength(1024);

        builder.Property(x => x.StorageKey)
            .HasColumnName("storage_key")
            .HasMaxLength(512);

        builder.Property(x => x.ThumbnailStorageKey)
            .HasColumnName("thumbnail_storage_key")
            .HasMaxLength(512);

        builder.Property(x => x.ContentType)
            .HasColumnName("content_type")
            .HasMaxLength(128);

        builder.Property(x => x.FileSizeBytes)
            .HasColumnName("file_size_bytes");

        builder.Property(x => x.Width)
            .HasColumnName("width");

        builder.Property(x => x.Height)
            .HasColumnName("height");

        builder.Property(x => x.ThumbnailWidth)
            .HasColumnName("thumbnail_width");

        builder.Property(x => x.ThumbnailHeight)
            .HasColumnName("thumbnail_height");

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

        builder.HasIndex(x => x.StorageKey)
            .IsUnique()
            .HasDatabaseName("ux_poll_photos_storage_key");
    }
}
