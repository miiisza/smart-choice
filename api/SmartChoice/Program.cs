using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Data;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using SmartChoice.Api.Auth;
using SmartChoice.Api.Contracts;
using SmartChoice.Api.Media;
using SmartChoice.Api.Storage;
using SmartChoice.Api.Validation;
using SmartChoice.Data;
using SmartChoice.Data.Seeding;
using SmartChoice.Domain.Entities;
using SmartChoice.Domain.Enums;
using SmartChoice.Domain.Exceptions;

const string CorsPolicyName = "SmartChoiceDevCors";
const string InviteIssueRateLimitPolicyName = "invite-issue";
const string PollCreateRateLimitPolicyName = "poll-create";
const string VoteCastRateLimitPolicyName = "vote-cast";
const int FeedPageSize = 20;

static string ProblemTypeForStatus(int statusCode)
{
    return $"https://httpstatuses.com/{statusCode}";
}

static async Task WriteProblemDetailsAsync(
    HttpContext httpContext,
    int statusCode,
    string title,
    string detail,
    CancellationToken cancellationToken,
    IDictionary<string, object?>? extensions = null)
{
    httpContext.Response.StatusCode = statusCode;
    httpContext.Response.ContentType = "application/problem+json";

    var problem = new ProblemDetails
    {
        Status = statusCode,
        Title = title,
        Detail = detail,
        Type = ProblemTypeForStatus(statusCode),
        Instance = httpContext.Request.Path
    };
    problem.Extensions["traceId"] = httpContext.TraceIdentifier;
    if (extensions is not null)
    {
        foreach (var extension in extensions)
        {
            problem.Extensions[extension.Key] = extension.Value;
        }
    }

    await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken: cancellationToken);
}

static string GetActorLabel(ClaimsPrincipal principal)
{
    var userIdRaw = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (long.TryParse(userIdRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId) && userId > 0)
    {
        return $"user:{userId}";
    }

    var guestTokenIdRaw = principal.FindFirst(AuthClaimTypes.GuestTokenId)?.Value;
    if (long.TryParse(guestTokenIdRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var guestTokenId)
        && guestTokenId > 0)
    {
        return $"guest:{guestTokenId}";
    }

    var actorType = principal.FindFirst(AuthClaimTypes.ActorType)?.Value;
    if (!string.IsNullOrWhiteSpace(actorType))
    {
        return $"{actorType}:unknown";
    }

    return "anonymous";
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        var problem = context.ProblemDetails;
        problem.Instance ??= context.HttpContext.Request.Path;
        if (problem.Status is int statusCode)
        {
            problem.Type ??= ProblemTypeForStatus(statusCode);
        }

        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    };
});
builder.Services.AddScoped<DevDataSeeder>();
builder.Services.AddSingleton<PasswordHashingService>();

var authOptions = ResolveAuthOptions(builder.Configuration);
builder.Services.AddSingleton(authOptions);
builder.Services.AddSingleton<JwtTokenService>();

