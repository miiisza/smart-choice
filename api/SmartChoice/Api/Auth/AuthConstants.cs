namespace SmartChoice.Api.Auth;

public static class AuthConstants
{
    public const string RegisteredUserPolicy = "registered-user";
}

public static class AuthClaimTypes
{
    public const string ActorType = "actor_type";
    public const string TokenType = "token_type";
    public const string GuestTokenId = "guest_token_id";
    public const string InviteId = "invite_id";
    public const string PollId = "poll_id";
}

public static class AuthActorTypes
{
    public const string User = "user";
    public const string Guest = "guest";
}

public static class AuthTokenTypes
{
    public const string Access = "access";
    public const string Refresh = "refresh";
    public const string GuestAccess = "guest_access";
}
