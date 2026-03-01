using Microsoft.EntityFrameworkCore;
using MySql.EntityFrameworkCore.Extensions;
using SmartChoice.Domain.Entities;

namespace SmartChoice.Data;

public sealed class SmartChoiceDbContext(DbContextOptions<SmartChoiceDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Poll> Polls => Set<Poll>();
    public DbSet<PollPhoto> PollPhotos => Set<PollPhoto>();
    public DbSet<Vote> Votes => Set<Vote>();
    public DbSet<Invite> Invites => Set<Invite>();
    public DbSet<GuestToken> GuestTokens => Set<GuestToken>();
    public DbSet<Report> Reports => Set<Report>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        MySQLModelBuilderExtensions.HasCharSet(modelBuilder, "utf8mb4");
        RelationalModelBuilderExtensions.UseCollation(modelBuilder, "utf8mb4_0900_ai_ci");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SmartChoiceDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