var objectStorageOptions = ResolveObjectStorageOptions(builder.Configuration);
builder.Services.AddSingleton(objectStorageOptions);
builder.Services.AddSingleton<ImageProcessingService>();
if (objectStorageOptions.Provider == ObjectStorageProvider.S3Compatible)
{
    builder.Services.AddSingleton<IAmazonS3>(_ =>
    {
        var config = new AmazonS3Config
        {
            ForcePathStyle = objectStorageOptions.ForcePathStyle,
            RegionEndpoint = RegionEndpoint.GetBySystemName(objectStorageOptions.Region)
        };

        if (!string.IsNullOrWhiteSpace(objectStorageOptions.ServiceUrl))
        {
            config.ServiceURL = objectStorageOptions.ServiceUrl.TrimEnd('/');
            config.AuthenticationRegion = objectStorageOptions.Region;
        }

        var hasExplicitCredentials = !string.IsNullOrWhiteSpace(objectStorageOptions.AccessKey)
                                     && !string.IsNullOrWhiteSpace(objectStorageOptions.SecretKey);

        return hasExplicitCredentials
            ? new AmazonS3Client(
                new BasicAWSCredentials(objectStorageOptions.AccessKey, objectStorageOptions.SecretKey),
                config)
            : new AmazonS3Client(config);
    });
    builder.Services.AddSingleton<IObjectStorageService, S3ObjectStorageService>();
}
else
{
    builder.Services.AddSingleton<LocalDiskObjectStorageService>();
    builder.Services.AddSingleton<IObjectStorageService>(
        provider => provider.GetRequiredService<LocalDiskObjectStorageService>());
}
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = objectStorageOptions.MaxUploadBytes + 1024 * 1024;
});

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
                await WriteProblemDetailsAsync(
                    context.HttpContext,
                    StatusCodes.Status401Unauthorized,
                    "Unauthorized",
                    "A valid Bearer access token is required.",
                    context.HttpContext.RequestAborted);
            },
            OnForbidden = async context =>
            {
                if (context.Response.HasStarted)
                {
                    return;
                }

                await WriteProblemDetailsAsync(
                    context.HttpContext,
                    StatusCodes.Status403Forbidden,
                    "Forbidden",
                    "You are not allowed to access this resource.",
                    context.HttpContext.RequestAborted);
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

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("RateLimiting");
        logger.LogWarning(
            "Rate limit exceeded for {Method} {Path}. Actor={Actor}; RemoteIp={RemoteIp}",
            context.HttpContext.Request.Method,
            context.HttpContext.Request.Path,
            GetActorLabel(context.HttpContext.User),
            context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

        Dictionary<string, object?>? extensions = null;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
            context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            extensions = new Dictionary<string, object?>
            {
                ["retryAfterSeconds"] = retryAfterSeconds
            };
        }

        await WriteProblemDetailsAsync(
            context.HttpContext,
            StatusCodes.Status429TooManyRequests,
            "Too many requests",
            "Rate limit exceeded. Please retry later.",
            cancellationToken,
            extensions);
    };

    options.AddPolicy(
        InviteIssueRateLimitPolicyName,
        context => RateLimitPartition.GetFixedWindowLimiter(
            ResolveRateLimitPartitionKey(context, "invite"),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 8,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy(
        PollCreateRateLimitPolicyName,
        context => RateLimitPartition.GetFixedWindowLimiter(
            ResolveRateLimitPartitionKey(context, "poll-create"),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy(
        VoteCastRateLimitPolicyName,
        context => RateLimitPartition.GetFixedWindowLimiter(
            ResolveRateLimitPartitionKey(context, "vote"),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 12,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    await ApplyMigrationsAndSeedAsync(app);
}

await EnsureObjectStorageReadyAsync(app);

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseCors(CorsPolicyName);
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new { service = "smart-choice-api", status = "running" }));
app.MapHealthChecks("/health/live", CreateHealthOptions("live"));
app.MapHealthChecks("/health/ready", CreateHealthOptions("ready"));
app.MapGet("/health", () => Results.Redirect("/health/ready"));

if (objectStorageOptions.Provider == ObjectStorageProvider.LocalDisk)
{
    app.MapGet(
        "/api/storage/local/{**key}",
        async (string key, long expires, string sig, LocalDiskObjectStorageService localDiskStorage,
            CancellationToken cancellationToken) =>
        {
            if (!localDiskStorage.IsSignedReadRequestValid(key, expires, sig))
            {
                return ProblemResponse(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Forbidden",
                    detail: "Signed object URL is invalid or has expired.");
            }

            var fileObject = await localDiskStorage.OpenReadAsync(key, cancellationToken);
            if (fileObject is null)
            {
                return ProblemResponse(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Object not found",
                    detail: "Requested object does not exist.");
            }

            return Results.Stream(fileObject.Content, fileObject.ContentType);
        });
}

var authGroup = app.MapGroup("/api/auth");

authGroup.MapPost(
    "/register",
    async (RegisterRequest request, SmartChoiceDbContext dbContext, PasswordHashingService passwordHashingService,
        JwtTokenService jwtTokenService, CancellationToken cancellationToken) =>
    {
        var validationErrors = RequestValidation.Validate(request);
        if (validationErrors.Count > 0)
        {
            return ValidationProblemResponse(validationErrors);
        }

        if (!PasswordHashingService.ValidatePassword(request.Password, out var passwordError))
        {
            return ValidationProblemResponse(new Dictionary<string, string[]>
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
            return ValidationProblemResponse(new Dictionary<string, string[]>
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
            return ValidationProblemResponse(new Dictionary<string, string[]>
            {
                [nameof(request.Email)] = ["Email is already registered."]
            });
        }
        catch (DomainValidationException ex)
        {
            return ValidationProblemResponse(new Dictionary<string, string[]>
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
            return ValidationProblemResponse(validationErrors);
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
            return ValidationProblemResponse(validationErrors);
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
        AuthOptions options, ILogger<Program> logger, CancellationToken cancellationToken) =>
    {
        var validationErrors = RequestValidation.Validate(request);
        if (validationErrors.Count > 0)
        {
            logger.LogWarning(
                "Guest token request validation failed. InviteCode={InviteCode}; ErrorCount={ErrorCount}",
                MaskInviteCode(request.InviteCode),
                validationErrors.Count);
            return ValidationProblemResponse(validationErrors);
        }

        var poll = await dbContext.Polls
            .AsNoTracking()
            .Where(x => x.Id == request.PollId)
            .Select(x => new { x.Id, x.Status })
            .SingleOrDefaultAsync(cancellationToken);

        if (poll is null)
        {
            logger.LogWarning(
                "Guest token request rejected: poll not found. PollId={PollId}; InviteCode={InviteCode}",
                request.PollId,
                MaskInviteCode(request.InviteCode));
            return ProblemResponse(
                statusCode: StatusCodes.Status404NotFound,
                title: "Poll not found",
                detail: $"Poll with id {request.PollId} does not exist.");
        }

        if (poll.Status != PollStatus.Open)
        {
            logger.LogWarning(
                "Guest token request rejected: poll is not open. PollId={PollId}; Status={Status}; InviteCode={InviteCode}",
                poll.Id,
                poll.Status,
                MaskInviteCode(request.InviteCode));
            return ProblemResponse(
                statusCode: StatusCodes.Status409Conflict,
                title: "Poll unavailable",
                detail: "Guest tokens can only be issued for open polls.");
        }

        var inviteCode = request.InviteCode.Trim().ToUpperInvariant();
        var now = DateTime.UtcNow;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var claimedInviteCount = await dbContext.Invites
                .Where(x => x.Code == inviteCode
                            && x.IsActive
                            && x.ExpiresAt > now
                            && x.UsedCount < x.MaxUses)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(x => x.UsedCount, x => x.UsedCount + 1)
                        .SetProperty(x => x.UpdatedAt, _ => now),
                    cancellationToken);

            if (claimedInviteCount == 0)
            {
                await transaction.RollbackAsync(cancellationToken);

                var unavailableInvite = await dbContext.Invites
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Code == inviteCode, cancellationToken);

                if (unavailableInvite is null)
                {
                    logger.LogWarning(
                        "Guest token request rejected: invite not found. PollId={PollId}; InviteCode={InviteCode}",
                        request.PollId,
                        MaskInviteCode(inviteCode));
                    return ValidationProblemResponse(new Dictionary<string, string[]>
                    {
                        [nameof(request.InviteCode)] = ["Invite code is invalid."]
                    });
                }

                logger.LogWarning(
                    "Guest token request rejected: invite unavailable. PollId={PollId}; InviteId={InviteId}; IsActive={IsActive}; UsedCount={UsedCount}; MaxUses={MaxUses}; ExpiresAt={ExpiresAt}",
                    request.PollId,
                    unavailableInvite.Id,
                    unavailableInvite.IsActive,
                    unavailableInvite.UsedCount,
                    unavailableInvite.MaxUses,
                    unavailableInvite.ExpiresAt);
                return ProblemResponse(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Invite unavailable",
                    detail: "Invite is expired, inactive, or has reached max uses.");
            }

            var invite = await dbContext.Invites
                .AsNoTracking()
                .SingleAsync(x => x.Code == inviteCode, cancellationToken);
            var guestTokenJti = TokenSecurity.CreateRandomToken(24);
            var guestTokenHash = TokenSecurity.Sha256(guestTokenJti);
            var ttlExpiresAt = now.AddHours(options.GuestTokenHours);
            var guestExpiresAt = ttlExpiresAt <= invite.ExpiresAt ? ttlExpiresAt : invite.ExpiresAt;
            var guestToken = new GuestToken(guestTokenHash, invite.Id, request.PollId, guestExpiresAt);
            dbContext.GuestTokens.Add(guestToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var jwt = jwtTokenService.CreateGuestToken(
                guestToken.Id,
                invite.Id,
                request.PollId,
                guestTokenJti,
                guestExpiresAt);
            logger.LogInformation(
                "Guest token issued. PollId={PollId}; InviteId={InviteId}; GuestTokenId={GuestTokenId}; ExpiresAt={ExpiresAt}",
                request.PollId,
                invite.Id,
                guestToken.Id,
                guestExpiresAt);
            return Results.Ok(new GuestTokenResponse(jwt, guestExpiresAt, request.PollId));
        }
        catch (DomainValidationException ex)
        {
            logger.LogWarning(
                "Guest token request failed domain validation. InviteCode={InviteCode}; Error={Error}",
                MaskInviteCode(inviteCode),
                ex.Message);
            return ValidationProblemResponse(new Dictionary<string, string[]>
            {
                ["domain"] = [ex.Message]
            });
        }
    })
    .RequireRateLimiting(InviteIssueRateLimitPolicyName);

app.MapPost(
        "/api/polls",
        async (CreatePollRequest request, HttpContext httpContext, SmartChoiceDbContext dbContext,
            ILogger<Program> logger, IObjectStorageService objectStorageService,
            ObjectStorageOptions storageOptions, CancellationToken cancellationToken) =>
        {
            var validationErrors = RequestValidation.Validate(request);
            if (validationErrors.Count > 0)
            {
                logger.LogWarning(
                    "Poll draft create validation failed. Actor={Actor}; ErrorCount={ErrorCount}",
                    GetActorLabel(httpContext.User),
                    validationErrors.Count);
                return ValidationProblemResponse(validationErrors);
            }

            var authorUserId = await GetActiveUserIdAsync(httpContext.User, dbContext, cancellationToken);
            if (!authorUserId.HasValue)
            {
                logger.LogWarning(
                    "Poll draft create rejected: invalid user token. Actor={Actor}",
                    GetActorLabel(httpContext.User));
                return UnauthorizedProblem("Invalid user token.");
            }

            try
            {
                var poll = Poll.CreateDraft(
                    authorUserId.Value,
                    request.Question,
                    request.PhotoUrls,
                    request.Latitude.GetValueOrDefault(),
                    request.Longitude.GetValueOrDefault(),
                    request.RadiusMeters.GetValueOrDefault(),
                    request.StartsAt,
                    request.EndsAt);

                dbContext.Polls.Add(poll);
                await dbContext.SaveChangesAsync(cancellationToken);

                logger.LogInformation(
                    "Poll draft created. PollId={PollId}; AuthorUserId={AuthorUserId}; PhotoCount={PhotoCount}",
                    poll.Id,
                    poll.AuthorUserId,
                    poll.Photos.Count);
                var response = await ToPollDtoAsync(poll, objectStorageService, storageOptions, cancellationToken);
                return Results.Created($"/api/polls/{poll.Id}", response);
            }
            catch (DomainValidationException ex)
            {
                logger.LogWarning(
                    "Poll draft create rejected by domain validation. AuthorUserId={AuthorUserId}; Error={Error}",
                    authorUserId.Value,
                    ex.Message);
                return ValidationProblemResponse(new Dictionary<string, string[]>
                {
                    ["domain"] = [ex.Message]
                });
            }
        })
    .RequireAuthorization(AuthConstants.RegisteredUserPolicy)
    .RequireRateLimiting(PollCreateRateLimitPolicyName);

app.MapPost(
        "/api/polls/{pollId:long}/photos",
        async (long pollId, [FromForm] IFormFile? file, HttpContext httpContext, SmartChoiceDbContext dbContext,
            IObjectStorageService objectStorageService, ImageProcessingService imageProcessingService,
            ObjectStorageOptions storageOptions, CancellationToken cancellationToken) =>
        {
            if (file is null)
            {
                return ValidationProblemResponse(new Dictionary<string, string[]>
                {
                    ["file"] = ["File is required."]
                });
            }

            if (file.Length <= 0)
            {
                return ValidationProblemResponse(new Dictionary<string, string[]>
                {
                    ["file"] = ["File cannot be empty."]
                });
            }

            if (file.Length > storageOptions.MaxUploadBytes)
            {
                return ValidationProblemResponse(new Dictionary<string, string[]>
                {
                    ["file"] = [$"Max file size is {storageOptions.MaxUploadBytes} bytes."]
                });
            }

            var normalizedContentType = NormalizeImageContentType(file.ContentType);
            if (normalizedContentType is null)
            {
                return ValidationProblemResponse(new Dictionary<string, string[]>
                {
                    ["file"] = ["Unsupported image type. Allowed types: image/jpeg, image/png, image/webp."]
                });
            }

            var authorUserId = await GetActiveUserIdAsync(httpContext.User, dbContext, cancellationToken);
            if (!authorUserId.HasValue)
            {
                return UnauthorizedProblem("Invalid user token.");
            }

            var poll = await dbContext.Polls
                .Include(x => x.Photos)
                .SingleOrDefaultAsync(x => x.Id == pollId, cancellationToken);

            if (poll is null)
            {
                return ProblemResponse(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Poll not found",
                    detail: $"Poll with id {pollId} does not exist.");
            }

            if (poll.AuthorUserId != authorUserId.Value)
            {
                return ProblemResponse(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Forbidden",
                    detail: "Only the poll author can upload photos for this poll.");
            }

            if (poll.Status != PollStatus.Draft)
            {
                return ProblemResponse(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Poll photo upload rejected",
                    detail: "Photos can only be uploaded for draft polls.");
            }

            if (poll.Photos.Count >= Poll.MaxPhotos)
            {
                return ProblemResponse(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Poll photo upload rejected",
                    detail: $"Poll cannot contain more than {Poll.MaxPhotos} photos.");
            }

            await using var originalBuffer = new MemoryStream();
            try
            {
                await using var sourceStream = file.OpenReadStream();
                await sourceStream.CopyToAsync(originalBuffer, cancellationToken);
            }
            catch (IOException)
            {
                return ValidationProblemResponse(new Dictionary<string, string[]>
                {
                    ["file"] = [$"Max file size is {storageOptions.MaxUploadBytes} bytes."]
                });
            }
            if (originalBuffer.Length <= 0)
            {
                return ValidationProblemResponse(new Dictionary<string, string[]>
                {
                    ["file"] = ["File cannot be empty."]
                });
            }

            if (originalBuffer.Length > storageOptions.MaxUploadBytes)
            {
                return ValidationProblemResponse(new Dictionary<string, string[]>
                {
                    ["file"] = [$"Max file size is {storageOptions.MaxUploadBytes} bytes."]
                });
            }

            ThumbnailResult thumbnail;
            try
            {
                originalBuffer.Position = 0;
                thumbnail = await imageProcessingService.CreateThumbnailAsync(
                    originalBuffer,
                    storageOptions.ThumbnailWidth,
                    cancellationToken);
                originalBuffer.Position = 0;
            }
            catch (InvalidDataException ex)
            {
                return ValidationProblemResponse(new Dictionary<string, string[]>
                {
                    ["file"] = [ex.Message]
                });
            }

            var extension = FileExtensionForImageContentType(normalizedContentType);
            var keyRoot = $"polls/{pollId}/{Guid.NewGuid():N}";
            var originalKey = $"{keyRoot}/original.{extension}";
            var thumbnailKey = $"{keyRoot}/thumbnail.jpg";

            StoredObject? originalObject = null;
            StoredObject? thumbnailObject = null;
            try
            {
                originalObject = await objectStorageService.UploadAsync(
                    originalKey,
                    originalBuffer,
                    normalizedContentType,
                    cancellationToken);

                await using var thumbnailStream = new MemoryStream(thumbnail.Content, writable: false);
                thumbnailObject = await objectStorageService.UploadAsync(
                    thumbnailKey,
                    thumbnailStream,
                    thumbnail.ContentType,
                    cancellationToken);

                var photo = poll.AddUploadedPhoto(
                    originalObject.Key,
                    thumbnailObject.Key,
                    normalizedContentType,
                    originalBuffer.Length,
                    thumbnail.OriginalWidth,
                    thumbnail.OriginalHeight,
                    thumbnail.ThumbnailWidth,
                    thumbnail.ThumbnailHeight);

                await dbContext.SaveChangesAsync(cancellationToken);

                var photoDto = await ToPollPhotoDtoAsync(
                    photo,
                    objectStorageService,
                    storageOptions,
                    cancellationToken);

                return Results.Created(
                    $"/api/polls/{pollId}/photos/{photo.Id}",
                    photoDto);
            }
            catch (DomainValidationException ex)
            {
                if (originalObject is not null)
                {
                    await objectStorageService.DeleteIfExistsAsync(originalObject.Key, cancellationToken);
                }

                if (thumbnailObject is not null)
                {
                    await objectStorageService.DeleteIfExistsAsync(thumbnailObject.Key, cancellationToken);
                }

                return ProblemResponse(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Poll photo upload rejected",
                    detail: ex.Message);
            }
            catch
            {
                if (originalObject is not null)
                {
                    await objectStorageService.DeleteIfExistsAsync(originalObject.Key, cancellationToken);
                }

                if (thumbnailObject is not null)
                {
                    await objectStorageService.DeleteIfExistsAsync(thumbnailObject.Key, cancellationToken);
                }

                throw;
            }
        })
    .RequireAuthorization(AuthConstants.RegisteredUserPolicy)
    .DisableAntiforgery();

app.MapPost(
        "/api/polls/{pollId:long}/publish",
        async (long pollId, HttpContext httpContext, SmartChoiceDbContext dbContext,
            IObjectStorageService objectStorageService, ObjectStorageOptions storageOptions,
            CancellationToken cancellationToken) =>
        {
            var authorUserId = await GetActiveUserIdAsync(httpContext.User, dbContext, cancellationToken);
            if (!authorUserId.HasValue)
            {
                return UnauthorizedProblem("Invalid user token.");
            }

            var poll = await dbContext.Polls
                .Include(x => x.Photos)
                .SingleOrDefaultAsync(x => x.Id == pollId, cancellationToken);

            if (poll is null)
            {
                return ProblemResponse(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Poll not found",
                    detail: $"Poll with id {pollId} does not exist.");
            }

            if (poll.AuthorUserId != authorUserId.Value)
            {
                return ProblemResponse(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Forbidden",
                    detail: "Only the poll author can publish this poll.");
            }

            try
            {
                poll.Publish(DateTime.UtcNow);
                await dbContext.SaveChangesAsync(cancellationToken);
                var response = await ToPollDtoAsync(poll, objectStorageService, storageOptions, cancellationToken);
                return Results.Ok(response);
            }
            catch (DomainValidationException ex)
            {
                return ProblemResponse(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Poll publish rejected",
                    detail: ex.Message);
            }
        })
    .RequireAuthorization(AuthConstants.RegisteredUserPolicy);

app.MapPost(
        "/api/polls/{pollId:long}/close",
        async (long pollId, HttpContext httpContext, SmartChoiceDbContext dbContext,
            IObjectStorageService objectStorageService, ObjectStorageOptions storageOptions,
            CancellationToken cancellationToken) =>
        {
            var authorUserId = await GetActiveUserIdAsync(httpContext.User, dbContext, cancellationToken);
            if (!authorUserId.HasValue)
            {
                return UnauthorizedProblem("Invalid user token.");
            }

            var poll = await dbContext.Polls
                .Include(x => x.Photos)
                .SingleOrDefaultAsync(x => x.Id == pollId, cancellationToken);

            if (poll is null)
            {
                return ProblemResponse(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Poll not found",
                    detail: $"Poll with id {pollId} does not exist.");
            }

            if (poll.AuthorUserId != authorUserId.Value)
            {
                return ProblemResponse(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Forbidden",
                    detail: "Only the poll author can close this poll.");
            }

            try
            {
                poll.Close(DateTime.UtcNow);
                await dbContext.SaveChangesAsync(cancellationToken);
                var response = await ToPollDtoAsync(poll, objectStorageService, storageOptions, cancellationToken);
                return Results.Ok(response);
            }
            catch (DomainValidationException ex)
            {
                return ProblemResponse(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Poll close rejected",
                    detail: ex.Message);
            }
        })
    .RequireAuthorization(AuthConstants.RegisteredUserPolicy);

app.MapPost(
        "/api/votes",
        async (CastVoteRequest request, HttpContext httpContext, SmartChoiceDbContext dbContext,
            ILogger<Program> logger, CancellationToken cancellationToken) =>
        {
            var validationErrors = RequestValidation.Validate(request);
            if (validationErrors.Count > 0)
            {
                logger.LogWarning(
                    "Vote create validation failed. PollId={PollId}; PollPhotoId={PollPhotoId}; Actor={Actor}; ErrorCount={ErrorCount}",
                    request.PollId,
                    request.PollPhotoId,
                    GetActorLabel(httpContext.User),
                    validationErrors.Count);
                return ValidationProblemResponse(validationErrors);
            }

            var poll = await dbContext.Polls
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == request.PollId, cancellationToken);

            if (poll is null)
            {
                logger.LogWarning(
                    "Vote create rejected: poll not found. PollId={PollId}; Actor={Actor}",
                    request.PollId,
                    GetActorLabel(httpContext.User));
                return ProblemResponse(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Poll not found",
                    detail: $"Poll with id {request.PollId} does not exist.");
            }

            var pollPhotoExists = await dbContext.PollPhotos
                .AsNoTracking()
                .AnyAsync(photo => photo.Id == request.PollPhotoId && photo.PollId == request.PollId, cancellationToken);

            if (!pollPhotoExists)
            {
                logger.LogWarning(
                    "Vote create rejected: poll photo does not exist. PollId={PollId}; PollPhotoId={PollPhotoId}; Actor={Actor}",
                    request.PollId,
                    request.PollPhotoId,
                    GetActorLabel(httpContext.User));
                return ValidationProblemResponse(new Dictionary<string, string[]>
                {
                    [nameof(request.PollPhotoId)] = ["Poll photo does not exist for this poll."]
                });
            }

            var now = DateTime.UtcNow;

            try
            {
                poll.EnsureCanAcceptVote(now);
            }
            catch (DomainValidationException ex)
            {
                logger.LogWarning(
                    "Vote create rejected: poll unavailable. PollId={PollId}; PollPhotoId={PollPhotoId}; Error={Error}",
                    request.PollId,
                    request.PollPhotoId,
                    ex.Message);
                return ProblemResponse(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Poll unavailable",
                    detail: ex.Message);
            }

            var voteActorType = "unknown";
            long? voteActorId = null;
            try
            {
                Vote vote;
                if (HasActor(httpContext.User, AuthActorTypes.User))
                {
                    var voterUserId = await GetActiveUserIdAsync(httpContext.User, dbContext, cancellationToken);
                    if (!voterUserId.HasValue)
                    {
                        logger.LogWarning(
                            "Vote create rejected: invalid user token. PollId={PollId}; PollPhotoId={PollPhotoId}",
                            request.PollId,
                            request.PollPhotoId);
                        return UnauthorizedProblem("Invalid user token.");
                    }

                    voteActorType = AuthActorTypes.User;
                    voteActorId = voterUserId.Value;
                    vote = Vote.CreateByUser(request.PollId, request.PollPhotoId, voterUserId.Value);
                }
                else if (HasActor(httpContext.User, AuthActorTypes.Guest))
                {
                    voteActorType = AuthActorTypes.Guest;
                    if (!TryGetLongClaim(httpContext.User, AuthClaimTypes.GuestTokenId, out var guestTokenId)
                        || !TryGetClaim(httpContext.User, JwtRegisteredClaimNames.Jti, out var guestTokenJti))
                    {
                        logger.LogWarning(
                            "Vote create rejected: invalid guest token claims. PollId={PollId}; PollPhotoId={PollPhotoId}",
                            request.PollId,
                            request.PollPhotoId);
                        return UnauthorizedProblem("Invalid guest token.");
                    }

                    voteActorId = guestTokenId;
                    var guestToken = await dbContext.GuestTokens
                        .SingleOrDefaultAsync(token => token.Id == guestTokenId, cancellationToken);

                    if (guestToken is null || !guestToken.IsValid(now))
                    {
                        logger.LogWarning(
                            "Vote create rejected: guest token expired or revoked. PollId={PollId}; PollPhotoId={PollPhotoId}; GuestTokenId={GuestTokenId}",
                            request.PollId,
                            request.PollPhotoId,
                            guestTokenId);
                        return UnauthorizedProblem("Guest token has expired or was revoked.");
                    }

                    if (!TryGetLongClaim(httpContext.User, AuthClaimTypes.PollId, out var tokenPollId)
                        || tokenPollId != request.PollId
                        || guestToken.PollId != request.PollId)
                    {
                        logger.LogWarning(
                            "Vote create rejected: guest token is scoped to a different poll. PollId={PollId}; PollPhotoId={PollPhotoId}; GuestTokenId={GuestTokenId}; TokenPollId={TokenPollId}; StoredPollId={StoredPollId}",
                            request.PollId,
                            request.PollPhotoId,
                            guestTokenId,
                            tokenPollId,
                            guestToken.PollId);
                        return ProblemResponse(
                            statusCode: StatusCodes.Status403Forbidden,
                            title: "Forbidden",
                            detail: "Guest token is not valid for this poll.");
                    }

                    var expectedTokenHash = TokenSecurity.Sha256(guestTokenJti);
                    if (!string.Equals(guestToken.TokenHash, expectedTokenHash, StringComparison.OrdinalIgnoreCase))
                    {
                        logger.LogWarning(
                            "Vote create rejected: guest token hash mismatch. PollId={PollId}; PollPhotoId={PollPhotoId}; GuestTokenId={GuestTokenId}",
                            request.PollId,
                            request.PollPhotoId,
                            guestTokenId);
                        return UnauthorizedProblem("Invalid guest token.");
                    }

                    guestToken.MarkUsed(now);
                    vote = Vote.CreateByGuest(request.PollId, request.PollPhotoId, guestTokenId);
                }
                else
                {
                    logger.LogWarning(
                        "Vote create rejected: unsupported actor. PollId={PollId}; PollPhotoId={PollPhotoId}; Actor={Actor}",
                        request.PollId,
                        request.PollPhotoId,
                        GetActorLabel(httpContext.User));
                    return UnauthorizedProblem("Unsupported token actor.");
                }

                dbContext.Votes.Add(vote);
                await dbContext.SaveChangesAsync(cancellationToken);

                logger.LogInformation(
                    "Vote created. VoteId={VoteId}; PollId={PollId}; PollPhotoId={PollPhotoId}; ActorType={ActorType}; ActorId={ActorId}",
                    vote.Id,
                    vote.PollId,
                    vote.PollPhotoId,
                    voteActorType,
                    voteActorId);
                return Results.Created($"/api/votes/{vote.Id}", ToVoteDto(vote));
            }
            catch (DbUpdateException ex) when (IsVoteUniquenessViolation(ex))
            {
                logger.LogInformation(
                    "Duplicate vote blocked. PollId={PollId}; PollPhotoId={PollPhotoId}; ActorType={ActorType}; ActorId={ActorId}",
                    request.PollId,
                    request.PollPhotoId,
                    voteActorType,
                    voteActorId);
                return ProblemResponse(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Duplicate vote",
                    detail: "Vote already exists for this voter and poll.");
            }
            catch (DomainValidationException ex)
            {
                logger.LogWarning(
                    "Vote create rejected by domain validation. PollId={PollId}; PollPhotoId={PollPhotoId}; ActorType={ActorType}; ActorId={ActorId}; Error={Error}",
                    request.PollId,
                    request.PollPhotoId,
                    voteActorType,
                    voteActorId,
                    ex.Message);
                return ValidationProblemResponse(new Dictionary<string, string[]>
                {
                    ["domain"] = [ex.Message]
                });
            }
        })
    .RequireAuthorization()
    .RequireRateLimiting(VoteCastRateLimitPolicyName);

app.MapGet("/feed", GetLocalFeedAsync);
app.MapGet("/api/polls/feed", GetLocalFeedAsync);

app.MapGet(
    "/api/polls/{pollId:long}/results",
    async (long pollId, HttpContext httpContext, SmartChoiceDbContext dbContext,
        IObjectStorageService objectStorageService, ObjectStorageOptions storageOptions,
        CancellationToken cancellationToken) =>
    {
        var poll = await dbContext.Polls
            .AsNoTracking()
            .Where(x => x.Id == pollId)
            .Select(x => new { x.Id, x.Status, x.AuthorUserId })
            .SingleOrDefaultAsync(cancellationToken);

        if (poll is null)
        {
            return ProblemResponse(
                statusCode: StatusCodes.Status404NotFound,
                    title: "Poll not found",
                    detail: $"Poll with id {pollId} does not exist.");
        }

        var canViewPoll = await CanActorViewPollAsync(
            poll.Id,
            poll.Status,
            poll.AuthorUserId,
            httpContext.User,
            dbContext,
            cancellationToken);
        if (!canViewPoll)
        {
            return ProblemResponse(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Forbidden",
                detail: "You are not allowed to view this poll.");
        }

        var voteCountsPerPhoto = dbContext.Votes
            .AsNoTracking()
            .Where(vote => vote.PollId == pollId)
            .GroupBy(vote => vote.PollPhotoId)
            .Select(group => new
            {
                PollPhotoId = group.Key,
                VoteCount = group.Count()
            });

        var optionRows = await dbContext.PollPhotos
            .AsNoTracking()
            .Where(photo => photo.PollId == pollId)
            .GroupJoin(
                voteCountsPerPhoto,
                photo => photo.Id,
                voteCount => voteCount.PollPhotoId,
                (photo, voteCount) => new
                {
                    photo.Id,
                    photo.PhotoUrl,
                    photo.ThumbnailUrl,
                    photo.StorageKey,
                    photo.ThumbnailStorageKey,
                    photo.DisplayOrder,
                    VoteCount = voteCount.Select(x => x.VoteCount).FirstOrDefault()
                })
            .OrderBy(photo => photo.DisplayOrder)
            .ToListAsync(cancellationToken);

        var totalVotes = optionRows.Sum(row => row.VoteCount);
        var ttl = TimeSpan.FromMinutes(storageOptions.SignedUrlTtlMinutes);

        var options = new List<PollResultOptionDto>(optionRows.Count);
        foreach (var row in optionRows)
        {
            var signedUrls = await ResolvePollPhotoUrlsAsync(
                row.PhotoUrl,
                row.ThumbnailUrl,
                row.StorageKey,
                row.ThumbnailStorageKey,
                objectStorageService,
                ttl,
                cancellationToken);

            options.Add(new PollResultOptionDto(
                row.Id,
                signedUrls.DisplayUrl,
                signedUrls.ThumbUrl,
                signedUrls.DisplayUrl,
                signedUrls.ThumbUrl,
                row.DisplayOrder,
                row.VoteCount,
                CalculatePercentage(row.VoteCount, totalVotes)));
        }

        var topOption = options
            .OrderByDescending(option => option.VoteCount)
            .ThenBy(option => option.DisplayOrder)
            .FirstOrDefault();

        PollWinnerDto? winner = null;
        if (topOption is not null && topOption.VoteCount > 0)
        {
            winner = new PollWinnerDto(
                topOption.PollPhotoId,
                topOption.PhotoUrl,
                topOption.ThumbnailUrl,
                topOption.DisplayUrl,
                topOption.ThumbUrl,
                topOption.VoteCount,
                topOption.Percentage);
        }

        return Results.Ok(new PollResultsDto(poll.Id, poll.Status, totalVotes, winner, options));
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

static ObjectStorageOptions ResolveObjectStorageOptions(IConfiguration configuration)
{
    var sectionOptions = configuration.GetSection(ObjectStorageOptions.SectionName).Get<ObjectStorageOptions>()
                       ?? new ObjectStorageOptions();

    var providerRaw = FirstNonEmpty(
        configuration["ObjectStorage:Provider"],
        configuration["PHOTO_STORAGE_PROVIDER"],
        sectionOptions.Provider.ToString());
    if (!Enum.TryParse<ObjectStorageProvider>(providerRaw, ignoreCase: true, out var provider))
    {
        throw new InvalidOperationException(
            "ObjectStorage provider must be one of: S3Compatible, LocalDisk.");
    }

    var bucketName = FirstNonEmpty(configuration["S3_BUCKET"], configuration["ObjectStorage:BucketName"], sectionOptions.BucketName);
    var region = FirstNonEmpty(configuration["S3_REGION"], configuration["ObjectStorage:Region"], sectionOptions.Region);
    var accessKey = FirstNonEmpty(configuration["S3_ACCESS_KEY"], configuration["ObjectStorage:AccessKey"], sectionOptions.AccessKey);
    var secretKey = FirstNonEmpty(configuration["S3_SECRET_KEY"], configuration["ObjectStorage:SecretKey"], sectionOptions.SecretKey);
    var endpoint = FirstNonEmpty(configuration["S3_ENDPOINT"], configuration["ObjectStorage:ServiceUrl"], sectionOptions.ServiceUrl);
    var publicBaseUrl = FirstNonEmpty(configuration["ObjectStorage:PublicBaseUrl"], sectionOptions.PublicBaseUrl);
    var localDiskRootPath = FirstNonEmpty(configuration["ObjectStorage:LocalDiskRootPath"], sectionOptions.LocalDiskRootPath);
    var localDiskSigningSecret = FirstNonEmpty(configuration["ObjectStorage:LocalDiskSigningSecret"], sectionOptions.LocalDiskSigningSecret);

    var forcePathStyle = ParseOptionalBool(
        configuration["S3_FORCE_PATH_STYLE"] ?? configuration["ObjectStorage:ForcePathStyle"],
        sectionOptions.ForcePathStyle,
        "S3_FORCE_PATH_STYLE");
    var signedUrlTtlMinutes = ParseOptionalInt(
        configuration["SIGNED_URL_TTL_MINUTES"] ?? configuration["ObjectStorage:SignedUrlTtlMinutes"],
        sectionOptions.SignedUrlTtlMinutes,
        "SIGNED_URL_TTL_MINUTES");

    if (sectionOptions.ThumbnailWidth <= 0)
    {
        throw new InvalidOperationException("ObjectStorage:ThumbnailWidth must be greater than zero.");
    }

    if (sectionOptions.MaxUploadBytes <= 0)
    {
        throw new InvalidOperationException("ObjectStorage:MaxUploadBytes must be greater than zero.");
    }

    if (signedUrlTtlMinutes <= 0)
    {
        throw new InvalidOperationException("SIGNED_URL_TTL_MINUTES must be greater than zero.");
    }

    if (provider == ObjectStorageProvider.S3Compatible)
    {
        if (string.IsNullOrWhiteSpace(bucketName))
        {
            throw new InvalidOperationException("Missing S3_BUCKET or ObjectStorage:BucketName.");
        }

        if (string.IsNullOrWhiteSpace(region))
        {
            throw new InvalidOperationException("Missing S3_REGION or ObjectStorage:Region.");
        }

        var hasAccessKey = !string.IsNullOrWhiteSpace(accessKey);
        var hasSecretKey = !string.IsNullOrWhiteSpace(secretKey);
        if (hasAccessKey != hasSecretKey)
        {
            throw new InvalidOperationException(
                "S3_ACCESS_KEY and S3_SECRET_KEY (or ObjectStorage:AccessKey/SecretKey) must be set together.");
        }
    }

    return new ObjectStorageOptions
    {
        Provider = provider,
        BucketName = (bucketName ?? string.Empty).Trim(),
        Region = (region ?? string.Empty).Trim(),
        AccessKey = (accessKey ?? string.Empty).Trim(),
        SecretKey = (secretKey ?? string.Empty).Trim(),
        ServiceUrl = NormalizeUrl(endpoint),
        PublicBaseUrl = NormalizeUrl(publicBaseUrl),
        LocalDiskRootPath = string.IsNullOrWhiteSpace(localDiskRootPath)
            ? sectionOptions.LocalDiskRootPath
            : localDiskRootPath.Trim(),
        LocalDiskSigningSecret = string.IsNullOrWhiteSpace(localDiskSigningSecret)
            ? sectionOptions.LocalDiskSigningSecret
            : localDiskSigningSecret.Trim(),
        ForcePathStyle = forcePathStyle,
        EnsureBucketExistsOnStartup = sectionOptions.EnsureBucketExistsOnStartup,
        MakeBucketPublicOnStartup = sectionOptions.MakeBucketPublicOnStartup,
        ThumbnailWidth = sectionOptions.ThumbnailWidth,
        SignedUrlTtlMinutes = signedUrlTtlMinutes,
        MaxUploadBytes = sectionOptions.MaxUploadBytes
    };
}

static string? FirstNonEmpty(params string?[] values)
{
    foreach (var value in values)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }
    }

    return null;
}

static string? NormalizeUrl(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    return value.Trim().TrimEnd('/');
}

static bool ParseOptionalBool(string? value, bool fallback, string settingName)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return fallback;
    }

    if (!bool.TryParse(value, out var parsed))
    {
        throw new InvalidOperationException($"{settingName} must be a boolean value.");
    }

    return parsed;
}

static int ParseOptionalInt(string? value, int fallback, string settingName)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return fallback;
    }

    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
    {
        throw new InvalidOperationException($"{settingName} must be a valid integer.");
    }

    return parsed;
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

static async Task EnsureObjectStorageReadyAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("StartupStorage");
    var objectStorage = scope.ServiceProvider.GetRequiredService<IObjectStorageService>();
    await objectStorage.EnsureBucketReadyAsync(CancellationToken.None);
    logger.LogInformation("Object storage bucket is ready.");
}

