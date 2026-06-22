using System.Globalization;

namespace Cerdik.Web.Services;

/// <summary>Lightweight UI localization. Resolves a key to the current request culture
/// (set by RequestLocalization from the culture cookie), falling back to English, then the key.
/// Kept as an in-code table (no .resx tooling needed) so it builds anywhere and is easy to review.</summary>
public interface IUiText
{
    /// <summary>Localized text for <paramref name="key"/> in the current UI culture.</summary>
    string this[string key] { get; }

    /// <summary>The active two-letter language code (en | ms | zh | ta).</summary>
    string Lang { get; }
}

public sealed class UiText : IUiText
{
    public static readonly string[] Supported = ["en", "ms", "zh", "ta"];

    public string Lang
    {
        get
        {
            var code = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return Supported.Contains(code) ? code : "en";
        }
    }

    public string this[string key] => Resolve(key, Lang);

    private static string Resolve(string key, string lang)
    {
        if (Strings.TryGetValue(key, out var byLang))
        {
            if (byLang.TryGetValue(lang, out var v)) return v;
            if (byLang.TryGetValue("en", out var en)) return en;
        }
        return key;
    }

    /// <summary>Native display names for the language switcher.</summary>
    public static readonly (string Code, string Name)[] Languages =
    [
        ("en", "English"),
        ("ms", "Bahasa Melayu"),
        ("zh", "中文"),
        ("ta", "தமிழ்"),
    ];

    private static Dictionary<string, string> T(string en, string ms, string zh, string ta) =>
        new() { ["en"] = en, ["ms"] = ms, ["zh"] = zh, ["ta"] = ta };

    private static readonly Dictionary<string, Dictionary<string, string>> Strings = new()
    {
        // Navigation
        ["nav.dashboard"] = T("Dashboard", "Papan Pemuka", "仪表板", "டாஷ்போர்டு"),
        ["nav.plans"] = T("Plans", "Pelan", "计划", "திட்டங்கள்"),
        ["nav.billing"] = T("Billing", "Pengebilan", "账单", "பில்லிங்"),
        ["nav.safety"] = T("Safety", "Keselamatan", "安全", "பாதுகாப்பு"),
        ["nav.today"] = T("Today", "Hari Ini", "今天", "இன்று"),
        ["nav.tutor"] = T("Tutor", "Tutor", "导师", "ஆசிரியர்"),
        ["nav.progress"] = T("Progress", "Kemajuan", "进度", "முன்னேற்றம்"),
        ["nav.analytics"] = T("Analytics", "Analitik", "分析", "பகுப்பாய்வு"),
        ["nav.users"] = T("Users", "Pengguna", "用户", "பயனர்கள்"),
        ["nav.content"] = T("Content", "Kandungan", "内容", "உள்ளடக்கம்"),
        ["nav.media"] = T("Media", "Media", "媒体", "ஊடகம்"),
        ["nav.curriculum"] = T("Curriculum", "Kurikulum", "课程", "பாடத்திட்டம்"),
        ["nav.moderation"] = T("Moderation", "Penyederhanaan", "审核", "மிதப்படுத்தல்"),
        ["nav.payments"] = T("Payments", "Pembayaran", "付款", "கட்டணங்கள்"),

        // App chrome
        ["app.signout"] = T("Sign out", "Log keluar", "登出", "வெளியேறு"),
        ["app.tagline"] = T("Malaysian Homeschooling Portal", "Portal Pendidikan Rumah Malaysia", "马来西亚在家教育门户", "மலேசிய வீட்டுக் கல்வி வாயில்"),
        ["app.language"] = T("Language", "Bahasa", "语言", "மொழி"),

        // Auth — sign in
        ["auth.signin.title"] = T("Sign in", "Log masuk", "登录", "உள்நுழைய"),
        ["auth.signin.sub"] = T("Welcome back! Please enter your details.", "Selamat kembali! Sila masukkan butiran anda.", "欢迎回来！请输入您的信息。", "மீண்டும் வரவேற்கிறோம்! உங்கள் விவரங்களை உள்ளிடவும்."),
        ["auth.email"] = T("Email", "E-mel", "电子邮件", "மின்னஞ்சல்"),
        ["auth.password"] = T("Password", "Kata laluan", "密码", "கடவுச்சொல்"),
        ["auth.forgot"] = T("Forgot password?", "Lupa kata laluan?", "忘记密码？", "கடவுச்சொல் மறந்துவிட்டதா?"),
        ["auth.signin.button"] = T("Sign in", "Log masuk", "登录", "உள்நுழைய"),
        ["auth.signin.busy"] = T("Signing in…", "Log masuk…", "正在登录…", "உள்நுழைகிறது…"),
        ["auth.signin.foot"] = T("New to cerdikMY?", "Baru di cerdikMY?", "初次使用 cerdikMY？", "cerdikMY-க்கு புதியவரா?"),
        ["auth.create"] = T("Create an account", "Cipta akaun", "创建账户", "கணக்கை உருவாக்கு"),
    };
}
