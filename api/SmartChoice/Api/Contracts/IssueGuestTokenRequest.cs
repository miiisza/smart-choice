using System.ComponentModel.DataAnnotations;

namespace SmartChoice.Api.Contracts;

public sealed class IssueGuestTokenRequest
{
    [Required]
    [StringLength(64, MinimumLength = 3)]
    public string InviteCode { get; init; } = string.Empty;
}