static async Task<IResult> GetLocalFeedAsync(
    SmartChoiceDbContext dbContext,
    IObjectStorageService objectStorageService,
    ObjectStorageOptions storageOptions,
    double lat,
    double lng,
    int radius,
    int page = 1,
    CancellationToken cancellationToken = default)
{
    var validationErrors = ValidateFeedRequest(lat, lng, radius, page);
    if (validationErrors.Count > 0)
    {
        return ValidationProblemResponse(validationErrors);
    }

    var offset = ((long)page - 1L) * FeedPageSize;
    if (offset > int.MaxValue)
    {
        return ValidationProblemResponse(new Dictionary<string, string[]>
        {
            [nameof(page)] = ["Page is too large."]
        });
    }

    var rows = await QueryFeedDistanceRowsAsync(
        dbContext,
        lat,
        lng,
        radius,
        FeedPageSize + 1,
        offset,
        cancellationToken);

    var hasNextPage = rows.Count > FeedPageSize;
    var pageRows = hasNextPage
        ? rows.Take(FeedPageSize).ToArray()
        : rows.ToArray();

    if (pageRows.Length == 0)
    {
        return Results.Ok(new FeedPageDto(page, FeedPageSize, false, "newest", []));
    }

    var pollIds = pageRows.Select(row => row.PollId).ToArray();

    var polls = await dbContext.Polls
        .AsNoTracking()
        .Where(poll => pollIds.Contains(poll.Id))
        .Select(poll => new
        {
            poll.Id,
            poll.Question,
            poll.CreatedAt,
            poll.EndsAt,
            poll.Latitude,
            poll.Longitude,
            poll.RadiusMeters,
            Photos = poll.Photos
                .OrderBy(photo => photo.DisplayOrder)
                .Select(photo => new
                {
                    photo.Id,
                    photo.PhotoUrl,
                    photo.ThumbnailUrl,
                    photo.StorageKey,
                    photo.ThumbnailStorageKey,
                    photo.DisplayOrder
                })
        })
        .ToListAsync(cancellationToken);

    var orderByPollId = pageRows
        .Select((row, index) => new { row.PollId, index })
        .ToDictionary(x => x.PollId, x => x.index);

    var distanceByPollId = pageRows.ToDictionary(row => row.PollId, row => row.DistanceMeters);

    var ttl = TimeSpan.FromMinutes(storageOptions.SignedUrlTtlMinutes);
    var items = new List<FeedPollDto>(polls.Count);
    foreach (var poll in polls.OrderBy(poll => orderByPollId[poll.Id]))
    {
        var mappedPhotos = await MapPollPhotoDtosAsync(
            poll.Photos
                .Select(photo => new PollPhotoProjection(
                    photo.Id,
                    photo.PhotoUrl,
                    photo.ThumbnailUrl,
                    photo.StorageKey,
                    photo.ThumbnailStorageKey,
                    photo.DisplayOrder))
                .ToArray(),
            objectStorageService,
            ttl,
            cancellationToken);

        items.Add(new FeedPollDto(
            poll.Id,
            poll.Question,
            poll.CreatedAt,
            poll.EndsAt,
            poll.Latitude,
            poll.Longitude,
            poll.RadiusMeters,
            Math.Round(distanceByPollId[poll.Id], 2, MidpointRounding.AwayFromZero),
            mappedPhotos));
    }

    return Results.Ok(new FeedPageDto(page, FeedPageSize, hasNextPage, "newest", items));
}

