using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using SmartChoice.Api.Auth;
using SmartChoice.Api.Contracts;
using SmartChoice.Api.Validation;
using SmartChoice.Data;
using SmartChoice.Data.Seeding;
using SmartChoice.Domain.Entities;
using SmartChoice.Domain.Enums;
using SmartChoice.Domain.Exceptions;

const string CorsPolicyName = "SmartChoiceDevCors";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddScoped<DevDataSeeder>();
builder.Services.AddSingleton<PasswordHashingService>();

var authOptions = ResolveAuthOptions(builder.Configuration);
builder.Services.AddSingleton(authOptions);
builder.Services.AddSingleton<JwtTokenService>();

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authOptions.SigningKey));
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateIssuer = true,
            ValidIssuer = authOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = authOptions.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var tokenType = context.Principal?.FindFirst(AuthClaimTypes.TokenType)?.Value;
                var isAccessToken = string.Equals(tokenType, AuthTokenTypes.Access, StringComparison.Ordinal)
                                    || string.Equals(tokenType, AuthTokenTypes.GuestAccess, StringComparison.Ordinal);

                if (!isAccessToken)
                {
                    context.Fail("Only access tokens can be used for API authorization.");
                }

                return Task.CompletedTask;
            },
            OnChallenge = async context =>
            {
                if (context.Handled || context.Response.HasStarted)
                {
                    return;
                }

                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/problem+json";

                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status401Unauthorized,
                    Title = "Unauthorized",
                    Detail = "A valid Bearer access token is required."
                };

                await context.Response.WriteAsJsonAsync(problem);
            },
            OnForbidden = async context =>
            {
                if (context.Response.HasStarted)
                {
                    return;
                }

                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/problem+json";

                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Forbidden",
                    Detail = "You are not allowed to access this resource."
                };

                await context.Response.WriteAsJsonAsync(problem);
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        AuthConstants.RegisteredUserPolicy,
        policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(AuthClaimTypes.ActorType, AuthActorTypes.User)
            .RequireClaim(AuthClaimTypes.TokenType, AuthTokenTypes.Access));
});

builder.Services.AddDbContext<SmartChoiceDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("Default")
                           ?? throw new InvalidOperationException("Missing ConnectionStrings:Default");

    options.UseMySQL(connectionString);
});

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck(
        "mysql-connection-string",
        () =>
        {
            var connectionString = builder.Configuration.GetConnectionString("Default");
            return string.IsNullOrWhiteSpace(connectionString)
                ? HealthCheckResult.Unhealthy("Missing ConnectionStrings:Default")
                : HealthCheckResult.Healthy();
        },
        tags: ["ready"]);

var allowedOrigins = ResolveAllowedOrigins(builder.Configuration);
builder.Services.AddCors(options =>
{
    options.AddPolicy(
        CorsPolicyName,
        policy =>
        {
            if (allowedOrigins.Length == 0)
            {
                policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
                return;
            }

            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
        });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    await ApplyMigrationsAndSeedAsync(app);
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseCors(CorsPolicyName);
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new { service = "smart-choice-api", status = "running" }));
app.MapHealthChecks("/health/live", CreateHealthOptions("live"));
app.MapHealthChecks("/health/ready", CreateHealthOptions("ready"));
app.MapGet("/health", () => Results.Redirect("/health/ready"));

var authGroup = app.MapGroup("/api/auth");

