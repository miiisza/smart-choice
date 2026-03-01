using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Data;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
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
const int FeedPageSize = 20;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddScoped<DevDataSeeder>();
builder.Services.AddSingleton<PasswordHashingService>();

var authOptions = ResolveAuthOptions(builder.Configuration);
builder.Services.AddSingleton(authOptions);
builder.Services.AddSingleton<JwtTokenService>();

var objectStorageOptions = ResolveObjectStorageOptions(builder.Configuration);
builder.Services.AddSingleton(objectStorageOptions);
builder.Services.AddSingleton<ImageProcessingService>();
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

await EnsureObjectStorageReadyAsync(app);

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

            var authorUserId = await GetActiveUserIdAsync(httpContext.User, dbContext, cancellationToken);
            if (!authorUserId.HasValue)
            {
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

                return Results.Created($"/api/polls/{poll.Id}", ToPollDto(poll));
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
        "/api/polls/{pollId:long}/photos",
        async (long pollId, [FromForm] IFormFile? file, HttpContext httpContext, SmartChoiceDbContext dbContext,
            IObjectStorageService objectStorageService, ImageProcessingService imageProcessingService,
            ObjectStorageOptions storageOptions, CancellationToken cancellationToken) =>
        {
            if (file is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["file"] = ["File is required."]
                });
            }

            if (file.Length <= 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["file"] = ["File cannot be empty."]
                });
            }

            if (file.Length > storageOptions.MaxUploadBytes)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["file"] = [$"Max file size is {storageOptions.MaxUploadBytes} bytes."]
                });
            }

            var normalizedContentType = NormalizeImageContentType(file.ContentType);
            if (normalizedContentType is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
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
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Poll not found",
                    detail: $"Poll with id {pollId} does not exist.");
            }

            if (poll.AuthorUserId != authorUserId.Value)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Forbidden",
                    detail: "Only the poll author can upload photos for this poll.");
            }

            if (poll.Status != PollStatus.Draft)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Poll photo upload rejected",
                    detail: "Photos can only be uploaded for draft polls.");
            }

            if (poll.Photos.Count >= Poll.MaxPhotos)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Poll photo upload rejected",
                    detail: $"Poll cannot contain more than {Poll.MaxPhotos} photos.");
            }

            await using var originalBuffer = new MemoryStream();
            try
            {
                await using var sourceStream = file.OpenReadStream(storageOptions.MaxUploadBytes);
                await sourceStream.CopyToAsync(originalBuffer, cancellationToken);
            }
            catch (IOException)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["file"] = [$"Max file size is {storageOptions.MaxUploadBytes} bytes."]
                });
            }
            if (originalBuffer.Length <= 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["file"] = ["File cannot be empty."]
                });
            }

            if (originalBuffer.Length > storageOptions.MaxUploadBytes)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
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
                return Results.ValidationProblem(new Dictionary<string, string[]>
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
                    originalObject.Url,
                    thumbnailObject.Url,
                    originalObject.Key,
                    thumbnailObject.Key,
                    normalizedContentType,
                    originalBuffer.Length,
                    thumbnail.OriginalWidth,
                    thumbnail.OriginalHeight,
                    thumbnail.ThumbnailWidth,
                    thumbnail.ThumbnailHeight);

                await dbContext.SaveChangesAsync(cancellationToken);

                return Results.Created(
                    $"/api/polls/{pollId}/photos/{photo.Id}",
                    new PollPhotoDto(photo.Id, photo.PhotoUrl, photo.ThumbnailUrl, photo.DisplayOrder));
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

                return Results.Problem(
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
    .RequireAuthorization(AuthConstants.RegisteredUserPolicy);

app.MapPost(
        "/api/polls/{pollId:long}/publish",
        async (long pollId, HttpContext httpContext, SmartChoiceDbContext dbContext, CancellationToken cancellationToken) =>
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
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Poll not found",
                    detail: $"Poll with id {pollId} does not exist.");
            }

            if (poll.AuthorUserId != authorUserId.Value)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Forbidden",
                    detail: "Only the poll author can publish this poll.");
            }

            try
            {
                poll.Publish(DateTime.UtcNow);
                await dbContext.SaveChangesAsync(cancellationToken);
                return Results.Ok(ToPollDto(poll));
            }
            catch (DomainValidationException ex)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Poll publish rejected",
                    detail: ex.Message);
            }
        })
    .RequireAuthorization(AuthConstants.RegisteredUserPolicy);

