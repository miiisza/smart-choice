using SmartChoice.Domain.Exceptions;

namespace SmartChoice.Domain.Entities;

public sealed class Invite
{
    public long Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public DateTime ExpiresAt { get; private set; }
    public int MaxUses { get; private set; }
    public int UsedCount { get; private set; }
    public long? CreatedByUserId { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; private set; }

    public User? CreatedByUser { get; private set; }
    public ICollection<GuestToken> GuestTokens { get; } = new List<GuestToken>();

    private Invite()
    {
    }

    public Invite(string code, DateTime expiresAt, int maxUses, long? createdByUserId = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new DomainValidationException("Invite code is required.");
        }

        if (expiresAt <= DateTime.UtcNow)
        {
            throw new DomainValidationException("Invite expiration must be in the future.");
        }

        if (maxUses <= 0)
        {
            throw new DomainValidationException("MaxUses must be greater than zero.");
        }

        Code = code.Trim().ToUpperInvariant();
        ExpiresAt = expiresAt;
        MaxUses = maxUses;
        UsedCount = 0;
        CreatedByUserId = createdByUserId;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public bool IsExpired(DateTime utcNow)
    {
        return ExpiresAt <= utcNow;
    }

    public bool CanUse(DateTime utcNow)
    {
        return IsActive && !IsExpired(utcNow) && UsedCount < MaxUses;
    }

    public void RegisterUse(DateTime utcNow)
    {
        if (!CanUse(utcNow))
        {
            throw new DomainValidationException("Invite cannot be used.");
        }

        UsedCount++;
        UpdatedAt = utcNow;
    }

    public void Deactivate(DateTime utcNow)
    {
        IsActive = false;
        UpdatedAt = utcNow;
    }
}
