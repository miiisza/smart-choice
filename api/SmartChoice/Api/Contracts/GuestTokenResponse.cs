namespace SmartChoice.Api.Contracts;

public sealed record GuestTokenResponse(
    string GuestToken,
    DateTime ExpiresAt,
    long PollId,
    string TokenType = "Bearer");
