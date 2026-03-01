using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartChoice.Domain.Entities;
using SmartChoice.Domain.Enums;

namespace SmartChoice.Data.Configurations;

public sealed class PollConfiguration : IEntityTypeConfiguration<Poll>
{
    public void Configure(EntityTypeBuilder<Poll> builder)
    {
        builder.ToTable("polls", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_polls_dates", "`ends_at` IS NULL OR `starts_at` IS NULL OR `ends_at` > `starts_at`");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.AuthorUserId)
            .HasColumnName("author_user_id")
            .IsRequired();

        builder.Property(x => x.Question)
            .HasColumnName("question")
            .HasMaxLength(280)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<byte>()
            .HasDefaultValue(PollStatus.Open)
            .IsRequired();

        builder.Property(x => x.StartsAt)
            .HasColumnName("starts_at")
            .HasColumnType("datetime(6)");

        builder.Property(x => x.EndsAt)
            .HasColumnName("ends_at")
            .HasColumnType("datetime(6)");

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("datetime(6)")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("datetime(6)");

        builder.HasOne(x => x.AuthorUser)
            .WithMany(x => x.PollsAuthored)
            .HasForeignKey(x => x.AuthorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Metadata.FindNavigation(nameof(Poll.Photos))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.Photos)
            .WithOne(x => x.Poll)
            .HasForeignKey(x => x.PollId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Votes)
            .WithOne(x => x.Poll)
            .HasForeignKey(x => x.PollId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Reports)
            .WithOne(x => x.Poll)
            .HasForeignKey(x => x.PollId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.Status, x.CreatedAt })
            .HasDatabaseName("ix_polls_status_created_at");

        builder.HasIndex(x => new { x.AuthorUserId, x.CreatedAt })
            .HasDatabaseName("ix_polls_author_created_at");

        builder.HasIndex(x => new { x.EndsAt, x.Status })
            .HasDatabaseName("ix_polls_ends_at_status");
    }
}
