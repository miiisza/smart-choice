namespace SmartChoice.Api.Contracts;

public sealed record GuestTokenResponse(
    string GuestToken,
    DateTime ExpiresAt,
    string TokenType = "Bearer");