static Dictionary<string, string[]> ValidateFeedRequest(double lat, double lng, int radius, int page)
{
    var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

    static void AddError(Dictionary<string, List<string>> errorMap, string key, string message)
    {
        if (!errorMap.TryGetValue(key, out var messages))
        {
            messages = [];
            errorMap[key] = messages;
        }

        messages.Add(message);
    }

    if (double.IsNaN(lat) || double.IsInfinity(lat) || lat is < -90 or > 90)
    {
        AddError(errors, nameof(lat), "Latitude must be in range [-90, 90].");
    }

    if (double.IsNaN(lng) || double.IsInfinity(lng) || lng is < -180 or > 180)
    {
        AddError(errors, nameof(lng), "Longitude must be in range [-180, 180].");
    }

    if (radius is < Poll.MinRadiusMeters or > Poll.MaxRadiusMeters)
    {
        AddError(
            errors,
            nameof(radius),
            $"Radius must be in range [{Poll.MinRadiusMeters}, {Poll.MaxRadiusMeters}] meters.");
    }

    if (page < 1)
    {
        AddError(errors, nameof(page), "Page must be greater than zero.");
    }

    return errors.ToDictionary(
        entry => entry.Key,
        entry => entry.Value.Distinct(StringComparer.Ordinal).ToArray(),
        StringComparer.Ordinal);
}

