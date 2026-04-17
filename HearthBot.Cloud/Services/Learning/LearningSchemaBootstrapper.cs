using HearthBot.Cloud.Data;
using Microsoft.EntityFrameworkCore;

namespace HearthBot.Cloud.Services.Learning;

public static class LearningSchemaBootstrapper
{
    public static async Task EnsureSchemaAsync(LearningDbContext db, CancellationToken ct = default)
    {
        await db.Database.EnsureCreatedAsync(ct);

        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL;", ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;", ct);
    }
}
