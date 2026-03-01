using System.ComponentModel.DataAnnotations;

namespace SmartChoice.Api.Contracts;

public sealed class CastVoteRequest : IValidatableObject
{
    [Required]
    [Range(1, long.MaxValue)]
    public long PollId { get; init; }

    [Required]
    [Range(1, long.MaxValue)]
    public long PollPhotoId { get; init; }

    [Range(1, long.MaxValue)]
    public long? VoterUserId { get; init; }

    [Range(1, long.MaxValue)]
    public long? GuestTokenId { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var hasUser = VoterUserId.HasValue;
        var hasGuest = GuestTokenId.HasValue;

        if (hasUser == hasGuest)
        {
            yield return new ValidationResult(
                "Provide exactly one voter identity: VoterUserId or GuestTokenId.",
                [nameof(VoterUserId), nameof(GuestTokenId)]);
        }
    }
}
