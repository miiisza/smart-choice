using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
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
builder.Services.AddScoped<DevDataSeeder>();

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

app.UseHttpsRedirection();
app.UseCors(CorsPolicyName);

app.MapGet("/", () => Results.Ok(new { service = "smart-choice-api", status = "running" }));
app.MapHealthChecks("/health/live", CreateHealthOptions("live"));
app.MapHealthChecks("/health/ready", CreateHealthOptions("ready"));
app.MapGet("/health", () => Results.Redirect("/health/ready"));

app.MapPost(
    "/api/polls",
    async (CreatePollRequest request, SmartChoiceDbContext dbContext, CancellationToken cancellationToken) =>
    {
        var validationErrors = RequestValidation.Validate(request);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        var authorExists = await dbContext.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == request.AuthorUserId, cancellationToken);
        if (!authorExists)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.AuthorUserId)] = ["Author user does not exist."]
            });
        }

        try
        {
            var poll = Poll.Create(
                request.AuthorUserId,
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
    });

app.MapPost(
    "/api/votes",
    async (CastVoteRequest request, SmartChoiceDbContext dbContext, CancellationToken cancellationToken) =>
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

        if (request.VoterUserId.HasValue)
        {
            var userExists = await dbContext.Users
                .AsNoTracking()
                .AnyAsync(user => user.Id == request.VoterUserId.Value, cancellationToken);
            if (!userExists)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request.VoterUserId)] = ["Voter user does not exist."]
                });
            }
        }

        if (request.GuestTokenId.HasValue)
        {
            var guestExists = await dbContext.GuestTokens
                .AsNoTracking()
                .AnyAsync(token => token.Id == request.GuestTokenId.Value && !token.IsRevoked, cancellationToken);
            if (!guestExists)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request.GuestTokenId)] = ["Guest token does not exist or is revoked."]
                });
            }
        }

        try
        {
            Vote vote = request.VoterUserId.HasValue
                ? Vote.CreateByUser(request.PollId, request.PollPhotoId, request.VoterUserId.Value)
                : Vote.CreateByGuest(request.PollId, request.PollPhotoId, request.GuestTokenId!.Value);

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
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["vote"] = ["Vote already exists for this voter and poll."]
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
            return Results.NotFound();
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
