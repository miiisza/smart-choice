using System.ComponentModel.DataAnnotations;
using SmartChoice.Domain.Entities;

namespace SmartChoice.Api.Contracts;

public sealed class CreatePollRequest : IValidatableObject
{
    [Required]
    [StringLength(280, MinimumLength = 3)]
    public string Question { get; init; } = string.Empty;

    [Required]
    [MaxLength(Poll.MaxPhotos)]
    public IReadOnlyCollection<string> PhotoUrls { get; init; } = [];

    [Required]
    [Range(-90d, 90d)]
    public double? Latitude { get; init; }

    [Required]
    [Range(-180d, 180d)]
    public double? Longitude { get; init; }

    [Required]
    [Range(Poll.MinRadiusMeters, Poll.MaxRadiusMeters)]
    public int? RadiusMeters { get; init; }

    public DateTime? StartsAt { get; init; }
    public DateTime? EndsAt { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartsAt.HasValue && EndsAt.HasValue && EndsAt <= StartsAt)
        {
            yield return new ValidationResult("EndsAt must be greater than StartsAt.", [nameof(EndsAt)]);
        }

        var photoUrls = PhotoUrls ?? [];

        if (photoUrls.Any(string.IsNullOrWhiteSpace))
        {
            yield return new ValidationResult("PhotoUrls cannot contain empty values.", [nameof(PhotoUrls)]);
        }

        var uniquePhotos = photoUrls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (uniquePhotos != photoUrls.Count)
        {
            yield return new ValidationResult("PhotoUrls must be unique.", [nameof(PhotoUrls)]);
        }

        if (Latitude.HasValue && (double.IsNaN(Latitude.Value) || double.IsInfinity(Latitude.Value)))
        {
            yield return new ValidationResult("Latitude must be a finite number.", [nameof(Latitude)]);
        }

        if (Longitude.HasValue && (double.IsNaN(Longitude.Value) || double.IsInfinity(Longitude.Value)))
        {
            yield return new ValidationResult("Longitude must be a finite number.", [nameof(Longitude)]);
        }
    }
}
