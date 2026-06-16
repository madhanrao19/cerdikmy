namespace Cerdik.Domain;

/// <summary>Curriculum education level (KPM-aligned).</summary>
public enum Level
{
    Preschool,
    Primary,
    LowerSecondary,
    UpperSecondary,
}

/// <summary>Malaysian school type variants.</summary>
public enum SchoolType
{
    /// <summary>Sekolah Kebangsaan.</summary>
    SK,
    /// <summary>Sekolah Jenis Kebangsaan (Cina).</summary>
    SJKC,
    /// <summary>Sekolah Jenis Kebangsaan (Tamil).</summary>
    SJKT,
    /// <summary>Sekolah Menengah Kebangsaan.</summary>
    SMK,
    /// <summary>Sekolah Menengah Kebangsaan Agama.</summary>
    SMKA,
    /// <summary>Sekolah Agama Bantuan Kerajaan.</summary>
    SABK,
    Homeschool,
    Private,
}

/// <summary>Instruction / content language.</summary>
public enum Language
{
    /// <summary>Bahasa Melayu.</summary>
    BM,
    /// <summary>English.</summary>
    EN,
    /// <summary>Chinese (Mandarin).</summary>
    ZH,
    /// <summary>Tamil.</summary>
    TA,
    Other,
}

/// <summary>Dual Language Programme mode.</summary>
public enum DlpMode
{
    None,
    Bilingual,
    DlpSubjectVariant,
}

/// <summary>Platform roles for RBAC.</summary>
public enum UserRole
{
    Parent,
    Student,
    Admin,
    ContentAdmin,
    SafetyReviewer,
}

public enum ConsentType
{
    DataProcessing,
    AiTutoring,
    Marketing,
    MediaCapture,
}

public enum PublishState
{
    Draft,
    InReview,
    Published,
    Unpublished,
    Archived,
}

public enum LessonBlockType
{
    Text,
    Image,
    Video,
    Audio,
    Interactive,
    Callout,
    WorkedExample,
}

public enum ActivityType
{
    Quiz,
    Exercise,
    Practice,
    Assessment,
}

public enum QuestionType
{
    MultipleChoice,
    TrueFalse,
    ShortAnswer,
    Numeric,
    Ordering,
}

public enum AttemptStatus
{
    InProgress,
    Submitted,
    Graded,
    Abandoned,
}

/// <summary>Tahap Penguasaan — KPM-style mastery band (1–6).</summary>
public enum MasteryBand
{
    TP1 = 1,
    TP2 = 2,
    TP3 = 3,
    TP4 = 4,
    TP5 = 5,
    TP6 = 6,
}

public enum TutorMessageRole
{
    System,
    User,
    Assistant,
}

public enum ModerationStage
{
    PreGeneration,
    PostGeneration,
    ManualReport,
}

public enum ModerationDecision
{
    Allow,
    Flag,
    Block,
    Escalate,
}

public enum RiskLevel
{
    None,
    Low,
    Medium,
    High,
    Critical,
}

public enum SubscriptionStatus
{
    Trialing,
    Active,
    PastDue,
    Canceled,
    Expired,
}

public enum PaymentProvider
{
    Billplz,
    Curlec,
    Stripe,
}

public enum PaymentStatus
{
    Pending,
    Succeeded,
    Failed,
    Refunded,
}

public enum InvoiceStatus
{
    Open,
    Paid,
    Void,
    Uncollectible,
}

public enum PrivacyRequestType
{
    Export,
    Delete,
}

public enum PrivacyRequestStatus
{
    Received,
    Processing,
    Completed,
    Rejected,
}
