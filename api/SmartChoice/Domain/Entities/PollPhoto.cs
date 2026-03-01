using SmartChoice.Domain.Exceptions;

namespace SmartChoice.Domain.Entities;

public sealed class PollPhoto
{
    public long Id { get; private set; }
    public long PollId { get; private set; }
    public string PhotoUrl { get; private set; } = string.Empty;
    public string? ThumbnailUrl { get; private set; }
    public string? StorageKey { get; private set; }
    public string? ThumbnailStorageKey { get; private set; }
    public string? ContentType { get; private set; }
    public long? FileSizeBytes { get; private set; }
    public int? Width { get; private set; }
    public int? Height { get; private set; }
    public int? ThumbnailWidth { get; private set; }
    public int? ThumbnailHeight { get; private set; }
    public byte DisplayOrder { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    public Poll? Poll { get; private set; }
    public ICollection<Vote> Votes { get; } = new List<Vote>();

    private PollPhoto()
    {
    }

    private PollPhoto(
        Poll poll,
        string photoUrl,
        string? thumbnailUrl,
        string? storageKey,
        string? thumbnailStorageKey,
        string? contentType,
        long? fileSizeBytes,
        int? width,
        int? height,
        int? thumbnailWidth,
        int? thumbnailHeight,
        int displayOrder)
    {
        if (displayOrder is < 1 or > Poll.MaxPhotos)
        {
            throw new DomainValidationException($"DisplayOrder must be between 1 and {Poll.MaxPhotos}.");
        }

        if (string.IsNullOrWhiteSpace(photoUrl))
        {
            throw new DomainValidationException("PhotoUrl is required.");
        }

        if (fileSizeBytes is < 0)
        {
            throw new DomainValidationException("FileSizeBytes cannot be negative.");
        }

        if (width.HasValue != height.HasValue)
        {
            throw new DomainValidationException("Image width and height must be provided together.");
        }

        if ((width.HasValue && width.Value <= 0) || (height.HasValue && height.Value <= 0))
        {
            throw new DomainValidationException("Image dimensions must be greater than zero.");
        }

        if (thumbnailWidth.HasValue != thumbnailHeight.HasValue)
        {
            throw new DomainValidationException("Thumbnail width and height must be provided together.");
        }

        if ((thumbnailWidth.HasValue && thumbnailWidth.Value <= 0)
            || (thumbnailHeight.HasValue && thumbnailHeight.Value <= 0))
        {
            throw new DomainValidationException("Thumbnail dimensions must be greater than zero.");
        }

        Poll = poll;
        PollId = poll.Id;
        PhotoUrl = photoUrl.Trim();
        ThumbnailUrl = string.IsNullOrWhiteSpace(thumbnailUrl) ? null : thumbnailUrl.Trim();
        StorageKey = string.IsNullOrWhiteSpace(storageKey) ? null : storageKey.Trim();
        ThumbnailStorageKey = string.IsNullOrWhiteSpace(thumbnailStorageKey) ? null : thumbnailStorageKey.Trim();
        ContentType = string.IsNullOrWhiteSpace(contentType) ? null : contentType.Trim();
        FileSizeBytes = fileSizeBytes;
        Width = width;
        Height = height;
        ThumbnailWidth = thumbnailWidth;
        ThumbnailHeight = thumbnailHeight;
        DisplayOrder = (byte)displayOrder;
        CreatedAt = DateTime.UtcNow;
    }

    internal static PollPhoto CreateForPoll(Poll poll, string photoUrl, int displayOrder)
    {
        return new PollPhoto(
            poll,
            photoUrl,
            thumbnailUrl: null,
            storageKey: null,
            thumbnailStorageKey: null,
            contentType: null,
            fileSizeBytes: null,
            width: null,
            height: null,
            thumbnailWidth: null,
            thumbnailHeight: null,
            displayOrder);
    }

    internal static PollPhoto CreateUploadedForPoll(
        Poll poll,
        string photoUrl,
        string thumbnailUrl,
        string storageKey,
        string thumbnailStorageKey,
        string contentType,
        long fileSizeBytes,
        int width,
        int height,
        int thumbnailWidth,
        int thumbnailHeight,
        int displayOrder)
    {
        if (string.IsNullOrWhiteSpace(thumbnailUrl))
        {
            throw new DomainValidationException("ThumbnailUrl is required for uploaded photos.");
        }

        if (string.IsNullOrWhiteSpace(storageKey) || string.IsNullOrWhiteSpace(thumbnailStorageKey))
        {
            throw new DomainValidationException("Object storage keys are required for uploaded photos.");
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new DomainValidationException("ContentType is required for uploaded photos.");
        }

        return new PollPhoto(
            poll,
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
    }

    internal void SetDisplayOrder(int displayOrder)
    {
        if (displayOrder is < 1 or > Poll.MaxPhotos)
        {
            throw new DomainValidationException($"DisplayOrder must be between 1 and {Poll.MaxPhotos}.");
        }

        DisplayOrder = (byte)displayOrder;
    }
}
