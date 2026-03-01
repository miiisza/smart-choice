namespace SmartChoice.Api.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string Issuer { get; init; } = "smart-choice-api";
    public string Audience { get; init; } = "smart-choice-client";
    public string SigningKey { get; init; } = string.Empty;
    public int AccessTokenMinutes { get; init; } = 30;
    public int RefreshTokenDays { get; init; } = 14;
    public int GuestTokenHours { get; init; } = 24;
}