static async Task<List<FeedDistanceSqlRow>> QueryFeedDistanceRowsAsync(
    SmartChoiceDbContext dbContext,
    double lat,
    double lng,
    int radius,
    int limit,
    long offset,
    CancellationToken cancellationToken)
{
    var connection = dbContext.Database.GetDbConnection();
    var shouldCloseConnection = connection.State != ConnectionState.Open;
    if (shouldCloseConnection)
    {
        await connection.OpenAsync(cancellationToken);
    }

    try
    {
        await using var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandText = """
                              SELECT ranked.id AS poll_id, ranked.distance_meters
                              FROM (
                                  SELECT
                                      p.id,
                                      p.created_at,
                                      p.radius_meters,
                                      ST_Distance_Sphere(
                                          POINT(p.longitude, p.latitude),
                                          POINT(@lng, @lat)
                                      ) AS distance_meters
                                  FROM polls p
                                  WHERE p.status = @status
                              ) AS ranked
                              WHERE ranked.distance_meters <= LEAST(ranked.radius_meters, @radius)
                              ORDER BY ranked.created_at DESC, ranked.id DESC
                              LIMIT @limit OFFSET @offset;
                              """;

        AddDbParameter(command, "@lng", DbType.Double, lng);
        AddDbParameter(command, "@lat", DbType.Double, lat);
        AddDbParameter(command, "@status", DbType.Byte, (byte)PollStatus.Open);
        AddDbParameter(command, "@radius", DbType.Int32, radius);
        AddDbParameter(command, "@limit", DbType.Int32, limit);
        AddDbParameter(command, "@offset", DbType.Int64, offset);

        var rows = new List<FeedDistanceSqlRow>(limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var pollIdOrdinal = reader.GetOrdinal("poll_id");
        var distanceOrdinal = reader.GetOrdinal("distance_meters");

        while (await reader.ReadAsync(cancellationToken))
        {
            var pollId = reader.GetInt64(pollIdOrdinal);
            var distance = Convert.ToDouble(reader.GetValue(distanceOrdinal), CultureInfo.InvariantCulture);
            rows.Add(new FeedDistanceSqlRow(pollId, distance));
        }

        return rows;
    }
    finally
    {
        if (shouldCloseConnection)
        {
            await connection.CloseAsync();
        }
    }
}

static void AddDbParameter(DbCommand command, string name, DbType type, object value)
{
    var parameter = command.CreateParameter();
    parameter.ParameterName = name;
    parameter.DbType = type;
    parameter.Value = value;
    command.Parameters.Add(parameter);
}

static bool IsVoteUniquenessViolation(DbUpdateException exception)
{
    var message = exception.ToString();
    return message.Contains("ux_votes_poll_user", StringComparison.OrdinalIgnoreCase)
           || message.Contains("ux_votes_poll_guest_token", StringComparison.OrdinalIgnoreCase)
           || message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase)
           || message.Contains("duplicate", StringComparison.OrdinalIgnoreCase);
}