authGroup.MapPost(
    "/register",
    async (RegisterRequest request, SmartChoiceDbContext dbContext, PasswordHashingService passwordHashingService,
        JwtTokenService jwtTokenService, CancellationToken cancellationToken) =>
    {
        var validationErrors = RequestValidation.Validate(request);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        if (!PasswordHashingService.ValidatePassword(request.Password, out var passwordError))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Password)] = [passwordError]
            });
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var emailExists = await dbContext.Users
            .AsNoTracking()
            .AnyAsync(user => user.Email == normalizedEmail, cancellationToken);

        if (emailExists)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Email)] = ["Email is already registered."]
            });
        }

        var username = await GenerateUniqueUsernameAsync(dbContext, normalizedEmail, cancellationToken);
        var passwordHash = passwordHashingService.HashPassword(request.Password);

        try
        {
            var user = new User(username, normalizedEmail, passwordHash);
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync(cancellationToken);

            var tokens = jwtTokenService.CreateUserTokenPair(user, DateTime.UtcNow);
            var response = new AuthTokenResponse(
                tokens.AccessToken,
                tokens.AccessTokenExpiresAt,
                tokens.RefreshToken,
                tokens.RefreshTokenExpiresAt);

            return Results.Created($"/api/users/{user.Id}", response);
        }
        catch (DbUpdateException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Email)] = ["Email is already registered."]
            });
        }
        catch (DomainValidationException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["domain"] = [ex.Message]
            });
        }
    });

authGroup.MapPost(
    "/login",
    async (LoginRequest request, SmartChoiceDbContext dbContext, PasswordHashingService passwordHashingService,
        JwtTokenService jwtTokenService, CancellationToken cancellationToken) =>
    {
        var validationErrors = RequestValidation.Validate(request);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Email == normalizedEmail, cancellationToken);

        if (user is null || !user.IsActive || !passwordHashingService.VerifyPassword(request.Password, user.PasswordHash))
        {
            return UnauthorizedProblem("Invalid email or password.");
        }

        var tokens = jwtTokenService.CreateUserTokenPair(user, DateTime.UtcNow);
        return Results.Ok(new AuthTokenResponse(
            tokens.AccessToken,
            tokens.AccessTokenExpiresAt,
            tokens.RefreshToken,
            tokens.RefreshTokenExpiresAt));
    });

authGroup.MapPost(
    "/refresh",
    async (RefreshRequest request, SmartChoiceDbContext dbContext, JwtTokenService jwtTokenService,
        CancellationToken cancellationToken) =>
    {
        var validationErrors = RequestValidation.Validate(request);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        var principal = jwtTokenService.ValidateRefreshToken(request.RefreshToken);
        if (principal is null || !TryGetLongClaim(principal, ClaimTypes.NameIdentifier, out var userId))
        {
            return UnauthorizedProblem("Invalid refresh token.");
        }

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, cancellationToken);

        if (user is null)
        {
            return UnauthorizedProblem("Invalid refresh token.");
        }

        var tokens = jwtTokenService.CreateUserTokenPair(user, DateTime.UtcNow);
        return Results.Ok(new AuthTokenResponse(
            tokens.AccessToken,
            tokens.AccessTokenExpiresAt,
            tokens.RefreshToken,
            tokens.RefreshTokenExpiresAt));
    });

authGroup.MapPost(
    "/guest",
    async (IssueGuestTokenRequest request, SmartChoiceDbContext dbContext, JwtTokenService jwtTokenService,
        AuthOptions options, CancellationToken cancellationToken) =>
    {
        var validationErrors = RequestValidation.Validate(request);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        var inviteCode = request.InviteCode.Trim().ToUpperInvariant();
        var invite = await dbContext.Invites.SingleOrDefaultAsync(x => x.Code == inviteCode, cancellationToken);

        if (invite is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.InviteCode)] = ["Invite code is invalid."]
            });
        }

        var now = DateTime.UtcNow;
        if (!invite.CanUse(now))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Invite unavailable",
                detail: "Invite is expired, inactive, or has reached max uses.");
        }

        var guestTokenJti = TokenSecurity.CreateRandomToken(24);
        var guestTokenHash = TokenSecurity.Sha256(guestTokenJti);
        var ttlExpiresAt = now.AddHours(options.GuestTokenHours);
        var guestExpiresAt = ttlExpiresAt <= invite.ExpiresAt ? ttlExpiresAt : invite.ExpiresAt;

        try
        {
            invite.RegisterUse(now);
            var guestToken = new GuestToken(guestTokenHash, invite.Id, guestExpiresAt);
            dbContext.GuestTokens.Add(guestToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            var jwt = jwtTokenService.CreateGuestToken(guestToken.Id, invite.Id, guestTokenJti, guestExpiresAt);
            return Results.Ok(new GuestTokenResponse(jwt, guestExpiresAt));
        }
        catch (DomainValidationException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["domain"] = [ex.Message]
            });
        }
    });

