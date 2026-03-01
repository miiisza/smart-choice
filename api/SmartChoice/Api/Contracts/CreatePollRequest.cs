using System.ComponentModel.DataAnnotations;
using SmartChoice.Domain.Entities;

namespace SmartChoice.Api.Contracts;

public sealed class CreatePollRequest : IValidatableObject
{
    [Required]
    [Range(1, long.MaxValue)]
    public long AuthorUserId { get; init; }

    [Required]
    [StringLength(280, MinimumLength = 3)]
    public string Question { get; init; } = string.Empty;

    [Required]
    [MinLength(Poll.MinPhotos)]
    [MaxLength(Poll.MaxPhotos)]
    public IReadOnlyCollection<string> PhotoUrls { get; init; } = [];

    public DateTime? StartsAt { get; init; }
    public DateTime? EndsAt { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartsAt.HasValue && EndsAt.HasValue && EndsAt <= StartsAt)
        {
            yield return new ValidationResult("EndsAt must be greater than StartsAt.", [nameof(EndsAt)]);
        }

        if (PhotoUrls.Any(string.IsNullOrWhiteSpace))
        {
            yield return new ValidationResult("PhotoUrls cannot contain empty values.", [nameof(PhotoUrls)]);
        }

        var uniquePhotos = PhotoUrls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (uniquePhotos != PhotoUrls.Count)
        {
            yield return new ValidationResult("PhotoUrls must be unique.", [nameof(PhotoUrls)]);
        }
    }
}