static async Task<PollDto> ToPollDtoAsync(
    Poll poll,
    IObjectStorageService objectStorageService,
    ObjectStorageOptions storageOptions,
    CancellationToken cancellationToken)
{
    var photoProjections = poll.Photos
        .OrderBy(photo => photo.DisplayOrder)
        .Select(photo => new PollPhotoProjection(
            photo.Id,
            photo.PhotoUrl,
            photo.ThumbnailUrl,
            photo.StorageKey,
            photo.ThumbnailStorageKey,
            photo.DisplayOrder))
        .ToArray();

    var photos = await MapPollPhotoDtosAsync(
        photoProjections,
        objectStorageService,
        TimeSpan.FromMinutes(storageOptions.SignedUrlTtlMinutes),
        cancellationToken);

    return new PollDto(
        poll.Id,
        poll.AuthorUserId,
        poll.Question,
        poll.Status,
        poll.Latitude,
        poll.Longitude,
        poll.RadiusMeters,
        poll.StartsAt,
        poll.EndsAt,
        poll.CreatedAt,
        poll.UpdatedAt,
        photos);
}

static async Task<PollPhotoDto> ToPollPhotoDtoAsync(
    PollPhoto photo,
    IObjectStorageService objectStorageService,
    ObjectStorageOptions storageOptions,
    CancellationToken cancellationToken)
{
    var mapped = await MapPollPhotoDtosAsync(
        [
            new PollPhotoProjection(
                photo.Id,
                photo.PhotoUrl,
                photo.ThumbnailUrl,
                photo.StorageKey,
                photo.ThumbnailStorageKey,
                photo.DisplayOrder)
        ],
        objectStorageService,
        TimeSpan.FromMinutes(storageOptions.SignedUrlTtlMinutes),
        cancellationToken);

    return mapped[0];
}

