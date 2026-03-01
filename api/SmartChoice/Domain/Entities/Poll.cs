using SmartChoice.Domain.Enums;
using SmartChoice.Domain.Exceptions;

namespace SmartChoice.Domain.Entities;

public sealed class Poll
{
    public const int MinPhotos = 2;
    public const int MaxPhotos = 4;
    public const int MinRadiusMeters = 1;
    public const int MaxRadiusMeters = 200_000;

    private readonly List<PollPhoto> _photos = [];

    public long Id { get; private set; }
    public long AuthorUserId { get; private set; }
    public string Question { get; private set; } = string.Empty;
    public PollStatus Status { get; private set; } = PollStatus.Draft;
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public int RadiusMeters { get; private set; }
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

    public static Poll CreateDraft(
        long authorUserId,
        string question,
        IReadOnlyCollection<string> photoUrls,
        double latitude,
        double longitude,
        int radiusMeters,
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

        ValidateLocation(latitude, longitude, radiusMeters);
        ValidateDates(startsAt, endsAt);
        ValidatePhotoUrls(photoUrls, minPhotos: 0);

        var poll = new Poll
        {
            AuthorUserId = authorUserId,
            Question = question.Trim(),
            Latitude = latitude,
            Longitude = longitude,
            RadiusMeters = radiusMeters,
            StartsAt = startsAt,
            EndsAt = endsAt,
            Status = PollStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        poll.SetPhotos(photoUrls);

        return poll;
    }

    public void ReplacePhotos(IReadOnlyCollection<string> photoUrls)
    {
        if (Status != PollStatus.Draft)
        {
            throw new DomainValidationException("Photos can only be updated for draft polls.");
        }

        ValidatePhotoUrls(photoUrls, minPhotos: 0);
        _photos.Clear();
        SetPhotos(photoUrls);
        UpdatedAt = DateTime.UtcNow;
    }

    public PollPhoto AddUploadedPhoto(
        string photoUrl,
        string thumbnailUrl,
        string storageKey,
        string thumbnailStorageKey,
        string contentType,
        long fileSizeBytes,
        int width,
        int height,
        int thumbnailWidth,
        int thumbnailHeight)
    {
        if (Status != PollStatus.Draft)
        {
            throw new DomainValidationException("Photos can only be updated for draft polls.");
        }

        if (_photos.Count >= MaxPhotos)
        {
            throw new DomainValidationException($"Poll cannot contain more than {MaxPhotos} photos.");
        }

        var displayOrder = _photos.Count + 1;
        var photo = PollPhoto.CreateUploadedForPoll(
            this,
            photoUrl,
            thumbnailUrl,
            storageKey,
            thumbnailStorageKey,
            contentType,
            fileSizeBytes,
            width,
            height,
            thumbnailWidth,
            thumbnailHeight,
            displayOrder);

        _photos.Add(photo);
        UpdatedAt = DateTime.UtcNow;

        return photo;
    }

    public void RemovePhoto(long pollPhotoId)
    {
        if (Status != PollStatus.Draft)
        {
            throw new DomainValidationException("Photos can only be updated for draft polls.");
        }

        var photoToRemove = _photos.SingleOrDefault(photo => photo.Id == pollPhotoId);
        if (photoToRemove is null)
        {
            throw new DomainValidationException("Photo does not exist in this poll.");
        }

        _photos.Remove(photoToRemove);

        var displayOrder = 1;
        foreach (var photo in _photos.OrderBy(x => x.DisplayOrder))
        {
            photo.SetDisplayOrder(displayOrder);
            displayOrder++;
        }

        UpdatedAt = DateTime.UtcNow;
    }

    public void Publish(DateTime nowUtc)
    {
        if (Status != PollStatus.Draft)
        {
            throw new DomainValidationException("Only draft polls can be published.");
        }

        if (_photos.Count < MinPhotos)
        {
            throw new DomainValidationException($"Poll must contain at least {MinPhotos} photos before publishing.");
        }

        Status = PollStatus.Open;
        UpdatedAt = nowUtc;
    }

    public void Close(DateTime nowUtc)
    {
        if (Status != PollStatus.Open)
        {
            throw new DomainValidationException("Only open polls can be closed.");
        }

        Status = PollStatus.Closed;
        UpdatedAt = nowUtc;
    }

    public void EnsureCanAcceptVote(DateTime nowUtc)
    {
        if (Status != PollStatus.Open)
        {
            throw new DomainValidationException("Poll is not open for voting.");
        }

        if (StartsAt.HasValue && nowUtc < StartsAt.Value)
        {
            throw new DomainValidationException("Poll voting has not started yet.");
        }

        if (EndsAt.HasValue && nowUtc >= EndsAt.Value)
        {
            throw new DomainValidationException("Poll voting has already ended.");
        }
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

    private static void ValidatePhotoUrls(IReadOnlyCollection<string> photoUrls, int minPhotos)
    {
        if (photoUrls is null)
        {
            throw new DomainValidationException("PhotoUrls are required.");
        }

        if (photoUrls.Count < minPhotos || photoUrls.Count > MaxPhotos)
        {
            throw new DomainValidationException($"Poll must contain between {minPhotos} and {MaxPhotos} photos.");
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

    private static void ValidateLocation(double latitude, double longitude, int radiusMeters)
    {
        if (double.IsNaN(latitude) || double.IsInfinity(latitude) || latitude is < -90 or > 90)
        {
            throw new DomainValidationException("Latitude must be in range [-90, 90].");
        }

        if (double.IsNaN(longitude) || double.IsInfinity(longitude) || longitude is < -180 or > 180)
        {
            throw new DomainValidationException("Longitude must be in range [-180, 180].");
        }

        if (radiusMeters is < MinRadiusMeters or > MaxRadiusMeters)
        {
            throw new DomainValidationException(
                $"RadiusMeters must be in range [{MinRadiusMeters}, {MaxRadiusMeters}].");
        }
    }
}
