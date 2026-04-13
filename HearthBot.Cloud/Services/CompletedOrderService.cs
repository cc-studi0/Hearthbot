#nullable enable
using HearthBot.Cloud.Data;
using HearthBot.Cloud.Models;
using Microsoft.EntityFrameworkCore;

namespace HearthBot.Cloud.Services;

public class CompletedOrderService
{
    private readonly CloudDbContext _db;

    public CompletedOrderService(CloudDbContext db)
    {
        _db = db;
    }

    public Task<List<CompletedOrderSnapshot>> GetVisibleAsync(DateTime now) =>
        _db.CompletedOrderSnapshots
            .Where(snapshot => snapshot.DeletedAt == null && snapshot.ExpiresAt > now)
            .OrderByDescending(snapshot => snapshot.CompletedAt)
            .ToListAsync();

    public async Task<CompletedOrderSnapshot?> HideAsync(int id, DateTime now)
    {
        var snapshot = await _db.CompletedOrderSnapshots.FindAsync(id);
        if (snapshot == null) return null;

        snapshot.DeletedAt ??= now;
        await _db.SaveChangesAsync();
        return snapshot;
    }
}
