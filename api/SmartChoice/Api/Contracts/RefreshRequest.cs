using System.ComponentModel.DataAnnotations;

namespace SmartChoice.Api.Contracts;

public sealed class RefreshRequest
{
    [Required]
    [MinLength(32)]
    [StringLength(4096)]
    public string RefreshToken { get; init; } = string.Empty;
}
