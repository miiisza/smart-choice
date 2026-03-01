using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using SmartChoice.Domain.Entities;

namespace SmartChoice.Api.Auth;

public sealed record UserTokenPair(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt);

public sealed class JwtTokenService
{
    private readonly JwtSecurityTokenHandler _tokenHandler = new();
    private readonly AuthOptions _authOptions;
    private readonly SigningCredentials _signingCredentials;
    private readonly TokenValidationParameters _refreshValidationParameters;

    public JwtTokenService(AuthOptions authOptions)
    {
        _authOptions = authOptions;

        var signingKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(_authOptions.SigningKey));
        _signingCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        _refreshValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateIssuer = true,
            ValidIssuer = _authOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = _authOptions.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    }

    public UserTokenPair CreateUserTokenPair(User user, DateTime utcNow)
    {
        var userId = user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var accessExpiresAt = utcNow.AddMinutes(_authOptions.AccessTokenMinutes);
        var accessClaims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(ClaimTypes.NameIdentifier, userId),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(AuthClaimTypes.ActorType, AuthActorTypes.User),
            new(AuthClaimTypes.TokenType, AuthTokenTypes.Access),
            new(JwtRegisteredClaimNames.Jti, TokenSecurity.CreateRandomToken(16))
        };

        var refreshExpiresAt = utcNow.AddDays(_authOptions.RefreshTokenDays);
        var refreshClaims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(ClaimTypes.NameIdentifier, userId),
            new(AuthClaimTypes.ActorType, AuthActorTypes.User),
            new(AuthClaimTypes.TokenType, AuthTokenTypes.Refresh),
            new(JwtRegisteredClaimNames.Jti, TokenSecurity.CreateRandomToken(16))
        };

        return new UserTokenPair(
            AccessToken: CreateToken(accessClaims, accessExpiresAt),
            AccessTokenExpiresAt: accessExpiresAt,
            RefreshToken: CreateToken(refreshClaims, refreshExpiresAt),
            RefreshTokenExpiresAt: refreshExpiresAt);
    }

    public string CreateGuestToken(long guestTokenId, long inviteId, string guestTokenJti, DateTime expiresAtUtc)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, $"guest:{guestTokenId}"),
            new(AuthClaimTypes.ActorType, AuthActorTypes.Guest),
            new(AuthClaimTypes.TokenType, AuthTokenTypes.GuestAccess),
            new(AuthClaimTypes.GuestTokenId, guestTokenId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(AuthClaimTypes.InviteId, inviteId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(JwtRegisteredClaimNames.Jti, guestTokenJti)
        };

        return CreateToken(claims, expiresAtUtc);
    }

    public ClaimsPrincipal? ValidateRefreshToken(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        try
        {
            var principal = _tokenHandler.ValidateToken(refreshToken, _refreshValidationParameters, out var token);
            if (token is not JwtSecurityToken jwtToken)
            {
                return null;
            }

            if (!string.Equals(jwtToken.Header.Alg, SecurityAlgorithms.HmacSha256, StringComparison.Ordinal))
            {
                return null;
            }

            var tokenType = principal.FindFirst(AuthClaimTypes.TokenType)?.Value;
            var actorType = principal.FindFirst(AuthClaimTypes.ActorType)?.Value;

            if (!string.Equals(tokenType, AuthTokenTypes.Refresh, StringComparison.Ordinal)
                || !string.Equals(actorType, AuthActorTypes.User, StringComparison.Ordinal))
            {
                return null;
            }

            return principal;
        }
        catch (SecurityTokenException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private string CreateToken(IEnumerable<Claim> claims, DateTime expiresAtUtc)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expiresAtUtc,
            Issuer = _authOptions.Issuer,
            Audience = _authOptions.Audience,
            SigningCredentials = _signingCredentials
        };

        var token = _tokenHandler.CreateToken(descriptor);
        return _tokenHandler.WriteToken(token);
    }
}
