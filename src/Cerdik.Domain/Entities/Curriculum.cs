using Cerdik.Domain.Common;

namespace Cerdik.Domain.Entities;

/// <summary>A reusable school profile describing a school-type/language/DLP configuration.</summary>
public class SchoolProfile : BaseEntity, ITenantScoped
{
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = default!;

    public string Name { get; set; } = default!;
    public SchoolType SchoolType { get; set; }
    public Language PrimaryLanguage { get; set; }
    public DlpMode DlpMode { get; set; } = DlpMode.None;
    public string? Notes { get; set; }
}

/// <summary>A versioned curriculum baseline (e.g. "KSSR Semakan 2017", "KSSM 2017").
/// We model the *structure* of standards, never copyrighted textbook content.</summary>
public class CurriculumVersion : BaseEntity
{
    /// <summary>Stable code used across retrieval filters, e.g. "KSSR-2017".</summary>
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public Level Level { get; set; }
    public int EffectiveYear { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }

    public ICollection<Subject> Subjects { get; set; } = new List<Subject>();
}

/// <summary>A subject within a curriculum version (e.g. Mathematics, Science, English).</summary>
public class Subject : BaseEntity
{
    public Guid CurriculumVersionId { get; set; }
    public CurriculumVersion CurriculumVersion { get; set; } = default!;

    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    /// <summary>Grade band, e.g. "Year 1", "Form 1", "Preschool".</summary>
    public string GradeBand { get; set; } = default!;
    public Level Level { get; set; }
    public int SortOrder { get; set; }

    public ICollection<SubjectVariant> Variants { get; set; } = new List<SubjectVariant>();
    public ICollection<LearningStandard> Standards { get; set; } = new List<LearningStandard>();
}

/// <summary>A delivery variant of a subject keyed by school_type, language and DLP mode.
/// This is the unit that lessons and retrieval are scoped to.</summary>
public class SubjectVariant : BaseEntity
{
    public Guid SubjectId { get; set; }
    public Subject Subject { get; set; } = default!;

    public SchoolType SchoolType { get; set; }
    public Language Language { get; set; }
    public DlpMode DlpMode { get; set; } = DlpMode.None;
    public PublishState State { get; set; } = PublishState.Draft;
    public string? Label { get; set; }

    public ICollection<Lesson> Lessons { get; set; } = new List<Lesson>();
}

/// <summary>A learning standard (Standard Pembelajaran). Original phrasing only — we describe the
/// outcome/skill, we do not reproduce protected textbook passages.</summary>
public class LearningStandard : BaseEntity
{
    public Guid SubjectId { get; set; }
    public Subject Subject { get; set; } = default!;

    /// <summary>Standard code, e.g. "1.1.1".</summary>
    public string Code { get; set; } = default!;
    public string Strand { get; set; } = default!;
    public string Description { get; set; } = default!;
    public MasteryBand TargetBand { get; set; } = MasteryBand.TP3;
    public int SortOrder { get; set; }
}
