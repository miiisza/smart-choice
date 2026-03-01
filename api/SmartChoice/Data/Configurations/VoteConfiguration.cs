using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartChoice.Domain.Entities;

namespace SmartChoice.Data.Configurations;

public sealed class VoteConfiguration : IEntityTypeConfiguration<Vote>
{
    public void Configure(EntityTypeBuilder<Vote> builder)
    {
        builder.ToTable("votes", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "ck_votes_actor",
                "((`voter_user_id` IS NOT NULL AND `guest_token_id` IS NULL) OR (`voter_user_id` IS NULL AND `guest_token_id` IS NOT NULL))");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.PollId)
            .HasColumnName("poll_id")
            .IsRequired();

        builder.Property(x => x.PollPhotoId)
            .HasColumnName("poll_photo_id")
            .IsRequired();

        builder.Property(x => x.VoterUserId)
            .HasColumnName("voter_user_id");

        builder.Property(x => x.GuestTokenId)
            .HasColumnName("guest_token_id");

        builder.Property(x => x.VotedAt)
            .HasColumnName("voted_at")
            .HasColumnType("datetime(6)")
            .IsRequired();

        builder.HasOne(x => x.Poll)
            .WithMany(x => x.Votes)
            .HasForeignKey(x => x.PollId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.PollPhoto)
            .WithMany(x => x.Votes)
            .HasForeignKey(x => x.PollPhotoId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.VoterUser)
            .WithMany(x => x.Votes)
            .HasForeignKey(x => x.VoterUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.GuestToken)
            .WithMany(x => x.Votes)
            .HasForeignKey(x => x.GuestTokenId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.PollId, x.VoterUserId })
            .IsUnique()
            .HasDatabaseName("ux_votes_poll_user");

        builder.HasIndex(x => new { x.PollId, x.GuestTokenId })
            .IsUnique()
            .HasDatabaseName("ux_votes_poll_guest_token");

        builder.HasIndex(x => new { x.PollId, x.PollPhotoId })
            .HasDatabaseName("ix_votes_poll_photo");

        builder.HasIndex(x => new { x.PollId, x.VotedAt })
            .HasDatabaseName("ix_votes_poll_voted_at");
    }
}
