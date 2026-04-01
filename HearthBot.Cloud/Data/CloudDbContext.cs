using HearthBot.Cloud.Models;
using Microsoft.EntityFrameworkCore;

namespace HearthBot.Cloud.Data;

public class CloudDbContext : DbContext
{
    public CloudDbContext(DbContextOptions<CloudDbContext> options) : base(options) { }

    public DbSet<Device> Devices => Set<Device>();
    public DbSet<GameRecord> GameRecords => Set<GameRecord>();
    public DbSet<PendingCommand> PendingCommands => Set<PendingCommand>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Device>(e =>
        {
            e.HasKey(d => d.DeviceId);
            e.Property(d => d.DeviceId).HasMaxLength(128);
        });

        b.Entity<GameRecord>(e =>
        {
            e.HasKey(g => g.Id);
            e.Property(g => g.Id).ValueGeneratedOnAdd();
            e.HasIndex(g => g.DeviceId);
            e.HasIndex(g => g.PlayedAt);
        });

        b.Entity<PendingCommand>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).ValueGeneratedOnAdd();
            e.HasIndex(c => new { c.DeviceId, c.Status });
        });
    }
}