app.MapPost(
        "/api/polls",
        async (CreatePollRequest request, HttpContext httpContext, SmartChoiceDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var validationErrors = RequestValidation.Validate(request);
            if (validationErrors.Count > 0)
            {
                return Results.ValidationProblem(validationErrors);
            }

            if (!TryGetLongClaim(httpContext.User, ClaimTypes.NameIdentifier, out var authorUserId))
            {
                return UnauthorizedProblem("Invalid user token.");
            }

            var authorExists = await dbContext.Users
                .AsNoTracking()
                .AnyAsync(user => user.Id == authorUserId && user.IsActive, cancellationToken);

            if (!authorExists)
            {
                return UnauthorizedProblem("Invalid user token.");
            }

            try
            {
                var poll = Poll.Create(
                    authorUserId,
                    request.Question,
                    request.PhotoUrls,
                    request.StartsAt,
                    request.EndsAt);

                dbContext.Polls.Add(poll);
                await dbContext.SaveChangesAsync(cancellationToken);

                return Results.Created($"/api/polls/{poll.Id}", new
                {
                    poll.Id,
                    poll.AuthorUserId,
                    poll.Question,
                    poll.Status,
                    Photos = poll.Photos
                        .OrderBy(photo => photo.DisplayOrder)
                        .Select(photo => new { photo.Id, photo.PhotoUrl, photo.DisplayOrder })
                });
            }
            catch (DomainValidationException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["domain"] = [ex.Message]
                });
            }
        })
    .RequireAuthorization(AuthConstants.RegisteredUserPolicy);

app.MapPost(
        "/api/votes",
        async (CastVoteRequest request, HttpContext httpContext, SmartChoiceDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var validationErrors = RequestValidation.Validate(request);
            if (validationErrors.Count > 0)
            {
                return Results.ValidationProblem(validationErrors);
            }

            var pollPhotoExists = await dbContext.PollPhotos
                .AsNoTracking()
                .AnyAsync(photo => photo.Id == request.PollPhotoId && photo.PollId == request.PollId, cancellationToken);

            if (!pollPhotoExists)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request.PollPhotoId)] = ["Poll photo does not exist for this poll."]
                });
            }

            var now = DateTime.UtcNow;

            try
            {
                Vote vote;
                if (HasActor(httpContext.User, AuthActorTypes.User))
                {
                    if (!TryGetLongClaim(httpContext.User, ClaimTypes.NameIdentifier, out var voterUserId))
                    {
                        return UnauthorizedProblem("Invalid user token.");
                    }

                    var userExists = await dbContext.Users
                        .AsNoTracking()
                        .AnyAsync(user => user.Id == voterUserId && user.IsActive, cancellationToken);

                    if (!userExists)
                    {
                        return UnauthorizedProblem("Invalid user token.");
                    }

                    vote = Vote.CreateByUser(request.PollId, request.PollPhotoId, voterUserId);
                }
                else if (HasActor(httpContext.User, AuthActorTypes.Guest))
                {
                    if (!TryGetLongClaim(httpContext.User, AuthClaimTypes.GuestTokenId, out var guestTokenId)
                        || !TryGetClaim(httpContext.User, JwtRegisteredClaimNames.Jti, out var guestTokenJti))
                    {
                        return UnauthorizedProblem("Invalid guest token.");
                    }

                    var guestToken = await dbContext.GuestTokens
                        .SingleOrDefaultAsync(token => token.Id == guestTokenId, cancellationToken);

                    if (guestToken is null || !guestToken.IsValid(now))
                    {
                        return UnauthorizedProblem("Guest token has expired or was revoked.");
                    }

                    var expectedTokenHash = TokenSecurity.Sha256(guestTokenJti);
                    if (!string.Equals(guestToken.TokenHash, expectedTokenHash, StringComparison.OrdinalIgnoreCase))
                    {
                        return UnauthorizedProblem("Invalid guest token.");
                    }

                    guestToken.MarkUsed(now);
                    vote = Vote.CreateByGuest(request.PollId, request.PollPhotoId, guestTokenId);
                }
                else
                {
                    return UnauthorizedProblem("Unsupported token actor.");
                }

                dbContext.Votes.Add(vote);
                await dbContext.SaveChangesAsync(cancellationToken);

                return Results.Created($"/api/votes/{vote.Id}", new
                {
                    vote.Id,
                    vote.PollId,
                    vote.PollPhotoId,
                    vote.VoterUserId,
                    vote.GuestTokenId,
                    vote.VotedAt
                });
            }
            catch (DbUpdateException ex) when (IsVoteUniquenessViolation(ex))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Duplicate vote",
                    detail: "Vote already exists for this voter and poll.");
            }
            catch (DomainValidationException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["domain"] = [ex.Message]
                });
            }
        })
    .RequireAuthorization();

