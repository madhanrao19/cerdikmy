using System.Text.Json;
using Cerdik.Application.Abstractions;
using Cerdik.Domain;
using Cerdik.Domain.Entities;
using Cerdik.Infrastructure.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cerdik.Infrastructure.Persistence;

/// <summary>Seeds realistic, ORIGINAL demo content. Important: none of this reproduces copyrighted
/// KPM textbook material — all lesson text is freshly written placeholder content that *maps to*
/// curriculum standards without copying protected sources.</summary>
public sealed class DemoDataSeeder
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ContentIndexer _indexer;
    private readonly ILogger<DemoDataSeeder> _log;

    public DemoDataSeeder(AppDbContext db, IPasswordHasher hasher, ContentIndexer indexer, ILogger<DemoDataSeeder> log)
    {
        _db = db;
        _hasher = hasher;
        _indexer = indexer;
        _log = log;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (await _db.Organizations.AnyAsync(ct))
        {
            _log.LogInformation("Seed skipped — data already present.");
            return;
        }

        _log.LogInformation("Seeding demo data…");

        var org = new Organization { Name = "cerdikMY Demo", Slug = "cerdikmy-demo" };
        _db.Organizations.Add(org);

        var household = new Household
        {
            Organization = org,
            Name = "Keluarga Demo",
            State = "Selangor",
            Postcode = "40000",
            PreferredLanguage = "BM",
        };
        _db.Households.Add(household);

        // ---- Accounts (one per role) ----
        var parent = NewUser(org, household, "parent.demo@cerdik.my", "Encik Demo", UserRole.Parent, "Demo!2345");
        var admin = NewUser(org, null, "admin@cerdik.my", "Platform Admin", UserRole.Admin, "Admin!2345");
        var contentAdmin = NewUser(org, null, "content@cerdik.my", "Content Editor", UserRole.ContentAdmin, "Content!2345");
        var safety = NewUser(org, null, "safety@cerdik.my", "Safety Reviewer", UserRole.SafetyReviewer, "Safety!2345");
        _db.Users.AddRange(parent, admin, contentAdmin, safety);

        // ---- Students ----
        var aisyah = new Student
        {
            Organization = org, Household = household, DisplayName = "Aisyah",
            Level = Level.Primary, SchoolType = SchoolType.SK, PrimaryLanguage = Language.BM,
            DlpMode = DlpMode.None, DateOfBirth = new DateOnly(2018, 3, 12), Points = 120,
        };
        var wei = new Student
        {
            Organization = org, Household = household, DisplayName = "Wei Han",
            Level = Level.Primary, SchoolType = SchoolType.SJKC, PrimaryLanguage = Language.ZH,
            DlpMode = DlpMode.None, DateOfBirth = new DateOnly(2017, 9, 1), Points = 80,
        };
        _db.Students.AddRange(aisyah, wei);

        // Aisyah also gets a login account.
        var aisyahLogin = NewUser(org, household, "aisyah@cerdik.my", "Aisyah", UserRole.Student, "Student!2345");
        aisyahLogin.Student = aisyah;
        _db.Users.Add(aisyahLogin);

        _db.StudentGuardians.AddRange(
            new StudentGuardian { Student = aisyah, GuardianUser = parent, IsPrimary = true, Relationship = "Parent" },
            new StudentGuardian { Student = wei, GuardianUser = parent, IsPrimary = true, Relationship = "Parent" });

        _db.Consents.Add(new Consent { User = parent, Type = ConsentType.DataProcessing, Granted = true, PolicyVersion = "1.0" });
        _db.Consents.Add(new Consent { User = parent, Type = ConsentType.AiTutoring, Granted = true, PolicyVersion = "1.0" });

        // ---- School profiles ----
        _db.SchoolProfiles.AddRange(
            new SchoolProfile { Organization = org, Name = "SK Homeschool (BM)", SchoolType = SchoolType.SK, PrimaryLanguage = Language.BM, DlpMode = DlpMode.None },
            new SchoolProfile { Organization = org, Name = "SJKC Homeschool (ZH)", SchoolType = SchoolType.SJKC, PrimaryLanguage = Language.ZH, DlpMode = DlpMode.None },
            new SchoolProfile { Organization = org, Name = "SJKT Homeschool (TA)", SchoolType = SchoolType.SJKT, PrimaryLanguage = Language.TA, DlpMode = DlpMode.None },
            new SchoolProfile { Organization = org, Name = "SMK DLP (EN)", SchoolType = SchoolType.SMK, PrimaryLanguage = Language.EN, DlpMode = DlpMode.DlpSubjectVariant });

        // ---- Curriculum versions ----
        var pra = new CurriculumVersion { Code = "KSSR-PRA-2017", Name = "KSSR Prasekolah (Semakan 2017)", Level = Level.Preschool, EffectiveYear = 2017, Description = "Preschool standards-based framework." };
        var kssr = new CurriculumVersion { Code = "KSSR-2017", Name = "KSSR (Semakan 2017)", Level = Level.Primary, EffectiveYear = 2017, Description = "Primary curriculum framework." };
        var kssmLower = new CurriculumVersion { Code = "KSSM-2017", Name = "KSSM (2017)", Level = Level.LowerSecondary, EffectiveYear = 2017, Description = "Secondary curriculum framework." };
        _db.CurriculumVersions.AddRange(pra, kssr, kssmLower);

        // A "publisher" for lessons.
        var publisher = contentAdmin;

        // ---- Preschool sample ----
        var preMath = BuildSubject(pra, "PRA-MAT", "Awal Matematik", "Preschool", Level.Preschool, 1);
        var preStd = AddStandard(preMath, "1.1.1", "Nombor & Operasi", "Membilang objek 1 hingga 10 dengan tepat.", MasteryBand.TP3);
        var preVariant = AddVariant(preMath, SchoolType.Homeschool, Language.BM, DlpMode.None, "Prasekolah BM");
        AddLesson(preVariant, preStd, publisher, "membilang-1-10", "Jom Membilang 1 hingga 10",
            "Belajar membilang objek harian dari satu hingga sepuluh.",
            [
                ("Kita boleh membilang objek di sekeliling kita. Mari mula dari nombor satu hingga sepuluh.", LessonBlockType.Text),
                ("Cuba bilang jari tangan anda: 1, 2, 3, 4, 5. Kemudian tangan sebelah lagi sehingga 10.", LessonBlockType.WorkedExample),
                ("Petua: tunjuk dan sentuh setiap objek semasa membilang supaya tidak terlepas.", LessonBlockType.Callout),
            ],
            quiz: ("Kuiz Membilang", ActivityType.Quiz,
            [
                Q("p1", "Berapakah jumlah bintang? ⭐⭐⭐", QuestionType.Numeric, [], "3", "Bilang satu per satu: 1, 2, 3."),
                Q("p2", "Selepas 5, nombor seterusnya ialah?", QuestionType.Numeric, [], "6", "Urutan: 5 kemudian 6."),
            ]));

        // ---- Primary Year 1 Mathematics across school types (SK/SJKC/SJKT) + DLP ----
        var math1 = BuildSubject(kssr, "MATE-T1", "Matematik", "Year 1", Level.Primary, 1);
        var mathAdd = AddStandard(math1, "2.1.1", "Operasi Tambah", "Menambah dua nombor dalam lingkungan 10.", MasteryBand.TP4);
        var mathPlace = AddStandard(math1, "1.2.1", "Nilai Tempat", "Mengenal nilai tempat sa dan puluh hingga 100.", MasteryBand.TP3);

        var mathSk = AddVariant(math1, SchoolType.SK, Language.BM, DlpMode.None, "Matematik SK (BM)");
        var mathSjkc = AddVariant(math1, SchoolType.SJKC, Language.ZH, DlpMode.None, "数学 SJKC (ZH)");
        var mathSjkt = AddVariant(math1, SchoolType.SJKT, Language.TA, DlpMode.None, "கணிதம் SJKT (TA)");
        var mathDlp = AddVariant(math1, SchoolType.SK, Language.EN, DlpMode.DlpSubjectVariant, "Mathematics (DLP, EN)");

        var skLesson = AddLesson(mathSk, mathAdd, publisher, "tambah-dalam-10", "Penambahan dalam Lingkungan 10",
            "Belajar menambah dua nombor supaya jumlahnya tidak melebihi 10.",
            [
                ("Menambah bermaksud menggabungkan dua kumpulan objek untuk mencari jumlah. Simbol tambah ialah +.", LessonBlockType.Text),
                ("Contoh: 3 + 4. Mula dengan 3, kemudian bilang 4 langkah ke hadapan: 4, 5, 6, 7. Jadi 3 + 4 = 7.", LessonBlockType.WorkedExample),
                ("Petua: gunakan garis nombor atau jari untuk membantu membilang ke hadapan.", LessonBlockType.Callout),
                ("Latihan: cuba 2 + 5 dan 6 + 3 sendiri sebelum menjawab kuiz.", LessonBlockType.Text),
            ],
            quiz: ("Kuiz Penambahan", ActivityType.Quiz,
            [
                Q("m1", "Berapakah 3 + 4?", QuestionType.Numeric, [], "7", "Bilang ke hadapan dari 3 sebanyak 4 langkah."),
                Q("m2", "Pilih jawapan betul untuk 2 + 5.", QuestionType.MultipleChoice, ["6", "7", "8"], "7", "2 dan 5 digabung menjadi 7."),
                Q("m3", "Benar atau palsu: 6 + 3 = 10?", QuestionType.TrueFalse, ["Benar", "Palsu"], "Palsu", "6 + 3 = 9, bukan 10."),
            ]));

        AddLesson(mathSjkc, mathAdd, publisher, "addition-zh", "10 以内的加法",
            "学习把两个数相加，使总和不超过 10。",
            [
                ("加法是把两组物品合起来求总数。加号是 +。", LessonBlockType.Text),
                ("例子：3 + 4。从 3 开始，向前数 4 步：4、5、6、7。所以 3 + 4 = 7。", LessonBlockType.WorkedExample),
            ],
            quiz: ("加法小测验", ActivityType.Quiz,
            [
                Q("zc1", "3 + 4 等于多少？", QuestionType.Numeric, [], "7", "从 3 向前数 4 步。"),
            ]));

        AddLesson(mathSjkt, mathAdd, publisher, "addition-ta", "10 வரை கூட்டல்",
            "இரண்டு எண்களை கூட்டி, கூட்டுத்தொகை 10ஐ தாண்டாதபடி கற்போம்.",
            [
                ("கூட்டல் என்பது இரு குழுக்களை இணைத்து மொத்தத்தைக் கண்டறிவது. கூட்டல் குறி +.", LessonBlockType.Text),
                ("எடுத்துக்காட்டு: 3 + 4. 3இல் தொடங்கி 4 படிகள் முன்னேறவும்: 4, 5, 6, 7. எனவே 3 + 4 = 7.", LessonBlockType.WorkedExample),
            ],
            quiz: ("கூட்டல் வினாடி வினா", ActivityType.Quiz,
            [
                Q("tc1", "3 + 4 = ?", QuestionType.Numeric, [], "7", "3இல் இருந்து 4 படிகள் முன்னேறவும்."),
            ]));

        AddLesson(mathDlp, mathPlace, publisher, "place-value-dlp", "Place Value to 100 (DLP)",
            "Understand tens and ones for numbers up to 100, taught in English under the DLP.",
            [
                ("A two-digit number is made of tens and ones. In 42, the 4 means four tens (40) and the 2 means two ones.", LessonBlockType.Text),
                ("Worked example: 57 = 5 tens + 7 ones = 50 + 7. The tens digit tells you how many groups of ten.", LessonBlockType.WorkedExample),
                ("Tip: line numbers up by place value when you compare them.", LessonBlockType.Callout),
            ],
            quiz: ("Place Value Check", ActivityType.Quiz,
            [
                Q("d1", "In 42, what is the value of the digit 4?", QuestionType.MultipleChoice, ["4", "40", "400"], "40", "4 is in the tens place, so it is 40."),
                Q("d2", "How many ones are in 57?", QuestionType.Numeric, [], "7", "The ones digit of 57 is 7."),
            ]));

        // ---- Primary Year 1 English ----
        var eng1 = BuildSubject(kssr, "ENG-T1", "English Language", "Year 1", Level.Primary, 2);
        var engStd = AddStandard(eng1, "1.1.1", "Listening & Speaking", "Recognise and produce the short vowel sounds a, e, i, o, u.", MasteryBand.TP3);
        var engVariant = AddVariant(eng1, SchoolType.SK, Language.EN, DlpMode.None, "English SK");
        AddLesson(engVariant, engStd, publisher, "short-vowel-sounds", "Short Vowel Sounds",
            "Listen for and say the five short vowel sounds in simple words.",
            [
                ("Vowels are the letters a, e, i, o and u. Each has a short sound, like the 'a' in cat.", LessonBlockType.Text),
                ("Say these words slowly and listen for the vowel: cat (a), pen (e), pig (i), dog (o), sun (u).", LessonBlockType.WorkedExample),
            ],
            quiz: ("Vowel Quiz", ActivityType.Quiz,
            [
                Q("e1", "Which word has the short 'i' sound?", QuestionType.MultipleChoice, ["cat", "pig", "dog"], "pig", "'pig' has the short i sound."),
                Q("e2", "True or false: 'sun' has the short 'u' sound.", QuestionType.TrueFalse, ["True", "False"], "True", "The u in sun is a short vowel."),
            ]));

        // ---- Lower secondary Form 1 Science (+ DLP variant) ----
        var sci1 = BuildSubject(kssmLower, "SAINS-F1", "Sains", "Form 1", Level.LowerSecondary, 1);
        var sciStd = AddStandard(sci1, "3.1.1", "Sel dan Organisma", "Menerangkan sel sebagai unit asas hidupan.", MasteryBand.TP4);
        var sciBm = AddVariant(sci1, SchoolType.SMK, Language.BM, DlpMode.Bilingual, "Sains F1 (BM)");
        var sciDlp = AddVariant(sci1, SchoolType.SMK, Language.EN, DlpMode.DlpSubjectVariant, "Science F1 (DLP, EN)");

        AddLesson(sciBm, sciStd, publisher, "sel-unit-asas", "Sel: Unit Asas Hidupan",
            "Memahami bahawa sel ialah binaan asas semua benda hidup.",
            [
                ("Semua benda hidup terdiri daripada sel. Sel ialah unit terkecil yang boleh menjalankan proses hidup.", LessonBlockType.Text),
                ("Contoh: sel haiwan mempunyai membran sel, sitoplasma dan nukleus. Sel tumbuhan turut mempunyai dinding sel.", LessonBlockType.WorkedExample),
                ("Petua: ingat nukleus sebagai 'pusat kawalan' sel.", LessonBlockType.Callout),
            ],
            quiz: ("Kuiz Sel", ActivityType.Quiz,
            [
                Q("s1", "Apakah unit asas hidupan?", QuestionType.MultipleChoice, ["Sel", "Organ", "Tisu"], "Sel", "Sel ialah unit terkecil hidupan."),
                Q("s2", "Benar atau palsu: sel tumbuhan mempunyai dinding sel.", QuestionType.TrueFalse, ["Benar", "Palsu"], "Benar", "Dinding sel hadir pada sel tumbuhan."),
            ]));

        AddLesson(sciDlp, sciStd, publisher, "cell-basic-unit-dlp", "The Cell: Basic Unit of Life (DLP)",
            "Understand that the cell is the basic building block of all living things.",
            [
                ("All living things are made of cells. A cell is the smallest unit that can carry out life processes.", LessonBlockType.Text),
                ("Example: an animal cell has a cell membrane, cytoplasm and a nucleus. Plant cells also have a cell wall.", LessonBlockType.WorkedExample),
            ],
            quiz: ("Cell Check", ActivityType.Quiz,
            [
                Q("cd1", "What is the basic unit of life?", QuestionType.MultipleChoice, ["Cell", "Organ", "Tissue"], "Cell", "The cell is the smallest unit of life."),
            ]));

        // ---- Upper secondary Form 4 Mathematics (+ DLP) ----
        var kssmUpper = new CurriculumVersion { Code = "KSSM-F4-2017", Name = "KSSM Tingkatan 4 (2017)", Level = Level.UpperSecondary, EffectiveYear = 2017 };
        _db.CurriculumVersions.Add(kssmUpper);
        var math4 = BuildSubject(kssmUpper, "MATE-F4", "Matematik", "Form 4", Level.UpperSecondary, 1);
        var quadStd = AddStandard(math4, "2.1.1", "Fungsi Kuadratik", "Menyelesaikan persamaan kuadratik dengan pemfaktoran.", MasteryBand.TP5);
        var math4Bm = AddVariant(math4, SchoolType.SMK, Language.BM, DlpMode.None, "Matematik F4 (BM)");
        var math4Dlp = AddVariant(math4, SchoolType.SMK, Language.EN, DlpMode.DlpSubjectVariant, "Mathematics F4 (DLP, EN)");

        AddLesson(math4Bm, quadStd, publisher, "persamaan-kuadratik", "Menyelesaikan Persamaan Kuadratik",
            "Selesaikan persamaan kuadratik mudah dengan kaedah pemfaktoran.",
            [
                ("Persamaan kuadratik berbentuk ax² + bx + c = 0. Satu kaedah penyelesaian ialah pemfaktoran.", LessonBlockType.Text),
                ("Contoh: x² + 5x + 6 = 0 difaktorkan kepada (x + 2)(x + 3) = 0, jadi x = -2 atau x = -3.", LessonBlockType.WorkedExample),
                ("Petua: cari dua nombor yang hasil darabnya c dan hasil tambahnya b.", LessonBlockType.Callout),
            ],
            quiz: ("Kuiz Kuadratik", ActivityType.Quiz,
            [
                Q("q1", "Faktorkan x² + 5x + 6.", QuestionType.MultipleChoice, ["(x+2)(x+3)", "(x+1)(x+6)", "(x+2)(x+4)"], "(x+2)(x+3)", "2×3=6 dan 2+3=5."),
                Q("q2", "Salah satu punca bagi (x+2)(x+3)=0 ialah?", QuestionType.MultipleChoice, ["x=-2", "x=2", "x=3"], "x=-2", "x+2=0 memberi x=-2."),
            ]));

        AddLesson(math4Dlp, quadStd, publisher, "quadratic-equations-dlp", "Solving Quadratic Equations (DLP)",
            "Solve simple quadratic equations by factorisation, taught in English under the DLP.",
            [
                ("A quadratic equation has the form ax² + bx + c = 0. One method to solve it is factorisation.", LessonBlockType.Text),
                ("Example: x² + 5x + 6 = 0 factorises to (x + 2)(x + 3) = 0, so x = -2 or x = -3.", LessonBlockType.WorkedExample),
            ],
            quiz: ("Quadratic Check", ActivityType.Quiz,
            [
                Q("qd1", "Factorise x² + 5x + 6.", QuestionType.MultipleChoice, ["(x+2)(x+3)", "(x+1)(x+6)", "(x+2)(x+4)"], "(x+2)(x+3)", "2×3=6 and 2+3=5."),
            ]));

        // ---- Billing ----
        var sub = new Subscription
        {
            Household = household, PlanCode = "family-monthly", PlanName = "Family Monthly",
            Status = SubscriptionStatus.Active, Currency = "MYR", AmountCents = 4900, SeatLimit = 4,
            Provider = PaymentProvider.Billplz, ProviderSubscriptionId = "sub_demo_001",
            CurrentPeriodStart = DateTimeOffset.UtcNow.AddDays(-5), CurrentPeriodEnd = DateTimeOffset.UtcNow.AddDays(25),
        };
        _db.Subscriptions.Add(sub);
        var invoice = new Invoice { Subscription = sub, Number = "INV-2026-0001", Status = InvoiceStatus.Paid, Currency = "MYR", AmountCents = 4900, PaidAt = DateTimeOffset.UtcNow.AddDays(-5) };
        _db.Invoices.Add(invoice);
        _db.Payments.Add(new Payment { Subscription = sub, Invoice = invoice, Provider = PaymentProvider.Billplz, ProviderPaymentId = "pay_demo_001", Status = PaymentStatus.Succeeded, Currency = "MYR", AmountCents = 4900, ProcessedAt = DateTimeOffset.UtcNow.AddDays(-5) });

        await _db.SaveChangesAsync(ct);

        // ---- Index published lessons into the RAG corpus, then create progress + a sample tutor chat. ----
        await _indexer.ReindexAllAsync(ct);
        await SeedProgressAsync(aisyah, skLesson, ct);
        await SeedSampleTutorConversationAsync(aisyah, mathSk, skLesson, ct);

        _log.LogInformation("Seed complete.");
    }

    private User NewUser(Organization org, Household? household, string email, string name, UserRole role, string password) => new()
    {
        Organization = org,
        Household = household,
        Email = email,
        FullName = name,
        Role = role,
        EmailConfirmed = true,
        PasswordHash = _hasher.Hash(password),
    };

    private Subject BuildSubject(CurriculumVersion cv, string code, string name, string gradeBand, Level level, int sort)
    {
        var s = new Subject { CurriculumVersion = cv, Code = code, Name = name, GradeBand = gradeBand, Level = level, SortOrder = sort };
        _db.Subjects.Add(s);
        return s;
    }

    private LearningStandard AddStandard(Subject subject, string code, string strand, string description, MasteryBand band)
    {
        var std = new LearningStandard { Subject = subject, Code = code, Strand = strand, Description = description, TargetBand = band };
        _db.LearningStandards.Add(std);
        return std;
    }

    private SubjectVariant AddVariant(Subject subject, SchoolType school, Language lang, DlpMode dlp, string label)
    {
        var v = new SubjectVariant { Subject = subject, SchoolType = school, Language = lang, DlpMode = dlp, State = PublishState.Published, Label = label };
        _db.SubjectVariants.Add(v);
        return v;
    }

    private Lesson AddLesson(
        SubjectVariant variant, LearningStandard standard, User publisher,
        string slug, string title, string summary,
        (string Markdown, LessonBlockType Type)[] blocks,
        (string Title, ActivityType Type, object[] Questions) quiz)
    {
        var lesson = new Lesson
        {
            SubjectVariant = variant, LearningStandard = standard, Slug = slug, Title = title, Summary = summary,
            EstimatedMinutes = 20, State = PublishState.Published, PublishedByUserId = publisher.Id, PublishedAt = DateTimeOffset.UtcNow,
        };
        var order = 0;
        foreach (var (md, type) in blocks)
        {
            lesson.Blocks.Add(new LessonBlock { Type = type, SortOrder = order++, Markdown = md });
        }
        lesson.Activities.Add(new Activity
        {
            Title = quiz.Title,
            Type = quiz.Type,
            MaxScore = quiz.Questions.Length,
            PassThresholdPercent = 50,
            State = PublishState.Published,
            QuestionsJson = JsonSerializer.Serialize(quiz.Questions),
        });
        _db.Lessons.Add(lesson);
        return lesson;
    }

    /// <summary>A graded question stored in Activity.QuestionsJson (server-side answer key).</summary>
    private static object Q(string id, string prompt, QuestionType type, string[] options, string correct, string explanation) => new
    {
        id,
        prompt,
        type = type.ToString(),
        options,
        points = 1,
        hint = (string?)null,
        correctAnswer = correct,
        explanation,
    };

    private async Task SeedProgressAsync(Student student, Lesson lesson, CancellationToken ct)
    {
        var pr = new ProgressRecord
        {
            Student = student,
            Lesson = lesson,
            SubjectId = lesson.SubjectVariant.SubjectId,
            Completed = true,
            CompletedAt = DateTimeOffset.UtcNow.AddDays(-1),
            MasteryScore = 72,
            TahapPenguasaan = MasteryMath.ToBand(72),
            TimeSpentSeconds = 900,
            AttemptCount = 2,
            LastActivityAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        _db.ProgressRecords.Add(pr);
        _db.Badges.Add(new Badge { Student = student, Code = "first-lesson", Name = "First Lesson Complete", Icon = "🎯" });
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>A sample tutor conversation grounded in the SK math lesson, with real citations.</summary>
    private async Task SeedSampleTutorConversationAsync(Student student, SubjectVariant variant, Lesson lesson, CancellationToken ct)
    {
        var subject = await _db.Subjects.Include(s => s.CurriculumVersion).FirstAsync(s => s.Id == variant.SubjectId, ct);
        var chunk = await _db.EmbeddingChunks.Where(c => c.LessonId == lesson.Id).OrderBy(c => c.ChunkIndex).FirstOrDefaultAsync(ct);

        var session = new TutorSession
        {
            Student = student,
            SubjectVariant = variant,
            LessonId = lesson.Id,
            Title = "Bantuan penambahan",
            CurriculumVersionCode = subject.CurriculumVersion.Code,
            SchoolType = variant.SchoolType,
            Language = variant.Language,
            DlpMode = variant.DlpMode,
        };

        session.Messages.Add(new TutorMessage { Role = TutorMessageRole.User, Content = "Macam mana nak kira 3 + 4?" });

        var assistant = new TutorMessage
        {
            Role = TutorMessageRole.Assistant,
            Content = "Bagus, jom kita selesaikan ini bersama!\n\nUntuk mengira **3 + 4**, mula dengan nombor 3, kemudian bilang 4 langkah ke hadapan: 4, 5, 6, 7. Jadi **3 + 4 = 7** [1].\n\nCuba pula 2 + 5 sendiri ya! 🙂",
            MasterySignal = MasteryBand.TP4,
            ModelUsed = "mock-tutor-v1",
        };
        if (chunk is not null)
        {
            assistant.Citations.Add(new Citation
            {
                EmbeddingChunkId = chunk.Id,
                LessonId = lesson.Id,
                LessonTitle = lesson.Title,
                Snippet = chunk.Content.Length > 180 ? chunk.Content[..180] + "…" : chunk.Content,
                Score = 0.82,
                Ordinal = 1,
            });
        }
        session.Messages.Add(assistant);

        _db.TutorSessions.Add(session);
        await _db.SaveChangesAsync(ct);
    }
}
