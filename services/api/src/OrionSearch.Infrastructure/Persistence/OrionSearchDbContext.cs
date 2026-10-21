using Microsoft.EntityFrameworkCore;
using OrionSearch.Domain.Auth;
using OrionSearch.Domain.Content;
using OrionSearch.Domain.Jobs;
using OrionSearch.Domain.Knowledge;
using OrionSearch.Domain.Research;

namespace OrionSearch.Infrastructure.Persistence;

public sealed class OrionSearchDbContext(DbContextOptions<OrionSearchDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Archive> Archives => Set<Archive>();
    public DbSet<ArchiveMember> ArchiveMembers => Set<ArchiveMember>();
    public DbSet<ArchiveSettings> ArchiveSettings => Set<ArchiveSettings>();
    public DbSet<ResearchRun> ResearchRuns => Set<ResearchRun>();
    public DbSet<ResearchQuery> ResearchQueries => Set<ResearchQuery>();
    public DbSet<Publisher> Publishers => Set<Publisher>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();
    public DbSet<ArchiveDocument> ArchiveDocuments => Set<ArchiveDocument>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();
    public DbSet<Finding> Findings => Set<Finding>();
    public DbSet<FindingEvidence> FindingEvidence => Set<FindingEvidence>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobAttempt> JobAttempts => Set<JobAttempt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("citext");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrionSearchDbContext).Assembly);
    }
}
