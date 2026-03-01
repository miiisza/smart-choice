using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

const string CorsPolicyName = "SmartChoiceDevCors";

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live" })
    .AddCheck(
        "mysql-connection-string",
        () =>
        {
            var connectionString = builder.Configuration.GetConnectionString("Default");
            return string.IsNullOrWhiteSpace(connectionString)
                ? HealthCheckResult.Unhealthy("Missing ConnectionStrings:Default")
                : HealthCheckResult.Healthy();
        },
        tags: new[] { "ready" });

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

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors(CorsPolicyName);

app.MapGet("/", () => Results.Ok(new { service = "smart-choice-api", status = "running" }));
app.MapHealthChecks("/health/live", CreateHealthOptions("live"));
app.MapHealthChecks("/health/ready", CreateHealthOptions("ready"));
app.MapGet("/health", () => Results.Redirect("/health/ready"));

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
