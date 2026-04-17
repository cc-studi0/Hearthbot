using HearthBot.Cloud.Models.Learning;
using Microsoft.EntityFrameworkCore;

namespace HearthBot.Cloud.Data;

public class LearningDbContext : DbContext
{
    public LearningDbContext(DbContextOptions<LearningDbContext> options) : base(options) { }

    public DbSet<Machine> Machines => Set<Machine>();
    public DbSet<LearningMatch> LearningMatches => Set<LearningMatch>();
    public DbSet<ActionDecision> ActionDecisions => Set<ActionDecision>();
    public DbSet<ActionCandidate> ActionCandidates => Set<ActionCandidate>();
    public DbSet<ChoiceDecision> ChoiceDecisions => Set<ChoiceDecision>();
    public DbSet<ChoiceOption> ChoiceOptions => Set<ChoiceOption>();
    public DbSet<MulliganDecision> MulliganDecisions => Set<MulliganDecision>();
    public DbSet<MulliganCard> MulliganCards => Set<MulliganCard>();
    public DbSet<ModelVersion> ModelVersions => Set<ModelVersion>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Machine>(e =>
        {
            e.HasKey(m => m.MachineId);
            e.Property(m => m.MachineId).HasMaxLength(64);
            e.HasIndex(m => m.LastSeenAt);
        });

        b.Entity<LearningMatch>(e =>
        {
            e.HasKey(m => m.MatchId);
            e.HasIndex(m => m.MachineId);
            e.HasIndex(m => m.StartAt);
        });

        b.Entity<ActionDecision>(e =>
        {
            e.HasKey(d => d.DecisionId);
            e.Property(d => d.DecisionId).ValueGeneratedOnAdd();
            e.HasIndex(d => d.ClientSampleId).IsUnique();
            e.HasIndex(d => d.MatchId);
            e.HasIndex(d => d.CreatedAt);
            e.HasIndex(d => d.MappingStatus);
        });

        b.Entity<ActionCandidate>(e =>
        {
            e.HasKey(c => c.CandidateId);
            e.Property(c => c.CandidateId).ValueGeneratedOnAdd();
            e.HasIndex(c => c.DecisionId);
            e.HasIndex(c => c.IsTeacherPick);
        });

        b.Entity<ChoiceDecision>(e =>
        {
            e.HasKey(d => d.DecisionId);
            e.Property(d => d.DecisionId).ValueGeneratedOnAdd();
            e.HasIndex(d => d.ClientSampleId).IsUnique();
            e.HasIndex(d => d.MatchId);
            e.HasIndex(d => d.CreatedAt);
        });

        b.Entity<ChoiceOption>(e =>
        {
            e.HasKey(o => o.OptionId);
            e.Property(o => o.OptionId).ValueGeneratedOnAdd();
            e.HasIndex(o => o.DecisionId);
        });

        b.Entity<MulliganDecision>(e =>
        {
            e.HasKey(d => d.DecisionId);
            e.Property(d => d.DecisionId).ValueGeneratedOnAdd();
            e.HasIndex(d => d.ClientSampleId).IsUnique();
            e.HasIndex(d => d.MatchId);
            e.HasIndex(d => d.CreatedAt);
        });

        b.Entity<MulliganCard>(e =>
        {
            e.HasKey(c => c.CardEntryId);
            e.Property(c => c.CardEntryId).ValueGeneratedOnAdd();
            e.HasIndex(c => c.DecisionId);
        });

        b.Entity<ModelVersion>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.Id).ValueGeneratedOnAdd();
            e.HasIndex(m => m.Version).IsUnique();
            e.HasIndex(m => new { m.ModelType, m.IsActive });
        });
    }
}