app.MapGet(
    "/api/polls/feed",
    async (SmartChoiceDbContext dbContext, int take = 20, CancellationToken cancellationToken = default) =>
    {
        var safeTake = Math.Clamp(take, 1, 100);

        var feed = await dbContext.Polls
            .AsNoTracking()
            .Where(poll => poll.Status == PollStatus.Open)
            .OrderByDescending(poll => poll.CreatedAt)
            .Take(safeTake)
            .Select(poll => new
            {
                poll.Id,
                poll.Question,
                poll.CreatedAt,
                poll.EndsAt,
                Photos = poll.Photos
                    .OrderBy(photo => photo.DisplayOrder)
                    .Select(photo => new { photo.Id, photo.PhotoUrl, photo.DisplayOrder })
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(feed);
    });

app.MapGet(
    "/api/polls/{pollId:long}/results",
    async (long pollId, SmartChoiceDbContext dbContext, CancellationToken cancellationToken) =>
    {
        var pollExists = await dbContext.Polls.AsNoTracking().AnyAsync(poll => poll.Id == pollId, cancellationToken);
        if (!pollExists)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Poll not found",
                detail: $"Poll with id {pollId} does not exist.");
        }

        var results = await dbContext.PollPhotos
            .AsNoTracking()
            .Where(photo => photo.PollId == pollId)
            .OrderBy(photo => photo.DisplayOrder)
            .Select(photo => new
            {
                photo.Id,
                photo.PhotoUrl,
                photo.DisplayOrder,
                Votes = photo.Votes.Count
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(results);
    });

app.Run();

static string[] ResolveAllowedOrigins(IConfiguration configuration)
{
    const string fallbackConfigPath = "Cors:AllowedOrigins";
    const string envVarName = "SMARTCHOICE_CORS_ORIGINS";

    var fromEnv = configuration[envVarName];
    if (!string.IsNullOrWhiteSpace(fromEnv))
    {
        return fromEnv
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    return configuration.GetSection(fallbackConfigPath).Get<string[]>() ?? [];
}

static AuthOptions ResolveAuthOptions(IConfiguration configuration)
{
    var options = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();

    var issuer = options.Issuer.Trim();
    var audience = options.Audience.Trim();
    var signingKey = options.SigningKey.Trim();

    if (string.IsNullOrWhiteSpace(issuer))
    {
        throw new InvalidOperationException("Missing Auth:Issuer.");
    }

    if (string.IsNullOrWhiteSpace(audience))
    {
        throw new InvalidOperationException("Missing Auth:Audience.");
    }

    if (string.IsNullOrWhiteSpace(signingKey) || signingKey.Length < 32)
    {
        throw new InvalidOperationException("Missing or invalid Auth:SigningKey (minimum 32 characters).");
    }

    if (options.AccessTokenMinutes <= 0 || options.RefreshTokenDays <= 0 || options.GuestTokenHours <= 0)
    {
        throw new InvalidOperationException("Auth token TTL values must be greater than zero.");
    }

    return new AuthOptions
    {
        Issuer = issuer,
        Audience = audience,
        SigningKey = signingKey,
        AccessTokenMinutes = options.AccessTokenMinutes,
        RefreshTokenDays = options.RefreshTokenDays,
        GuestTokenHours = options.GuestTokenHours
    };
}

static HealthCheckOptions CreateHealthOptions(string tag)
{
    return new HealthCheckOptions
    {
        Predicate = registration => registration.Tags.Contains(tag),
        ResponseWriter = async (context, report) =>
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = report.Status == HealthStatus.Healthy
                ? StatusCodes.Status200OK
                : StatusCodes.Status503ServiceUnavailable;

            var payload = new
            {
                status = report.Status.ToString(),
                checks = report.Entries.Select(entry => new
                {
                    name = entry.Key,
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description
                })
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
        }
    };
}

static async Task ApplyMigrationsAndSeedAsync(WebApplication app)
{
    var autoMigrate = app.Configuration.GetValue("Database:AutoMigrateOnStartup", true);
    var seedDevData = app.Configuration.GetValue("Database:SeedDevDataOnStartup", true);

    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("StartupDatabase");
    var dbContext = scope.ServiceProvider.GetRequiredService<SmartChoiceDbContext>();

    if (autoMigrate)
    {
        await dbContext.Database.MigrateAsync();
        logger.LogInformation("Database migrations applied.");
    }

    if (seedDevData)
    {
        var seeder = scope.ServiceProvider.GetRequiredService<DevDataSeeder>();
        await seeder.SeedAsync();
    }
}

static bool IsVoteUniquenessViolation(DbUpdateException exception)
{
    var message = exception.ToString();
    return message.Contains("ux_votes_poll_user", StringComparison.OrdinalIgnoreCase)
           || message.Contains("ux_votes_poll_guest_token", StringComparison.OrdinalIgnoreCase)
           || message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase)
           || message.Contains("duplicate", StringComparison.OrdinalIgnoreCase);
}

static IResult UnauthorizedProblem(string detail)
{
    return Results.Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: "Unauthorized",
        detail: detail);
}

static bool TryGetLongClaim(ClaimsPrincipal principal, string claimType, out long value)
{
    value = 0;
    var raw = principal.FindFirst(claimType)?.Value;
    return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0;
}

static bool TryGetClaim(ClaimsPrincipal principal, string claimType, out string value)
{
    value = principal.FindFirst(claimType)?.Value ?? string.Empty;
    return !string.IsNullOrWhiteSpace(value);
}

static bool HasActor(ClaimsPrincipal principal, string actorType)
{
    var value = principal.FindFirst(AuthClaimTypes.ActorType)?.Value;
    return string.Equals(value, actorType, StringComparison.Ordinal);
}

static async Task<string> GenerateUniqueUsernameAsync(
    SmartChoiceDbContext dbContext,
    string normalizedEmail,
    CancellationToken cancellationToken)
{
    var emailPrefix = normalizedEmail.Split('@', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .FirstOrDefault() ?? "user";

    var baseUsername = NormalizeUsernameCandidate(emailPrefix);
    if (string.IsNullOrWhiteSpace(baseUsername))
    {
        baseUsername = $"user_{TokenSecurity.CreateRandomToken(4).ToLowerInvariant()}";
    }

    if (baseUsername.Length > 64)
    {
        baseUsername = baseUsername[..64];
    }

    var candidate = baseUsername;
    var suffix = 1;

    while (await dbContext.Users.AsNoTracking().AnyAsync(user => user.Username == candidate, cancellationToken))
    {
        var suffixText = suffix.ToString(CultureInfo.InvariantCulture);
        var maxBaseLength = Math.Max(1, 64 - suffixText.Length - 1);
        var trimmedBase = baseUsername.Length > maxBaseLength ? baseUsername[..maxBaseLength] : baseUsername;
        candidate = $"{trimmedBase}_{suffixText}";
        suffix++;
    }

    return candidate;
}

static string NormalizeUsernameCandidate(string value)
{
    var chars = value
        .Trim()
        .ToLowerInvariant()
        .Where(character => char.IsLetterOrDigit(character) || character is '_' or '.')
        .ToArray();

    return new string(chars);
}
