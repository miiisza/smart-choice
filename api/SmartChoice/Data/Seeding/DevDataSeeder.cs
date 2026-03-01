using Microsoft.EntityFrameworkCore;
using SmartChoice.Domain.Entities;
using SmartChoice.Domain.Enums;

namespace SmartChoice.Data.Seeding;

public sealed class DevDataSeeder(SmartChoiceDbContext dbContext, ILogger<DevDataSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await dbContext.Users.AnyAsync(cancellationToken))
        {
            logger.LogInformation("Skipping dev seed because users already exist.");
            return;
        }

        var now = DateTime.UtcNow;

        var userA = new User("alice", "alice@smartchoice.local", "dev_hash_alice");
        var userB = new User("bob", "bob@smartchoice.local", "dev_hash_bob");

        dbContext.Users.AddRange(userA, userB);
        await dbContext.SaveChangesAsync(cancellationToken);

        var invite = new Invite("DEV2026", now.AddDays(30), maxUses: 50, createdByUserId: userA.Id);
        dbContext.Invites.Add(invite);
        await dbContext.SaveChangesAsync(cancellationToken);

        var guestToken = new GuestToken("dev_guest_token_hash_001", invite.Id, now.AddDays(7));
        dbContext.GuestTokens.Add(guestToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        var poll = Poll.Create(
            userA.Id,
            "Które zdjęcie powinno wygrać?",
            [
                "https://picsum.photos/id/10/800/600",
                "https://picsum.photos/id/20/800/600",
                "https://picsum.photos/id/30/800/600"
            ],
            now.AddHours(-2),
            now.AddDays(2));

        dbContext.Polls.Add(poll);
        await dbContext.SaveChangesAsync(cancellationToken);

        var photos = poll.Photos.OrderBy(x => x.DisplayOrder).ToArray();

        dbContext.Votes.AddRange(
            Vote.CreateByUser(poll.Id, photos[0].Id, userA.Id),
            Vote.CreateByUser(poll.Id, photos[1].Id, userB.Id),
            Vote.CreateByGuest(poll.Id, photos[2].Id, guestToken.Id));

        var report = Report.CreateByUser(poll.Id, userB.Id, "SPAM", "Przykładowe zgłoszenie developerskie.");
        report.SetStatus(ReportStatus.Reviewed);
        dbContext.Reports.Add(report);

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Seeded development data successfully.");
    }
}