static async Task<PollPhotoDto[]> MapPollPhotoDtosAsync(
    IReadOnlyCollection<PollPhotoProjection> photos,
    IObjectStorageService objectStorageService,
    TimeSpan signedUrlTtl,
    CancellationToken cancellationToken)
{
    var results = new List<PollPhotoDto>(photos.Count);

    foreach (var photo in photos.OrderBy(photo => photo.DisplayOrder))
    {
        var signedUrls = await ResolvePollPhotoUrlsAsync(
            photo.PhotoUrl,
            photo.ThumbnailUrl,
            photo.StorageKey,
            photo.ThumbnailStorageKey,
            objectStorageService,
            signedUrlTtl,
            cancellationToken);

        results.Add(new PollPhotoDto(
            photo.Id,
            signedUrls.DisplayUrl,
            signedUrls.ThumbUrl,
            signedUrls.DisplayUrl,
            signedUrls.ThumbUrl,
            photo.DisplayOrder));
    }

    return results.ToArray();
}

static async Task<PollPhotoResolvedUrls> ResolvePollPhotoUrlsAsync(
    string photoUrl,
    string? thumbnailUrl,
    string? storageKey,
    string? thumbnailStorageKey,
    IObjectStorageService objectStorageService,
    TimeSpan signedUrlTtl,
    CancellationToken cancellationToken)
{
    var displayUrl = photoUrl;
    if (!string.IsNullOrWhiteSpace(storageKey))
    {
        var signed = await objectStorageService.GetReadUrlAsync(storageKey, signedUrlTtl, cancellationToken);
        displayUrl = signed.Url;
    }

    var thumbUrl = thumbnailUrl;
    if (!string.IsNullOrWhiteSpace(thumbnailStorageKey))
    {
        var signedThumb = await objectStorageService.GetReadUrlAsync(thumbnailStorageKey, signedUrlTtl, cancellationToken);
        thumbUrl = signedThumb.Url;
    }

    return new PollPhotoResolvedUrls(displayUrl, thumbUrl);
}

