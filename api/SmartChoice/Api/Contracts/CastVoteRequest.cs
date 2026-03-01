using System.ComponentModel.DataAnnotations;

namespace SmartChoice.Api.Contracts;

public sealed class CastVoteRequest
{
    [Required]
    [Range(1, long.MaxValue)]
    public long PollId { get; init; }

    [Required]
    [Range(1, long.MaxValue)]
    public long PollPhotoId { get; init; }
}
