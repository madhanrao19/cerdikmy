using Cerdik.Domain.Common;

namespace Cerdik.Domain.Entities;

/// <summary>Top-level tenant (a homeschool co-op, a centre, or "Personal/Family").</summary>
public class Organization : BaseEntity
{
    public string Name { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public bool IsActive { get; set; } = true;

    public ICollection<Household> Households { get; set; } = new List<Household>();
    public ICollection<User> Users { get; set; } = new List<User>();
}

/// <summary>A family unit that groups guardians and students.</summary>
public class Household : BaseEntity, ITenantScoped
{
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = default!;

    public string Name { get; set; } = default!;
    public string? AddressLine { get; set; }
    public string? State { get; set; }
    public string? Postcode { get; set; }
    public string PreferredLanguage { get; set; } = "BM";

    public ICollection<User> Members { get; set; } = new List<User>();
    public ICollection<Student> Students { get; set; } = new List<Student>();
    public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
}

/// <summary>An authenticatable account. Parents and admins log in directly;
/// students log in through a guardian-managed account linked to a <see cref="Student"/>.</summary>
public class User : BaseEntity, ITenantScoped
{
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = default!;

    public Guid? HouseholdId { get; set; }
    public Household? Household { get; set; }

    public string Email { get; set; } = default!;
    public string? FullName { get; set; }
    public string PasswordHash { get; set; } = default!;
    public UserRole Role { get; set; } = UserRole.Parent;
    public bool EmailConfirmed { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>Set when this account *is* a student login.</summary>
    public Guid? StudentId { get; set; }
    public Student? Student { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<Consent> Consents { get; set; } = new List<Consent>();
}

/// <summary>Rotating refresh token (hashed) for the httpOnly-cookie auth flow.</summary>
public class RefreshToken : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;

    public string TokenHash { get; set; } = default!;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public string? CreatedByIp { get; set; }

    public bool IsActive => RevokedAt is null && DateTimeOffset.UtcNow < ExpiresAt;
}

/// <summary>A learner profile. May or may not have a login (User.StudentId).</summary>
public class Student : BaseEntity, ITenantScoped
{
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = default!;

    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = default!;

    public string DisplayName { get; set; } = default!;
    public string? Avatar { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public Level Level { get; set; }
    public SchoolType SchoolType { get; set; } = SchoolType.Homeschool;
    public Language PrimaryLanguage { get; set; } = Language.BM;
    public DlpMode DlpMode { get; set; } = DlpMode.None;
    public int Points { get; set; }

    public ICollection<StudentGuardian> Guardians { get; set; } = new List<StudentGuardian>();
    public ICollection<ProgressRecord> ProgressRecords { get; set; } = new List<ProgressRecord>();
    public ICollection<Attempt> Attempts { get; set; } = new List<Attempt>();
    public ICollection<StudyPlan> StudyPlans { get; set; } = new List<StudyPlan>();
}

/// <summary>Join table linking guardians (parent users) to students with a relationship label.</summary>
public class StudentGuardian : BaseEntity
{
    public Guid StudentId { get; set; }
    public Student Student { get; set; } = default!;

    public Guid GuardianUserId { get; set; }
    public User GuardianUser { get; set; } = default!;

    public string Relationship { get; set; } = "Parent";
    public bool IsPrimary { get; set; }
    public bool CanManageBilling { get; set; } = true;
}

/// <summary>Captured consent record (PDPA-aligned). Immutable once written.</summary>
public class Consent : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;

    public Guid? StudentId { get; set; }
    public Student? Student { get; set; }

    public ConsentType Type { get; set; }
    public bool Granted { get; set; }
    public string PolicyVersion { get; set; } = "1.0";
    public string? CapturedByIp { get; set; }
}

/// <summary>A guardian-configured weekly study plan for a student.</summary>
public class StudyPlan : BaseEntity
{
    public Guid StudentId { get; set; }
    public Student Student { get; set; } = default!;

    public Guid CreatedByUserId { get; set; }

    public string Name { get; set; } = "Weekly Plan";
    public int TargetMinutesPerDay { get; set; } = 30;

    /// <summary>JSON array of weekday subject targets, e.g. [{"day":"Mon","subjectId":"...","minutes":30}].</summary>
    public string ScheduleJson { get; set; } = "[]";
    public bool IsActive { get; set; } = true;
}
