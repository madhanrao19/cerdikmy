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

        // Parent — printable progress report
        ["report.title"] = T("Progress report", "Laporan kemajuan", "进度报告", "முன்னேற்ற அறிக்கை"),
        ["report.loading"] = T("Loading report…", "Memuatkan laporan…", "正在加载报告…", "அறிக்கையை ஏற்றுகிறது…"),
        ["report.back"] = T("Back to dashboard", "Kembali ke papan pemuka", "返回仪表板", "டாஷ்போர்டுக்குத் திரும்பு"),
        ["report.print"] = T("🖨️ Print report", "🖨️ Cetak laporan", "🖨️ 打印报告", "🖨️ அறிக்கையை அச்சிடு"),
        ["report.generated"] = T("Generated {0}", "Dijana {0}", "生成于 {0}", "உருவாக்கப்பட்டது {0}"),
        ["report.overall_mastery"] = T("Overall mastery", "Penguasaan keseluruhan", "总体掌握度", "மொத்த தேர்ச்சி"),
        ["report.lessons_completed"] = T("Lessons completed", "Pelajaran selesai", "已完成课程", "முடிக்கப்பட்ட பாடங்கள்"),
        ["report.badges_earned"] = T("Badges earned", "Lencana diperoleh", "获得徽章", "பெற்ற பதக்கங்கள்"),
        ["report.streak"] = T("Current streak", "Rentetan semasa", "当前连胜", "தற்போதைய தொடர்"),
        ["report.streak_days"] = T("{0} days", "{0} hari", "{0} 天", "{0} நாட்கள்"),
        ["report.subject_mastery"] = T("Subject mastery", "Penguasaan subjek", "科目掌握度", "பாடத் தேர்ச்சி"),
        ["report.col.subject"] = T("Subject", "Subjek", "科目", "பாடம்"),
        ["report.col.mastery"] = T("Mastery", "Penguasaan", "掌握度", "தேர்ச்சி"),
        ["report.col.band"] = T("Band", "Tahap", "等级", "நிலை"),
        ["report.col.lessons"] = T("Lessons", "Pelajaran", "课程", "பாடங்கள்"),
        ["report.col.last_active"] = T("Last active", "Aktif terakhir", "最近活动", "கடைசி செயல்பாடு"),
        ["report.activity"] = T("Activity (last 12 weeks)", "Aktiviti (12 minggu lepas)", "活动（最近 12 周）", "செயல்பாடு (கடந்த 12 வாரங்கள்)"),
        ["report.badges"] = T("Badges", "Lencana", "徽章", "பதக்கங்கள்"),
        ["report.exams"] = T("Recent exam results", "Keputusan peperiksaan terkini", "近期考试成绩", "சமீபத்திய தேர்வு முடிவுகள்"),
        ["report.col.date"] = T("Date", "Tarikh", "日期", "தேதி"),
        ["report.col.score"] = T("Score", "Markah", "分数", "மதிப்பெண்"),
        ["report.col.grade"] = T("Grade", "Gred", "等级", "தரம்"),
        ["report.no_exams"] = T("No exams taken yet.", "Tiada peperiksaan diambil lagi.", "尚未参加考试。", "இன்னும் தேர்வுகள் எடுக்கவில்லை."),
        ["report.not_found"] = T(
            "We couldn't find a report for this student.",
            "Kami tidak dapat menemui laporan untuk pelajar ini.",
            "找不到该学生的报告。",
            "இந்த மாணவருக்கான அறிக்கையைக் கண்டுபிடிக்க முடியவில்லை."),
        ["report.error"] = T(
            "We couldn't load this report. Please try again.",
            "Kami tidak dapat memuatkan laporan ini. Sila cuba lagi.",
            "无法加载此报告，请重试。",
            "இந்த அறிக்கையை ஏற்ற முடியவில்லை. மீண்டும் முயற்சிக்கவும்."),

        // Parent — predictive insights / outlook
        ["insights.title"] = T("Outlook", "Tinjauan", "前景预测", "எதிர்நோக்கு"),
        ["insights.projected"] = T("Projected grade", "Gred dijangka", "预测等级", "எதிர்பார்க்கப்படும் தரம்"),
        ["insights.trend"] = T("Trend", "Aliran", "趋势", "போக்கு"),
        ["insights.risk"] = T("Status", "Status", "状态", "நிலை"),
        ["insights.trend.improving"] = T("Improving ↑", "Bertambah baik ↑", "进步中 ↑", "மேம்படுகிறது ↑"),
        ["insights.trend.steady"] = T("Steady →", "Stabil →", "稳定 →", "நிலையானது →"),
        ["insights.trend.declining"] = T("Declining ↓", "Menurun ↓", "下降中 ↓", "குறைகிறது ↓"),
        ["insights.risk.low"] = T("On track", "Di landasan", "进展良好", "சரியான பாதையில்"),
        ["insights.risk.medium"] = T("Needs attention", "Perlu perhatian", "需要关注", "கவனம் தேவை"),
        ["insights.risk.high"] = T("At risk", "Berisiko", "有风险", "ஆபத்தில்"),
        ["insights.based_on_exams"] = T("Based on {0} recent exam(s)", "Berdasarkan {0} peperiksaan terkini", "基于最近 {0} 次考试", "சமீபத்திய {0} தேர்வுகளின் அடிப்படையில்"),
        ["insights.based_on_mastery"] = T("Based on overall mastery", "Berdasarkan penguasaan keseluruhan", "基于总体掌握度", "மொத்த தேர்ச்சியின் அடிப்படையில்"),

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
        ["student.cta.placement"] = T("📋 Take a placement check", "📋 Buat ujian penempatan", "📋 进行分级测试", "📋 நிலை தேர்வை எடுக்கவும்"),
        ["student.cta.exam"] = T("📝 Take a mock exam", "📝 Buat peperiksaan percubaan", "📝 进行模拟考试", "📝 மாதிரித் தேர்வை எடுக்கவும்"),

        // Student — certificate
        ["cert.title"] = T("Certificate", "Sijil", "证书", "சான்றிதழ்"),
        ["cert.view"] = T("🏅 View certificate", "🏅 Lihat sijil", "🏅 查看证书", "🏅 சான்றிதழைப் பார்"),
        ["cert.heading"] = T("Certificate of Achievement", "Sijil Pencapaian", "成就证书", "சாதனைச் சான்றிதழ்"),
        ["cert.awarded_to"] = T("This certifies that", "Ini mengesahkan bahawa", "兹证明", "இது சான்றளிக்கிறது"),
        ["cert.completed"] = T("has successfully passed the mock exam for", "telah berjaya lulus peperiksaan percubaan bagi", "已成功通过以下科目的模拟考试", "பின்வரும் பாடத்திற்கான மாதிரித் தேர்வில் வெற்றிகரமாக தேர்ச்சி பெற்றார்"),
        ["cert.with_grade"] = T("with grade", "dengan gred", "成绩为", "தரத்துடன்"),
        ["cert.issued"] = T("Issued {0}", "Dikeluarkan {0}", "颁发于 {0}", "வழங்கப்பட்டது {0}"),
        ["cert.print"] = T("🖨️ Print", "🖨️ Cetak", "🖨️ 打印", "🖨️ அச்சிடு"),
        ["cert.back"] = T("Back", "Kembali", "返回", "திரும்பு"),
        ["cert.not_found"] = T("Certificate not found.", "Sijil tidak ditemui.", "找不到证书。", "சான்றிதழ் கிடைக்கவில்லை."),

        // Student — mock exam
        ["student.exam.title"] = T("Mock exam", "Peperiksaan percubaan", "模拟考试", "மாதிரித் தேர்வு"),
        ["student.exam.sub"] = T(
            "A timed practice paper, exam-style.",
            "Kertas latihan bermasa, gaya peperiksaan.",
            "限时模拟试卷，考试形式。",
            "நேரம் கட்டுப்படுத்தப்பட்ட தேர்வு பாணி பயிற்சித் தாள்."),
        ["student.exam.pick"] = T("Choose a subject", "Pilih subjek", "选择科目", "ஒரு பாடத்தைத் தேர்வுசெய்க"),
        ["student.exam.start"] = T("Start exam", "Mula peperiksaan", "开始考试", "தேர்வைத் தொடங்கு"),
        ["student.exam.loading"] = T("Loading…", "Memuatkan…", "加载中…", "ஏற்றுகிறது…"),
        ["student.exam.error"] = T(
            "We couldn't load the exam. Please try again.",
            "Kami tidak dapat memuatkan peperiksaan. Sila cuba lagi.",
            "无法加载考试，请重试。",
            "தேர்வை ஏற்ற முடியவில்லை. மீண்டும் முயற்சிக்கவும்."),
        ["student.exam.time_left"] = T("Time left", "Masa berbaki", "剩余时间", "மீதமுள்ள நேரம்"),
        ["student.exam.submit"] = T("Submit exam", "Hantar peperiksaan", "提交考试", "தேர்வைச் சமர்ப்பி"),
        ["student.exam.submitting"] = T("Submitting…", "Menghantar…", "提交中…", "சமர்ப்பிக்கிறது…"),
        ["student.exam.result_title"] = T("Exam result", "Keputusan peperiksaan", "考试结果", "தேர்வு முடிவு"),
        ["student.exam.grade"] = T("Grade", "Gred", "等级", "தரம்"),
        ["student.exam.score"] = T("{0} / {1} correct", "{0} / {1} betul", "{0} / {1} 正确", "{0} / {1} சரி"),
        ["student.exam.time_taken"] = T("Time: {0}", "Masa: {0}", "用时：{0}", "நேரம்: {0}"),
        ["student.exam.retake"] = T("Take another", "Ambil yang lain", "再考一次", "மற்றொன்றை எடு"),
        ["student.exam.history"] = T("Past results", "Keputusan lepas", "历史成绩", "முந்தைய முடிவுகள்"),
        ["student.exam.no_history"] = T("No past exams yet.", "Tiada peperiksaan lepas lagi.", "暂无历史考试。", "இன்னும் முந்தைய தேர்வுகள் இல்லை."),

        // Student — placement / diagnostic test
        ["student.placement.title"] = T("Placement check", "Ujian penempatan", "分级测试", "நிலைத் தேர்வு"),
        ["student.placement.sub"] = T(
            "A quick quiz to find the best place to start.",
            "Kuiz ringkas untuk mencari tempat terbaik untuk bermula.",
            "一个快速测验，帮你找到最合适的起点。",
            "தொடங்குவதற்கு சிறந்த இடத்தைக் கண்டறிய ஒரு விரைவான வினாடி வினா."),
        ["student.placement.pick"] = T("Choose a subject", "Pilih subjek", "选择科目", "ஒரு பாடத்தைத் தேர்வுசெய்க"),
        ["student.placement.start"] = T("Start placement", "Mula penempatan", "开始测试", "தேர்வைத் தொடங்கு"),
        ["student.placement.loading"] = T("Loading…", "Memuatkan…", "加载中…", "ஏற்றுகிறது…"),
        ["student.placement.error"] = T(
            "We couldn't load the placement. Please try again.",
            "Kami tidak dapat memuatkan penempatan. Sila cuba lagi.",
            "无法加载测试，请重试。",
            "தேர்வை ஏற்ற முடியவில்லை. மீண்டும் முயற்சிக்கவும்."),
        ["student.placement.empty"] = T(
            "No placement questions are available for this subject yet.",
            "Tiada soalan penempatan tersedia untuk subjek ini lagi.",
            "此科目暂无测试题目。",
            "இந்தப் பாடத்திற்கு இன்னும் தேர்வு கேள்விகள் இல்லை."),
        ["student.placement.submit"] = T("See my result", "Lihat keputusan saya", "查看结果", "எனது முடிவைப் பார்"),
        ["student.placement.submitting"] = T("Checking…", "Menyemak…", "检查中…", "சரிபார்க்கிறது…"),
        ["student.placement.result_title"] = T("Your placement result", "Keputusan penempatan anda", "你的测试结果", "உங்கள் தேர்வு முடிவு"),
        ["student.placement.score"] = T("You scored {0}%", "Anda mendapat {0}%", "你得了 {0}%", "நீங்கள் {0}% பெற்றீர்கள்"),
        ["student.placement.recommended"] = T("Suggested starting level: {0}", "Tahap permulaan dicadangkan: {0}", "建议起始级别：{0}", "பரிந்துரைக்கப்பட்ட தொடக்க நிலை: {0}"),
        ["student.placement.retake"] = T("Take another", "Ambil yang lain", "再测一次", "மற்றொன்றை எடு"),
        ["student.badges.recent"] = T("Recent badges", "Lencana terbaru", "最近的徽章", "சமீபத்திய பதக்கங்கள்"),

        // Quiz / activity
        ["quiz.passed"] = T("Passed", "Lulus", "通过", "தேர்ச்சி"),
        ["quiz.keep_practising"] = T("Keep practising", "Teruskan berlatih", "继续练习", "தொடர்ந்து பயிற்சி செய்"),
        ["quiz.submit"] = T("Submit answers", "Hantar jawapan", "提交答案", "பதில்களைச் சமர்ப்பி"),
        ["quiz.submitting"] = T("Submitting…", "Menghantar…", "提交中…", "சமர்ப்பிக்கிறது…"),
        ["quiz.try_again"] = T("Try again", "Cuba lagi", "再试一次", "மீண்டும் முயற்சி"),
        ["quiz.correct"] = T("Correct", "Betul", "正确", "சரி"),
        ["quiz.incorrect"] = T("Incorrect", "Salah", "错误", "தவறு"),
        ["quiz.correct_answer"] = T("Correct answer:", "Jawapan betul:", "正确答案：", "சரியான பதில்:"),
        ["quiz.points"] = T("pts", "mata", "分", "புள்ளி"),
        ["quiz.error"] = T(
            "We couldn't submit your answers. Please try again.",
            "Kami tidak dapat menghantar jawapan anda. Sila cuba lagi.",
            "无法提交您的答案，请重试。",
            "உங்கள் பதில்களைச் சமர்ப்பிக்க முடியவில்லை. மீண்டும் முயற்சிக்கவும்."),
        ["quiz.true"] = T("True", "Benar", "是", "சரி"),
        ["quiz.false"] = T("False", "Palsu", "否", "தவறு"),

        // Accessibility (landmarks / skip link)
        ["a11y.skip"] = T("Skip to main content", "Langkau ke kandungan utama", "跳到主要内容", "முதன்மை உள்ளடக்கத்திற்குச் செல்"),
        ["a11y.toggle_nav"] = T("Toggle navigation menu", "Togol menu navigasi", "切换导航菜单", "வழிசெலுத்தல் பட்டியலை மாற்று"),
        ["a11y.sidebar"] = T("Sidebar", "Bar sisi", "侧边栏", "பக்கப்பட்டி"),
        ["a11y.main"] = T("Main content", "Kandungan utama", "主要内容", "முதன்மை உள்ளடக்கம்"),

        // Read aloud (text-to-speech)
        ["read.listen"] = T("Listen", "Dengar", "朗读", "கேள்"),
        ["read.stop"] = T("Stop", "Berhenti", "停止", "நிறுத்து"),

        // Student — spaced-repetition review
        ["student.review.title"] = T("Due for review", "Perlu diulang kaji", "待复习", "மீள்பார்வைக்கு உரியது"),
        ["student.review.sub"] = T("Refresh these to keep them strong.", "Segarkan semula untuk mengekalkannya.", "复习这些以巩固记忆。", "இவற்றை வலுவாக வைத்திருக்க புதுப்பிக்கவும்."),
        ["student.review.due"] = T("Review", "Ulang kaji", "复习", "மீள்பார்வை"),

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
