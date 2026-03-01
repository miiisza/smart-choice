using SmartChoice.Domain.Exceptions;

namespace SmartChoice.Domain.Entities;

public sealed class User
{
    public long Id { get; private set; }
    public string Username { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; private set; }

    public ICollection<Poll> PollsAuthored { get; } = new List<Poll>();
    public ICollection<Vote> Votes { get; } = new List<Vote>();
    public ICollection<Invite> InvitesCreated { get; } = new List<Invite>();
    public ICollection<Report> ReportsFiled { get; } = new List<Report>();

    private User()
    {
    }

    public User(string username, string email, string passwordHash)
    {
        SetCredentials(username, email, passwordHash);
        CreatedAt = DateTime.UtcNow;
        IsActive = true;
    }

    public void SetCredentials(string username, string email, string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new DomainValidationException("Username is required.");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new DomainValidationException("Email is required.");
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new DomainValidationException("PasswordHash is required.");
        }

        Username = username.Trim();
        Email = email.Trim().ToLowerInvariant();
        PasswordHash = passwordHash.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
    }
}
