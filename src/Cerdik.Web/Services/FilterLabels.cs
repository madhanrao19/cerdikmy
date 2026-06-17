using Cerdik.Domain;

namespace Cerdik.Web.Services;

/// <summary>Human-friendly display labels for domain enums used in the UI.</summary>
public static class FilterLabels
{
    public static string Level(Level level) => level switch
    {
        Cerdik.Domain.Level.Preschool => "Preschool (Prasekolah)",
        Cerdik.Domain.Level.Primary => "Primary (Rendah)",
        Cerdik.Domain.Level.LowerSecondary => "Lower Secondary (Menengah Rendah)",
        Cerdik.Domain.Level.UpperSecondary => "Upper Secondary (Menengah Atas)",
        _ => level.ToString(),
    };

    public static string Language(Language language) => language switch
    {
        Cerdik.Domain.Language.BM => "Bahasa Melayu",
        Cerdik.Domain.Language.EN => "English",
        Cerdik.Domain.Language.ZH => "中文 (Chinese)",
        Cerdik.Domain.Language.TA => "தமிழ் (Tamil)",
        _ => "Other",
    };

    public static string Dlp(DlpMode mode) => mode switch
    {
        DlpMode.None => "No DLP",
        DlpMode.Bilingual => "Bilingual",
        DlpMode.DlpSubjectVariant => "DLP subject variant",
        _ => mode.ToString(),
    };

    public static string Mastery(MasteryBand band) => band switch
    {
        MasteryBand.TP1 => "TP1 — Tahu",
        MasteryBand.TP2 => "TP2 — Faham",
        MasteryBand.TP3 => "TP3 — Boleh Guna",
        MasteryBand.TP4 => "TP4 — Mahir",
        MasteryBand.TP5 => "TP5 — Sangat Mahir",
        MasteryBand.TP6 => "TP6 — Cemerlang",
        _ => band.ToString(),
    };

    public static string Risk(RiskLevel risk) => risk.ToString();
}