static async Task<bool> CanActorViewPollAsync(
    long pollId,
    PollStatus pollStatus,
    long pollAuthorUserId,
    ClaimsPrincipal principal,
    SmartChoiceDbContext dbContext,
    CancellationToken cancellationToken)
{
    var activeUserId = await GetActiveUserIdAsync(principal, dbContext, cancellationToken);
    if (activeUserId.HasValue && activeUserId.Value == pollAuthorUserId)
    {
        return true;
    }

    if (pollStatus == PollStatus.Draft)
    {
        return false;
    }

    if (HasActor(principal, AuthActorTypes.User))
    {
        return activeUserId.HasValue;
    }

    if (HasActor(principal, AuthActorTypes.Guest))
    {
        return await IsActiveGuestTokenForPollAsync(principal, pollId, dbContext, cancellationToken);
    }

    return principal.Identity?.IsAuthenticated != true;
}

static async Task<bool> IsActiveGuestTokenForPollAsync(
    ClaimsPrincipal principal,
    long pollId,
    SmartChoiceDbContext dbContext,
    CancellationToken cancellationToken)
{
    if (!TryGetLongClaim(principal, AuthClaimTypes.GuestTokenId, out var guestTokenId)
        || !TryGetLongClaim(principal, AuthClaimTypes.PollId, out var tokenPollId)
        || !TryGetClaim(principal, JwtRegisteredClaimNames.Jti, out var guestTokenJti))
    {
        return false;
    }

    if (tokenPollId != pollId)
    {
        return false;
    }

    var guestToken = await dbContext.GuestTokens
        .AsNoTracking()
        .SingleOrDefaultAsync(token => token.Id == guestTokenId, cancellationToken);
    if (guestToken is null || guestToken.PollId != pollId || !guestToken.IsValid(DateTime.UtcNow))
    {
        return false;
    }

    var expectedTokenHash = TokenSecurity.Sha256(guestTokenJti);
    return string.Equals(guestToken.TokenHash, expectedTokenHash, StringComparison.OrdinalIgnoreCase);
}

static VoteDto ToVoteDto(Vote vote)
{
    return new VoteDto(
        vote.Id,
        vote.PollId,
        vote.PollPhotoId,
        vote.VoterUserId,
        vote.GuestTokenId,
        vote.VotedAt);
}

static decimal CalculatePercentage(int optionVotes, int totalVotes)
{
    if (totalVotes <= 0)
    {
        return 0m;
    }

    return Math.Round((decimal)optionVotes * 100m / totalVotes, 2, MidpointRounding.AwayFromZero);
}

static async Task<long?> GetActiveUserIdAsync(
    ClaimsPrincipal principal,
    SmartChoiceDbContext dbContext,
    CancellationToken cancellationToken)
{
    if (!TryGetLongClaim(principal, ClaimTypes.NameIdentifier, out var userId))
    {
        return null;
    }

    var isActive = await dbContext.Users
        .AsNoTracking()
        .AnyAsync(user => user.Id == userId && user.IsActive, cancellationToken);

    return isActive ? userId : null;
}

static IResult ValidationProblemResponse(IDictionary<string, string[]> errors, string? detail = null)
{
    return Results.ValidationProblem(
        errors,
        statusCode: StatusCodes.Status400BadRequest,
        title: "Validation failed",
        detail: detail,
        type: ProblemTypeForStatus(StatusCodes.Status400BadRequest));
}

static IResult ProblemResponse(
    int statusCode,
    string title,
    string detail,
    IDictionary<string, object?>? extensions = null)
{
    return Results.Problem(
        statusCode: statusCode,
        title: title,
        detail: detail,
        type: ProblemTypeForStatus(statusCode),
        extensions: extensions);
}

static string ResolveRateLimitPartitionKey(HttpContext httpContext, string operation)
{
    if (TryGetLongClaim(httpContext.User, ClaimTypes.NameIdentifier, out var userId))
    {
        return $"{operation}:user:{userId}";
    }

    if (TryGetLongClaim(httpContext.User, AuthClaimTypes.GuestTokenId, out var guestTokenId))
    {
        return $"{operation}:guest:{guestTokenId}";
    }

    var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString();
    if (!string.IsNullOrWhiteSpace(remoteIp))
    {
        return $"{operation}:ip:{remoteIp}";
    }

    return $"{operation}:anonymous";
}

static string MaskInviteCode(string? inviteCode)
{
    if (string.IsNullOrWhiteSpace(inviteCode))
    {
        return "***";
    }

    var trimmed = inviteCode.Trim().ToUpperInvariant();
    if (trimmed.Length <= 2)
    {
        return $"{trimmed[0]}*";
    }

    return $"{trimmed[..2]}***";
}

static IResult UnauthorizedProblem(string detail)
{
    return ProblemResponse(
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

static string? NormalizeImageContentType(string? contentType)
{
    if (string.IsNullOrWhiteSpace(contentType))
    {
        return null;
    }

    var normalized = contentType.Trim().ToLowerInvariant();
    return normalized switch
    {
        "image/jpg" => "image/jpeg",
        "image/jpeg" => "image/jpeg",
        "image/png" => "image/png",
        "image/webp" => "image/webp",
        _ => null
    };
}

static string FileExtensionForImageContentType(string contentType)
{
    return contentType switch
    {
        "image/jpeg" => "jpg",
        "image/png" => "png",
        "image/webp" => "webp",
        _ => "bin"
    };
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

sealed record PollPhotoProjection(
    long Id,
    string PhotoUrl,
    string? ThumbnailUrl,
    string? StorageKey,
    string? ThumbnailStorageKey,
    byte DisplayOrder);

sealed record PollPhotoResolvedUrls(
    string DisplayUrl,
    string? ThumbUrl);

sealed record FeedDistanceSqlRow(long PollId, double DistanceMeters);
