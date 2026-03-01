using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartChoice.Domain.Entities;
using SmartChoice.Domain.Enums;

namespace SmartChoice.Data.Configurations;

public sealed class ReportConfiguration : IEntityTypeConfiguration<Report>
{
    public void Configure(EntityTypeBuilder<Report> builder)
    {
        builder.ToTable("reports", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "ck_reports_actor",
                "((`reporter_user_id` IS NOT NULL AND `reporter_guest_token_id` IS NULL) OR (`reporter_user_id` IS NULL AND `reporter_guest_token_id` IS NOT NULL))");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.PollId)
            .HasColumnName("poll_id")
            .IsRequired();

        builder.Property(x => x.ReporterUserId)
            .HasColumnName("reporter_user_id");

        builder.Property(x => x.ReporterGuestTokenId)
            .HasColumnName("reporter_guest_token_id");

        builder.Property(x => x.ReasonCode)
            .HasColumnName("reason_code")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.Details)
            .HasColumnName("details")
            .HasMaxLength(1000);

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<byte>()
            .HasDefaultValue(ReportStatus.Open)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("datetime(6)")
            .IsRequired();

        builder.Property(x => x.ReviewedAt)
            .HasColumnName("reviewed_at")
            .HasColumnType("datetime(6)");

        builder.HasOne(x => x.Poll)
            .WithMany(x => x.Reports)
            .HasForeignKey(x => x.PollId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.ReporterUser)
            .WithMany(x => x.ReportsFiled)
            .HasForeignKey(x => x.ReporterUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ReporterGuestToken)
            .WithMany(x => x.ReportsFiled)
            .HasForeignKey(x => x.ReporterGuestTokenId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.Status, x.CreatedAt })
            .HasDatabaseName("ix_reports_status_created_at");

        builder.HasIndex(x => new { x.PollId, x.CreatedAt })
            .HasDatabaseName("ix_reports_poll_created_at");

        builder.HasIndex(x => x.ReporterUserId)
            .HasDatabaseName("ix_reports_reporter_user_id");

        builder.HasIndex(x => x.ReporterGuestTokenId)
            .HasDatabaseName("ix_reports_reporter_guest_token_id");
    }
}
