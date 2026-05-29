using SmartChoice.Domain.Exceptions;

namespace SmartChoice.Domain.Entities;

public sealed class GuestToken
{
    public long Id { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public long? InviteId { get; private set; }
    public long PollId { get; private set; }
    public DateTime? ExpiresAt { get; private set; }
    public bool IsRevoked { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; private set; }

    public Invite? Invite { get; private set; }
    public Poll? Poll { get; private set; }
    public ICollection<Vote> Votes { get; } = new List<Vote>();
    public ICollection<Report> ReportsFiled { get; } = new List<Report>();

    private GuestToken()
    {
    }

    public GuestToken(string tokenHash, long? inviteId, long pollId, DateTime? expiresAt)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new DomainValidationException("TokenHash is required.");
        }

        if (pollId <= 0)
        {
            throw new DomainValidationException("PollId must be greater than zero.");
        }

        if (expiresAt.HasValue && expiresAt.Value <= DateTime.UtcNow)
        {
            throw new DomainValidationException("Guest token expiration must be in the future.");
        }

        TokenHash = tokenHash.Trim();
        InviteId = inviteId;
        PollId = pollId;
        ExpiresAt = expiresAt;
        IsRevoked = false;
        CreatedAt = DateTime.UtcNow;
    }

    public bool IsValid(DateTime utcNow)
    {
        return !IsRevoked && (!ExpiresAt.HasValue || ExpiresAt.Value > utcNow);
    }

    public void MarkUsed(DateTime utcNow)
    {
        LastUsedAt = utcNow;
    }

    public void Revoke(DateTime utcNow)
    {
        IsRevoked = true;
        LastUsedAt = utcNow;
    }
}
