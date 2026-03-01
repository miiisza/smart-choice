using SmartChoice.Domain.Enums;
using SmartChoice.Domain.Exceptions;

namespace SmartChoice.Domain.Entities;

public sealed class Poll
{
    public const int MinPhotos = 2;
    public const int MaxPhotos = 4;

    private readonly List<PollPhoto> _photos = [];

    public long Id { get; private set; }
    public long AuthorUserId { get; private set; }
    public string Question { get; private set; } = string.Empty;
    public PollStatus Status { get; private set; } = PollStatus.Open;
    public DateTime? StartsAt { get; private set; }
    public DateTime? EndsAt { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; private set; }

    public User? AuthorUser { get; private set; }
    public IReadOnlyCollection<PollPhoto> Photos => _photos.AsReadOnly();
    public ICollection<Vote> Votes { get; } = new List<Vote>();
    public ICollection<Report> Reports { get; } = new List<Report>();

    private Poll()
    {
    }

    public static Poll Create(
        long authorUserId,
        string question,
        IReadOnlyCollection<string> photoUrls,
        DateTime? startsAt,
        DateTime? endsAt)
    {
        if (authorUserId <= 0)
        {
            throw new DomainValidationException("AuthorUserId must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(question))
        {
            throw new DomainValidationException("Question is required.");
        }

        ValidateDates(startsAt, endsAt);
        ValidatePhotoUrls(photoUrls);

        var poll = new Poll
        {
            AuthorUserId = authorUserId,
            Question = question.Trim(),
            StartsAt = startsAt,
            EndsAt = endsAt,
            Status = PollStatus.Open,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        poll.SetPhotos(photoUrls);

        return poll;
    }

    public void ReplacePhotos(IReadOnlyCollection<string> photoUrls)
    {
        ValidatePhotoUrls(photoUrls);
        _photos.Clear();
        SetPhotos(photoUrls);
        UpdatedAt = DateTime.UtcNow;
    }

    public void Close()
    {
        Status = PollStatus.Closed;
        UpdatedAt = DateTime.UtcNow;
    }

    private void SetPhotos(IReadOnlyCollection<string> photoUrls)
    {
        var displayOrder = 1;
        foreach (var photoUrl in photoUrls)
        {
            _photos.Add(PollPhoto.CreateForPoll(this, photoUrl, displayOrder));
            displayOrder++;
        }
    }

    private static void ValidatePhotoUrls(IReadOnlyCollection<string> photoUrls)
    {
        if (photoUrls.Count is < MinPhotos or > MaxPhotos)
        {
            throw new DomainValidationException($"Poll must contain between {MinPhotos} and {MaxPhotos} photos.");
        }

        if (photoUrls.Any(string.IsNullOrWhiteSpace))
        {
            throw new DomainValidationException("Photo URL cannot be empty.");
        }

        if (photoUrls.Any(url => url.Trim().Length > 1024))
        {
            throw new DomainValidationException("Photo URL is too long.");
        }

        var distinctCount = photoUrls
            .Select(url => url.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (distinctCount != photoUrls.Count)
        {
            throw new DomainValidationException("Poll photos must be unique.");
        }
    }

    private static void ValidateDates(DateTime? startsAt, DateTime? endsAt)
    {
        if (startsAt.HasValue && endsAt.HasValue && endsAt.Value <= startsAt.Value)
        {
            throw new DomainValidationException("EndsAt must be greater than StartsAt.");
        }
    }
}
