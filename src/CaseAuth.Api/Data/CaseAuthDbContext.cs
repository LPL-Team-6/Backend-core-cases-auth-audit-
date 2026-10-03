using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using CaseAuth.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace CaseAuth.Api.Data;

public class CaseAuthDbContext(DbContextOptions<CaseAuthDbContext> options) : DbContext(options)
{
    public DbSet<Case> Cases => Set<Case>();
    public DbSet<Applicant> Applicants => Set<Applicant>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<ExtractedField> ExtractedFields => Set<ExtractedField>();
    public DbSet<Finding> Findings => Set<Finding>();
    public DbSet<AiReview> AiReviews => Set<AiReview>();
    public DbSet<Decision> Decisions => Set<Decision>();
    public DbSet<CaseNote> CaseNotes => Set<CaseNote>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<ProcessingJob> ProcessingJobs => Set<ProcessingJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Case>(b =>
        {
            b.Property(c => c.Status).HasConversion<string>();
            b.Property(c => c.RowVersion).IsConcurrencyToken();
            b.HasIndex(c => c.FirmId);
            b.HasOne(c => c.Applicant).WithOne(a => a.Case).HasForeignKey<Case>(c => c.ApplicantId);
        });

        modelBuilder.Entity<Applicant>(b =>
        {
            b.HasIndex(a => a.FirmId);
            b.Property(a => a.Kind).HasConversion<string>();
        });

        modelBuilder.Entity<Document>(b =>
        {
            b.Property(d => d.DocumentType).HasConversion<string>();
            b.HasOne(d => d.Case).WithMany(c => c.Documents).HasForeignKey(d => d.CaseId);
            b.HasIndex(d => d.CaseId);
        });

        modelBuilder.Entity<ExtractedField>(b =>
        {
            b.HasOne(f => f.Document).WithMany(d => d.ExtractedFields).HasForeignKey(f => f.DocumentId);
            b.HasIndex(f => f.DocumentId);
        });

        modelBuilder.Entity<Finding>(b =>
        {
            b.Property(f => f.Severity).HasConversion<string>();
            b.Property(f => f.Source).HasConversion<string>();
            b.HasOne(f => f.Case).WithMany(c => c.Findings).HasForeignKey(f => f.CaseId);
            b.HasIndex(f => f.CaseId);
            // Unidirectional many-to-many via a shadow join table (FindingExtractedField) -
            // ExtractedField doesn't need to know which findings cite it.
            b.HasMany(f => f.SourceFields).WithMany();
        });

        modelBuilder.Entity<AiReview>(b =>
        {
            b.Property(r => r.Recommendation).HasConversion<string>();
            b.HasOne(r => r.Case).WithMany(c => c.AiReviews).HasForeignKey(r => r.CaseId);
            b.HasIndex(r => new { r.CaseId, r.Version }).IsUnique();
            // Stored as JSON text rather than EF's provider-specific collection mapping, so the
            // one migration set keeps working on both SQLite and Postgres.
            b.Property(r => r.KeyConcerns).HasConversion(JsonColumn<List<AiConcern>>(), JsonComparer<List<AiConcern>>());
            b.Property(r => r.NextSteps).HasConversion(JsonColumn<List<string>>(), JsonComparer<List<string>>());
        });

        modelBuilder.Entity<CaseNote>(b =>
        {
            b.HasKey(n => n.CaseId);
            b.HasOne(n => n.Case).WithOne().HasForeignKey<CaseNote>(n => n.CaseId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Decision>(b =>
        {
            b.Property(d => d.Outcome).HasConversion<string>();
            b.HasOne(d => d.Case).WithMany(c => c.Decisions).HasForeignKey(d => d.CaseId);
            b.HasOne(d => d.AiReview).WithMany().HasForeignKey(d => d.AiReviewId);
            // Enforces idempotency at the data layer: a retried request with the same key
            // can never produce a second row, even under a concurrent duplicate submission.
            b.HasIndex(d => new { d.CaseId, d.IdempotencyKey }).IsUnique();
        });

        modelBuilder.Entity<AuditEvent>(b =>
        {
            b.Property(a => a.Outcome).HasConversion<string>();
            b.HasIndex(a => a.CaseId);
            b.HasIndex(a => a.FirmId);
        });

        modelBuilder.Entity<ProcessingJob>(b =>
        {
            b.Property(j => j.JobType).HasConversion<string>();
            b.Property(j => j.Status).HasConversion<string>();
            b.HasOne(j => j.Case).WithMany().HasForeignKey(j => j.CaseId);
            b.HasIndex(j => j.Status);
            b.HasIndex(j => j.CaseId);
            b.HasIndex(j => new { j.CaseId, j.IdempotencyKey })
                .IsUnique()
                .HasFilter("\"IdempotencyKey\" IS NOT NULL");
        });
    }

    private static readonly JsonSerializerOptions JsonColumnOptions = new(JsonSerializerDefaults.Web);

    private static ValueConverter<T, string> JsonColumn<T>() where T : new() => new(
        v => JsonSerializer.Serialize(v, JsonColumnOptions),
        v => string.IsNullOrEmpty(v) ? new T() : JsonSerializer.Deserialize<T>(v, JsonColumnOptions) ?? new T());

    private static ValueComparer<T> JsonComparer<T>() where T : new() => new(
        (a, b) => JsonSerializer.Serialize(a, JsonColumnOptions) == JsonSerializer.Serialize(b, JsonColumnOptions),
        v => JsonSerializer.Serialize(v, JsonColumnOptions).GetHashCode(),
        v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, JsonColumnOptions), JsonColumnOptions) ?? new T());

    // RowVersion is an app-managed concurrency token (see Entities/Case.cs); regenerate it here
    // so SaveChanges always issues an UPDATE whose WHERE clause pins the value this context
    // loaded, and EF raises DbUpdateConcurrencyException if another writer won the race.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RefreshConcurrencyTokens();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        RefreshConcurrencyTokens();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void RefreshConcurrencyTokens()
    {
        foreach (var entry in ChangeTracker.Entries<Case>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.RowVersion = Guid.NewGuid();
                entry.Entity.UpdatedAt = DateTime.UtcNow;
            }
        }
    }
}