app.MapPost(
        "/api/polls/{pollId:long}/close",
        async (long pollId, HttpContext httpContext, SmartChoiceDbContext dbContext, CancellationToken cancellationToken) =>
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
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Poll not found",
                    detail: $"Poll with id {pollId} does not exist.");
            }

            if (poll.AuthorUserId != authorUserId.Value)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Forbidden",
                    detail: "Only the poll author can close this poll.");
            }

            try
            {
                poll.Close(DateTime.UtcNow);
                await dbContext.SaveChangesAsync(cancellationToken);
                return Results.Ok(ToPollDto(poll));
            }
            catch (DomainValidationException ex)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Poll close rejected",
                    detail: ex.Message);
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

            var poll = await dbContext.Polls
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == request.PollId, cancellationToken);

            if (poll is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Poll not found",
                    detail: $"Poll with id {request.PollId} does not exist.");
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
                poll.EnsureCanAcceptVote(now);
            }
            catch (DomainValidationException ex)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Poll unavailable",
                    detail: ex.Message);
            }

            try
            {
                Vote vote;
                if (HasActor(httpContext.User, AuthActorTypes.User))
                {
                    var voterUserId = await GetActiveUserIdAsync(httpContext.User, dbContext, cancellationToken);
                    if (!voterUserId.HasValue)
                    {
                        return UnauthorizedProblem("Invalid user token.");
                    }

                    vote = Vote.CreateByUser(request.PollId, request.PollPhotoId, voterUserId.Value);
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

                return Results.Created($"/api/votes/{vote.Id}", ToVoteDto(vote));
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

app.MapGet("/feed", GetLocalFeedAsync);
app.MapGet("/api/polls/feed", GetLocalFeedAsync);

app.MapGet(
    "/api/polls/{pollId:long}/results",
    async (long pollId, SmartChoiceDbContext dbContext, CancellationToken cancellationToken) =>
    {
        var poll = await dbContext.Polls
            .AsNoTracking()
            .Where(x => x.Id == pollId)
            .Select(x => new { x.Id, x.Status })
            .SingleOrDefaultAsync(cancellationToken);

        if (poll is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Poll not found",
                detail: $"Poll with id {pollId} does not exist.");
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
                    photo.DisplayOrder,
                    VoteCount = voteCount.Select(x => x.VoteCount).FirstOrDefault()
                })
            .OrderBy(photo => photo.DisplayOrder)
            .ToListAsync(cancellationToken);

        var totalVotes = optionRows.Sum(row => row.VoteCount);

        var options = optionRows
            .Select(row => new PollResultOptionDto(
                row.Id,
                row.PhotoUrl,
                row.DisplayOrder,
                row.VoteCount,
                CalculatePercentage(row.VoteCount, totalVotes)))
            .ToArray();

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
    var options = configuration.GetSection(ObjectStorageOptions.SectionName).Get<ObjectStorageOptions>()
                  ?? new ObjectStorageOptions();

    if (string.IsNullOrWhiteSpace(options.BucketName))
    {
        throw new InvalidOperationException("Missing ObjectStorage:BucketName.");
    }

    if (string.IsNullOrWhiteSpace(options.Region))
    {
        throw new InvalidOperationException("Missing ObjectStorage:Region.");
    }

    var hasAccessKey = !string.IsNullOrWhiteSpace(options.AccessKey);
    var hasSecretKey = !string.IsNullOrWhiteSpace(options.SecretKey);
    if (hasAccessKey != hasSecretKey)
    {
        throw new InvalidOperationException(
            "ObjectStorage:AccessKey and ObjectStorage:SecretKey must be set together or both left empty.");
    }

    if (options.ThumbnailWidth <= 0)
    {
        throw new InvalidOperationException("ObjectStorage:ThumbnailWidth must be greater than zero.");
    }

    if (options.MaxUploadBytes <= 0)
    {
        throw new InvalidOperationException("ObjectStorage:MaxUploadBytes must be greater than zero.");
    }

    return new ObjectStorageOptions
    {
        BucketName = options.BucketName.Trim(),
        Region = options.Region.Trim(),
        AccessKey = (options.AccessKey ?? string.Empty).Trim(),
        SecretKey = (options.SecretKey ?? string.Empty).Trim(),
        ServiceUrl = string.IsNullOrWhiteSpace(options.ServiceUrl) ? null : options.ServiceUrl.Trim().TrimEnd('/'),
        PublicBaseUrl = string.IsNullOrWhiteSpace(options.PublicBaseUrl) ? null : options.PublicBaseUrl.Trim().TrimEnd('/'),
        ForcePathStyle = options.ForcePathStyle,
        EnsureBucketExistsOnStartup = options.EnsureBucketExistsOnStartup,
        MakeBucketPublicOnStartup = options.MakeBucketPublicOnStartup,
        ThumbnailWidth = options.ThumbnailWidth,
        MaxUploadBytes = options.MaxUploadBytes
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
    double lat,
    double lng,
    int radius,
    int page = 1,
    CancellationToken cancellationToken = default)
{
    var validationErrors = ValidateFeedRequest(lat, lng, radius, page);
    if (validationErrors.Count > 0)
    {
        return Results.ValidationProblem(validationErrors);
    }

    var offset = ((long)page - 1L) * FeedPageSize;
    if (offset > int.MaxValue)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
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
                .Select(photo => new PollPhotoDto(photo.Id, photo.PhotoUrl, photo.ThumbnailUrl, photo.DisplayOrder))
        })
        .ToListAsync(cancellationToken);

    var orderByPollId = pageRows
        .Select((row, index) => new { row.PollId, index })
        .ToDictionary(x => x.PollId, x => x.index);

    var distanceByPollId = pageRows.ToDictionary(row => row.PollId, row => row.DistanceMeters);

    var items = polls
        .OrderBy(poll => orderByPollId[poll.Id])
        .Select(poll => new FeedPollDto(
            poll.Id,
            poll.Question,
            poll.CreatedAt,
            poll.EndsAt,
            poll.Latitude,
            poll.Longitude,
            poll.RadiusMeters,
            Math.Round(distanceByPollId[poll.Id], 2, MidpointRounding.AwayFromZero),
            poll.Photos.ToArray()))
        .ToArray();

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
                                          p.location,
                                          ST_SRID(POINT(@lng, @lat), 4326)
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

static PollDto ToPollDto(Poll poll)
{
    var photos = poll.Photos
        .OrderBy(photo => photo.DisplayOrder)
        .Select(photo => new PollPhotoDto(photo.Id, photo.PhotoUrl, photo.ThumbnailUrl, photo.DisplayOrder))
        .ToArray();

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

sealed record FeedDistanceSqlRow(long PollId, double DistanceMeters);
