using SmartChoice.Domain.Exceptions;

namespace SmartChoice.Domain.Entities;

public sealed class PollPhoto
{
    public long Id { get; private set; }
    public long PollId { get; private set; }
    public string PhotoUrl { get; private set; } = string.Empty;
    public byte DisplayOrder { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    public Poll? Poll { get; private set; }
    public ICollection<Vote> Votes { get; } = new List<Vote>();

    private PollPhoto()
    {
    }

    private PollPhoto(Poll poll, string photoUrl, int displayOrder)
    {
        if (displayOrder is < 1 or > Poll.MaxPhotos)
        {
            throw new DomainValidationException($"DisplayOrder must be between 1 and {Poll.MaxPhotos}.");
        }

        if (string.IsNullOrWhiteSpace(photoUrl))
        {
            throw new DomainValidationException("PhotoUrl is required.");
        }

        Poll = poll;
        PollId = poll.Id;
        PhotoUrl = photoUrl.Trim();
        DisplayOrder = (byte)displayOrder;
        CreatedAt = DateTime.UtcNow;
    }

    internal static PollPhoto CreateForPoll(Poll poll, string photoUrl, int displayOrder)
    {
        return new PollPhoto(poll, photoUrl, displayOrder);
    }
}
