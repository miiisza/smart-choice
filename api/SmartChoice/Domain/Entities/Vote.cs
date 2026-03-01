using SmartChoice.Domain.Exceptions;

namespace SmartChoice.Domain.Entities;

public sealed class Vote
{
    public long Id { get; private set; }
    public long PollId { get; private set; }
    public long PollPhotoId { get; private set; }
    public long? VoterUserId { get; private set; }
    public long? GuestTokenId { get; private set; }
    public DateTime VotedAt { get; private set; } = DateTime.UtcNow;

    public Poll? Poll { get; private set; }
    public PollPhoto? PollPhoto { get; private set; }
    public User? VoterUser { get; private set; }
    public GuestToken? GuestToken { get; private set; }

    private Vote()
    {
    }

    private Vote(long pollId, long pollPhotoId, long? voterUserId, long? guestTokenId)
    {
        if (pollId <= 0)
        {
            throw new DomainValidationException("PollId must be greater than zero.");
        }

        if (pollPhotoId <= 0)
        {
            throw new DomainValidationException("PollPhotoId must be greater than zero.");
        }

        var hasUser = voterUserId.HasValue;
        var hasGuest = guestTokenId.HasValue;
        if (hasUser == hasGuest)
        {
            throw new DomainValidationException("Vote must be cast by either a user or a guest token.");
        }

        PollId = pollId;
        PollPhotoId = pollPhotoId;
        VoterUserId = voterUserId;
        GuestTokenId = guestTokenId;
        VotedAt = DateTime.UtcNow;
    }

    public static Vote CreateByUser(long pollId, long pollPhotoId, long voterUserId)
    {
        if (voterUserId <= 0)
        {
            throw new DomainValidationException("VoterUserId must be greater than zero.");
        }

        return new Vote(pollId, pollPhotoId, voterUserId, null);
    }

    public static Vote CreateByGuest(long pollId, long pollPhotoId, long guestTokenId)
    {
        if (guestTokenId <= 0)
        {
            throw new DomainValidationException("GuestTokenId must be greater than zero.");
        }

        return new Vote(pollId, pollPhotoId, null, guestTokenId);
    }
}
