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
        ["nav.tutor_review"] = T("Tutor chats", "Sembang tutor", "导师对话", "ஆசிரியர் அரட்டை"),
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

        // Common (reused across pages)
        ["common.retry"] = T("Retry", "Cuba lagi", "重试", "மீண்டும் முயற்சி"),
        ["common.lessons"] = T("lessons", "pelajaran", "课", "பாடங்கள்"),
        ["common.no_student"] = T(
            "No student profile is linked to your account.",
            "Tiada profil pelajar dikaitkan dengan akaun anda.",
            "您的账户未关联学生档案。",
            "உங்கள் கணக்குடன் மாணவர் சுயவிவரம் இணைக்கப்படவில்லை."),

        // Parent — tutor conversation review
        ["parent.tutor.title"] = T("Tutor conversations", "Perbualan tutor", "导师对话", "ஆசிரியர் உரையாடல்கள்"),
        ["parent.tutor.sub"] = T(
            "Review your child's AI tutor chats. Flagged conversations appear first.",
            "Semak sembang tutor AI anak anda. Perbualan yang dibenderakan dipaparkan dahulu.",
            "查看您孩子与 AI 导师的对话。被标记的对话会优先显示。",
            "உங்கள் குழந்தையின் AI ஆசிரியர் அரட்டைகளை மதிப்பாய்வு செய்யுங்கள். கொடியிடப்பட்ட உரையாடல்கள் முதலில் தோன்றும்."),
        ["parent.tutor.child"] = T("Child", "Anak", "孩子", "குழந்தை"),
        ["parent.tutor.no_children"] = T(
            "No child profiles are linked to your account.",
            "Tiada profil anak dikaitkan dengan akaun anda.",
            "您的账户未关联任何孩子档案。",
            "உங்கள் கணக்குடன் குழந்தை சுயவிவரங்கள் எதுவும் இணைக்கப்படவில்லை."),
        ["parent.tutor.loading"] = T("Loading conversations…", "Memuatkan perbualan…", "正在加载对话…", "உரையாடல்களை ஏற்றுகிறது…"),
        ["parent.tutor.error"] = T(
            "We couldn't load conversations. Please try again.",
            "Kami tidak dapat memuatkan perbualan. Sila cuba lagi.",
            "无法加载对话，请重试。",
            "உரையாடல்களை ஏற்ற முடியவில்லை. மீண்டும் முயற்சிக்கவும்."),
        ["parent.tutor.empty"] = T(
            "No tutor conversations yet.",
            "Tiada perbualan tutor lagi.",
            "暂无导师对话。",
            "இன்னும் ஆசிரியர் உரையாடல்கள் இல்லை."),
        ["parent.tutor.flagged"] = T("Flagged", "Dibenderakan", "已标记", "கொடியிடப்பட்டது"),
        ["parent.tutor.messages"] = T("messages", "mesej", "条消息", "செய்திகள்"),
        ["parent.tutor.back"] = T("Back to conversations", "Kembali ke perbualan", "返回对话列表", "உரையாடல்களுக்குத் திரும்பு"),
        ["parent.tutor.role.student"] = T("Child", "Anak", "孩子", "குழந்தை"),
        ["parent.tutor.role.tutor"] = T("AI Tutor", "Tutor AI", "AI 导师", "AI ஆசிரியர்"),

        // Student — shared
        ["student.greeting"] = T("Hi, {0}!", "Hai, {0}!", "你好，{0}！", "வணக்கம், {0}!"),
        ["student.you"] = T("there", "kawan", "同学", "நண்பரே"),

        // Student — home/today
        ["student.home.sub"] = T(
            "Here's your learning for today.",
            "Inilah pembelajaran anda untuk hari ini.",
            "这是您今天的学习内容。",
            "இன்றைக்கான உங்கள் கற்றல் இங்கே."),
        ["student.home.loading"] = T("Loading your day…", "Memuatkan hari anda…", "正在加载今天的内容…", "உங்கள் நாளை ஏற்றுகிறது…"),
        ["student.home.error"] = T(
            "We couldn't load your home page. Please try again.",
            "Kami tidak dapat memuatkan halaman utama anda. Sila cuba lagi.",
            "无法加载您的主页，请重试。",
            "உங்கள் முகப்புப் பக்கத்தை ஏற்ற முடியவில்லை. மீண்டும் முயற்சிக்கவும்."),
        ["student.stat.points"] = T("Points", "Mata", "积分", "புள்ளிகள்"),
        ["student.stat.mastery"] = T("Overall mastery", "Penguasaan keseluruhan", "总体掌握度", "மொத்த தேர்ச்சி"),
        ["student.stat.lessons_done"] = T("Lessons done", "Pelajaran selesai", "已完成课程", "முடிந்த பாடங்கள்"),
        ["student.stat.badges"] = T("Badges", "Lencana", "徽章", "பதக்கங்கள்"),
        ["student.subjects.title"] = T("Your subjects", "Subjek anda", "您的科目", "உங்கள் பாடங்கள்"),
        ["student.subjects.empty"] = T(
            "No subjects are available yet.",
            "Tiada subjek tersedia lagi.",
            "暂无可用科目。",
            "இன்னும் பாடங்கள் எதுவும் இல்லை."),
        ["student.view_progress"] = T("View detailed progress", "Lihat kemajuan terperinci", "查看详细进度", "விரிவான முன்னேற்றத்தைக் காண்க"),
        ["student.cta.title"] = T("Keep learning", "Teruskan belajar", "继续学习", "தொடர்ந்து கற்க"),
        ["student.cta.sub"] = T(
            "Practise with your AI tutor or jump back into a lesson.",
            "Berlatih dengan tutor AI anda atau sambung semula pelajaran.",
            "与 AI 导师一起练习，或回到课程中。",
            "உங்கள் AI ஆசிரியருடன் பயிற்சி செய்யுங்கள் அல்லது பாடத்திற்குத் திரும்புங்கள்."),
        ["student.cta.tutor"] = T("💬 Ask the AI tutor", "💬 Tanya tutor AI", "💬 询问 AI 导师", "💬 AI ஆசிரியரிடம் கேளுங்கள்"),
        ["student.cta.progress"] = T("📈 See my progress", "📈 Lihat kemajuan saya", "📈 查看我的进度", "📈 எனது முன்னேற்றத்தைக் காண்க"),
        ["student.badges.recent"] = T("Recent badges", "Lencana terbaru", "最近的徽章", "சமீபத்திய பதக்கங்கள்"),

        // Student — adaptive recommendations
        ["student.recs.title"] = T("Recommended for you", "Disyorkan untuk anda", "为你推荐", "உங்களுக்கான பரிந்துரைகள்"),
        ["student.recs.reason.continue"] = T("Continue", "Sambung", "继续", "தொடர்க"),
        ["student.recs.reason.review"] = T("Review", "Ulang kaji", "复习", "மீள்பார்வை"),
        ["student.recs.reason.new"] = T("New", "Baharu", "新课", "புதியது"),

        // Student — streak & daily goal
        ["student.streak.days"] = T("{0}-day streak", "{0} hari berturut", "连续 {0} 天", "{0} நாள் தொடர்"),
        ["student.streak.start"] = T("Start your streak today!", "Mulakan rentetan anda hari ini!", "今天开始你的连胜吧！", "இன்று உங்கள் தொடரைத் தொடங்குங்கள்!"),
        ["student.streak.best"] = T("Best: {0} days", "Terbaik: {0} hari", "最佳：{0} 天", "சிறந்தது: {0} நாட்கள்"),
        ["student.streak.goal_met"] = T("Today's goal done! 🎉", "Matlamat hari ini selesai! 🎉", "今日目标已完成！🎉", "இன்றைய இலக்கு முடிந்தது! 🎉"),
        ["student.streak.goal_progress"] = T("{0} / {1} min today", "{0} / {1} min hari ini", "今日 {0} / {1} 分钟", "இன்று {0} / {1} நிமிடம்"),

        // Student — progress
        ["student.progress.title"] = T("My progress", "Kemajuan saya", "我的进度", "எனது முன்னேற்றம்"),
        ["student.progress.sub"] = T(
            "Track your mastery across subjects and your activity over time.",
            "Pantau penguasaan anda merentas subjek dan aktiviti anda dari masa ke masa.",
            "跟踪您各科目的掌握程度和长期活动情况。",
            "பாடங்கள் முழுவதும் உங்கள் தேர்ச்சியையும் காலப்போக்கில் உங்கள் செயல்பாட்டையும் கண்காணிக்கவும்."),
        ["student.progress.loading"] = T("Loading progress…", "Memuatkan kemajuan…", "正在加载进度…", "முன்னேற்றத்தை ஏற்றுகிறது…"),
        ["student.progress.error"] = T(
            "We couldn't load your progress. Please try again.",
            "Kami tidak dapat memuatkan kemajuan anda. Sila cuba lagi.",
            "无法加载您的进度，请重试。",
            "உங்கள் முன்னேற்றத்தை ஏற்ற முடியவில்லை. மீண்டும் முயற்சிக்கவும்."),
        ["student.progress.lessons_completed"] = T("Lessons completed", "Pelajaran selesai", "已完成课程", "முடிக்கப்பட்ட பாடங்கள்"),
        ["student.progress.subjects"] = T("Subjects", "Subjek", "科目", "பாடங்கள்"),
        ["student.progress.no_subjects"] = T("No subject data yet.", "Tiada data subjek lagi.", "暂无科目数据。", "இன்னும் பாட தரவு இல்லை."),
        ["student.progress.activity"] = T("Activity", "Aktiviti", "活动", "செயல்பாடு"),
        ["student.progress.awarded"] = T("Awarded {0}", "Dianugerahkan {0}", "获得于 {0}", "வழங்கப்பட்டது {0}"),

        // Student — per-standard mastery gap map
        ["student.standards.view"] = T("View standards", "Lihat standard", "查看标准", "தரநிலைகளைக் காண்க"),
        ["student.standards.hide"] = T("Hide standards", "Sembunyi standard", "隐藏标准", "தரநிலைகளை மறை"),
        ["student.standards.loading"] = T("Loading standards…", "Memuatkan standard…", "正在加载标准…", "தரநிலைகளை ஏற்றுகிறது…"),
        ["student.standards.error"] = T(
            "We couldn't load standards. Please try again.",
            "Kami tidak dapat memuatkan standard. Sila cuba lagi.",
            "无法加载标准，请重试。",
            "தரநிலைகளை ஏற்ற முடியவில்லை. மீண்டும் முயற்சிக்கவும்."),
        ["student.standards.empty"] = T(
            "No standards defined for this subject yet.",
            "Tiada standard ditakrifkan untuk subjek ini lagi.",
            "此科目尚未定义标准。",
            "இந்தப் பாடத்திற்கு இன்னும் தரநிலைகள் வரையறுக்கப்படவில்லை."),
        ["student.standards.mastered"] = T("Mastered", "Dikuasai", "已掌握", "தேர்ச்சி பெற்றது"),
        ["student.standards.developing"] = T("Developing", "Sedang berkembang", "发展中", "வளர்ந்து வருகிறது"),
        ["student.standards.not_started"] = T("Not started", "Belum dimulakan", "未开始", "தொடங்கவில்லை"),
        ["student.standards.target"] = T("Target {0}", "Sasaran {0}", "目标 {0}", "இலக்கு {0}"),
        ["student.standards.practise"] = T("Practise", "Berlatih", "练习", "பயிற்சி"),

        // Student — tutor
        ["student.tutor.title"] = T("AI Tutor", "Tutor AI", "AI 导师", "AI ஆசிரியர்"),
        ["student.tutor.sub"] = T(
            "A friendly tutor aligned to your KPM curriculum.",
            "Tutor mesra yang sejajar dengan kurikulum KPM anda.",
            "贴合您 KPM 课程的友好导师。",
            "உங்கள் KPM பாடத்திட்டத்துடன் இணைந்த நட்பான ஆசிரியர்."),
        ["student.tutor.new"] = T("New conversation", "Perbualan baharu", "新对话", "புதிய உரையாடல்"),
        ["student.tutor.loading"] = T("Starting your tutor session…", "Memulakan sesi tutor anda…", "正在开始导师会话…", "உங்கள் ஆசிரியர் அமர்வைத் தொடங்குகிறது…"),
        ["student.tutor.error"] = T(
            "We couldn't start a tutor session. Please try again.",
            "Kami tidak dapat memulakan sesi tutor. Sila cuba lagi.",
            "无法开始导师会话，请重试。",
            "ஆசிரியர் அமர்வைத் தொடங்க முடியவில்லை. மீண்டும் முயற்சிக்கவும்."),

        // Student — lesson
        ["student.lesson.title_fallback"] = T("Lesson", "Pelajaran", "课程", "பாடம்"),
        ["student.lesson.loading"] = T("Loading lesson…", "Memuatkan pelajaran…", "正在加载课程…", "பாடத்தை ஏற்றுகிறது…"),
        ["student.lesson.back"] = T("Back to home", "Kembali ke laman utama", "返回主页", "முகப்புக்குத் திரும்பு"),
        ["student.lesson.error"] = T(
            "We couldn't load this lesson. Please try again.",
            "Kami tidak dapat memuatkan pelajaran ini. Sila cuba lagi.",
            "无法加载此课程，请重试。",
            "இந்தப் பாடத்தை ஏற்ற முடியவில்லை. மீண்டும் முயற்சிக்கவும்."),
        ["student.lesson.not_found"] = T(
            "We couldn't find this lesson.",
            "Kami tidak dapat menemui pelajaran ini.",
            "找不到此课程。",
            "இந்தப் பாடத்தைக் கண்டுபிடிக்க முடியவில்லை."),
        ["student.lesson.practice"] = T(
            "Practice & check your understanding",
            "Berlatih & semak kefahaman anda",
            "练习并检查你的理解",
            "பயிற்சி செய்து உங்கள் புரிதலைச் சரிபார்க்கவும்"),
        ["student.lesson.activity_loading"] = T("Loading activity…", "Memuatkan aktiviti…", "正在加载活动…", "செயல்பாட்டை ஏற்றுகிறது…"),
        ["student.lesson.activity_error"] = T(
            "We couldn't load this activity. Please try again.",
            "Kami tidak dapat memuatkan aktiviti ini. Sila cuba lagi.",
            "无法加载此活动，请重试。",
            "இந்தச் செயல்பாட்டை ஏற்ற முடியவில்லை. மீண்டும் முயற்சிக்கவும்."),
    };
}
