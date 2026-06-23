using Cerdik.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Cerdik.Infrastructure.Persistence;

/// <summary>EF Core context for the cerdikMY platform (SQL Server).</summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Household> Households => Set<Household>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<StudentGuardian> StudentGuardians => Set<StudentGuardian>();
    public DbSet<Consent> Consents => Set<Consent>();
    public DbSet<StudyPlan> StudyPlans => Set<StudyPlan>();

    public DbSet<SchoolProfile> SchoolProfiles => Set<SchoolProfile>();
    public DbSet<CurriculumVersion> CurriculumVersions => Set<CurriculumVersion>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<SubjectVariant> SubjectVariants => Set<SubjectVariant>();
    public DbSet<LearningStandard> LearningStandards => Set<LearningStandard>();

    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<LessonBlock> LessonBlocks => Set<LessonBlock>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<Activity> Activities => Set<Activity>();
    public DbSet<Attempt> Attempts => Set<Attempt>();
    public DbSet<ProgressRecord> ProgressRecords => Set<ProgressRecord>();
    public DbSet<Badge> Badges => Set<Badge>();
    public DbSet<ExamAttempt> ExamAttempts => Set<ExamAttempt>();

    public DbSet<TutorSession> TutorSessions => Set<TutorSession>();
    public DbSet<TutorMessage> TutorMessages => Set<TutorMessage>();
    public DbSet<Citation> Citations => Set<Citation>();
    public DbSet<ModerationEvent> ModerationEvents => Set<ModerationEvent>();
    public DbSet<EmbeddingChunk> EmbeddingChunks => Set<EmbeddingChunk>();

    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<PrivacyRequest> PrivacyRequests => Set<PrivacyRequest>();

    /// <summary>Store all enums as readable strings (max 32 chars) rather than integers.</summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        ApplyEntityConfigurations(b);
    }

    public override int SaveChanges()
    {
        StampTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void StampTimestamps()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<Domain.Common.BaseEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = entry.Entity.CreatedAt == default ? now : entry.Entity.CreatedAt;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
    }

    private static void ApplyEntityConfigurations(ModelBuilder b)
    {
        b.Entity<Organization>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Slug).HasMaxLength(120);
        });

        b.Entity<Household>(e =>
        {
            e.HasOne(x => x.Organization).WithMany(o => o.Households).HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Name).HasMaxLength(200);
        });

        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.FullName).HasMaxLength(200);
            e.HasOne(x => x.Organization).WithMany(o => o.Users).HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Household).WithMany(h => h.Members).HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.SetNull);
            // Soft-deleted / anonymized accounts (privacy delete) are excluded from all queries.
            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        b.Entity<RefreshToken>(e =>
        {
            e.HasIndex(x => x.TokenHash);
            e.Property(x => x.TokenHash).HasMaxLength(200);
            e.HasOne(x => x.User).WithMany(u => u.RefreshTokens).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.Ignore(x => x.IsActive);
        });

        b.Entity<PasswordResetToken>(e =>
        {
            e.HasIndex(x => x.TokenHash);
            e.Property(x => x.TokenHash).HasMaxLength(200);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.Ignore(x => x.IsActive);
        });

        b.Entity<Student>(e =>
        {
            e.HasOne(x => x.Household).WithMany(h => h.Students).HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.DisplayName).HasMaxLength(120);
            // Soft-deleted students (privacy delete) are excluded from all queries.
            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        b.Entity<StudentGuardian>(e =>
        {
            e.HasIndex(x => new { x.StudentId, x.GuardianUserId }).IsUnique();
            e.HasOne(x => x.Student).WithMany(s => s.Guardians).HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.GuardianUser).WithMany().HasForeignKey(x => x.GuardianUserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Consent>(e =>
        {
            e.HasOne(x => x.User).WithMany(u => u.Consents).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<StudyPlan>(e =>
        {
            e.HasOne(x => x.Student).WithMany(s => s.StudyPlans).HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<SchoolProfile>(e =>
        {
            e.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Name).HasMaxLength(200);
        });

        b.Entity<CurriculumVersion>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(200);
        });

        b.Entity<Subject>(e =>
        {
            e.HasIndex(x => new { x.CurriculumVersionId, x.Code }).IsUnique();
            e.HasOne(x => x.CurriculumVersion).WithMany(c => c.Subjects).HasForeignKey(x => x.CurriculumVersionId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Code).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(160);
            e.Property(x => x.GradeBand).HasMaxLength(64);
        });

        b.Entity<SubjectVariant>(e =>
        {
            e.HasIndex(x => new { x.SubjectId, x.SchoolType, x.Language, x.DlpMode }).IsUnique();
            e.HasOne(x => x.Subject).WithMany(s => s.Variants).HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<LearningStandard>(e =>
        {
            e.HasIndex(x => new { x.SubjectId, x.Code }).IsUnique();
            e.HasOne(x => x.Subject).WithMany(s => s.Standards).HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Code).HasMaxLength(32);
            e.Property(x => x.Strand).HasMaxLength(200);
        });

        b.Entity<Lesson>(e =>
        {
            e.HasIndex(x => new { x.SubjectVariantId, x.State });
            e.HasIndex(x => x.Slug);
            e.HasOne(x => x.SubjectVariant).WithMany(v => v.Lessons).HasForeignKey(x => x.SubjectVariantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.LearningStandard).WithMany().HasForeignKey(x => x.LearningStandardId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.Title).HasMaxLength(240);
            e.Property(x => x.Slug).HasMaxLength(160);
        });

        b.Entity<LessonBlock>(e =>
        {
            e.HasIndex(x => new { x.LessonId, x.SortOrder });
            e.HasOne(x => x.Lesson).WithMany(l => l.Blocks).HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.MediaAsset).WithMany().HasForeignKey(x => x.MediaAssetId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<MediaAsset>(e =>
        {
            e.HasIndex(x => x.StorageKey).IsUnique();
            e.Property(x => x.StorageKey).HasMaxLength(512);
            e.Property(x => x.ContentType).HasMaxLength(128);
        });

        b.Entity<Activity>(e =>
        {
            e.HasIndex(x => x.LessonId);
            e.HasOne(x => x.Lesson).WithMany(l => l.Activities).HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.QuestionsJson).HasColumnType("nvarchar(max)");
        });

        b.Entity<Attempt>(e =>
        {
            e.HasIndex(x => x.StudentId);
            e.HasIndex(x => new { x.ActivityId, x.StudentId });
            e.HasOne(x => x.Activity).WithMany(a => a.Attempts).HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Student).WithMany(s => s.Attempts).HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.NoAction);
            e.Property(x => x.AnswersJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.ResultJson).HasColumnType("nvarchar(max)");
        });

        b.Entity<ProgressRecord>(e =>
        {
            e.HasIndex(x => new { x.StudentId, x.SubjectId });
            e.HasIndex(x => new { x.StudentId, x.LessonId }).IsUnique();
            e.HasOne(x => x.Student).WithMany(s => s.ProgressRecords).HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Lesson).WithMany().HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<Badge>(e =>
        {
            e.HasIndex(x => new { x.StudentId, x.Code }).IsUnique();
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ExamAttempt>(e =>
        {
            e.HasIndex(x => new { x.StudentId, x.SubjectId });
            e.Property(x => x.SubjectName).HasMaxLength(200);
            e.Property(x => x.Grade).HasMaxLength(4);
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TutorSession>(e =>
        {
            e.HasIndex(x => x.StudentId);
            e.HasIndex(x => x.NeedsReview);
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.SubjectVariant).WithMany().HasForeignKey(x => x.SubjectVariantId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<TutorMessage>(e =>
        {
            e.HasIndex(x => x.TutorSessionId);
            e.HasOne(x => x.TutorSession).WithMany(s => s.Messages).HasForeignKey(x => x.TutorSessionId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Content).HasColumnType("nvarchar(max)");
        });

        b.Entity<Citation>(e =>
        {
            e.HasOne(x => x.TutorMessage).WithMany(m => m.Citations).HasForeignKey(x => x.TutorMessageId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.EmbeddingChunk).WithMany().HasForeignKey(x => x.EmbeddingChunkId).OnDelete(DeleteBehavior.NoAction);
            e.Property(x => x.Snippet).HasMaxLength(1000);
        });

        b.Entity<ModerationEvent>(e =>
        {
            e.HasIndex(x => new { x.InterventionRaised, x.ReviewedAt });
            e.HasOne(x => x.TutorSession).WithMany(s => s.ModerationEvents).HasForeignKey(x => x.TutorSessionId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<EmbeddingChunk>(e =>
        {
            // Retrieval filter index (curriculum_version + school_type + language + dlp_mode + subject).
            e.HasIndex(x => new { x.CurriculumVersionCode, x.SubjectId, x.SchoolType, x.Language, x.DlpMode, x.Approved })
                .HasDatabaseName("IX_EmbeddingChunk_RetrievalFilter");
            e.HasOne(x => x.Lesson).WithMany(l => l.Chunks).HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Content).HasColumnType("nvarchar(max)");
            e.Property(x => x.EmbeddingJson).HasColumnType("nvarchar(max)");
            // The in-memory float[] is reconstructed from EmbeddingJson; the native SQL Server
            // VECTOR column + ANN index is added by a raw migration (see 0002_VectorIndex).
            e.Ignore(x => x.Embedding);
        });

        b.Entity<Subscription>(e =>
        {
            e.HasIndex(x => x.HouseholdId);
            e.HasOne(x => x.Household).WithMany(h => h.Subscriptions).HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Invoice>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.HasOne(x => x.Subscription).WithMany(s => s.Invoices).HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Payment>(e =>
        {
            e.HasIndex(x => new { x.Provider, x.ProviderPaymentId }).IsUnique();
            e.HasOne(x => x.Subscription).WithMany(s => s.Payments).HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.RawPayloadJson).HasColumnType("nvarchar(max)");
        });

        b.Entity<AuditLog>(e =>
        {
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => new { x.EntityType, x.EntityId });
            e.Property(x => x.Action).HasMaxLength(120);
            e.Property(x => x.EntityType).HasMaxLength(120);
            e.Property(x => x.MetadataJson).HasColumnType("nvarchar(max)");
        });

        b.Entity<PrivacyRequest>(e =>
        {
            e.HasIndex(x => new { x.Status, x.Type });
        });
    }
}
