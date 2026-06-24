using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Cerdik.Application.Dtos;
using Cerdik.Domain;

namespace Cerdik.Web.Services;

/// <summary>
/// Strongly-typed client over the cerdikMY HTTP API. One method per endpoint.
/// All methods return DTOs from <see cref="Cerdik.Application.Dtos"/> and throw
/// <see cref="ApiException"/> on any non-2xx response.
/// </summary>
public sealed class ApiClient
{
    private readonly HttpClient _http;
    private readonly AccessTokenProvider _tokens;

    /// <summary>Shared camelCase, case-insensitive options used by both ApiClient and TutorClient.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ApiClient(HttpClient http, AccessTokenProvider tokens)
    {
        _http = http;
        _tokens = tokens;
    }

    /// <summary>Sets (or clears) the bearer access token for the current circuit.</summary>
    public void SetAccessToken(string? token) => _tokens.Token = token;

    /// <summary>Applies the circuit's current bearer token to this request. Called before every call so
    /// any transient ApiClient instance picks up a token set elsewhere in the same circuit.</summary>
    private void ApplyAuth()
        => _http.DefaultRequestHeaders.Authorization =
            string.IsNullOrEmpty(_tokens.Token) ? null : new AuthenticationHeaderValue("Bearer", _tokens.Token);

    // ---------------------------------------------------------------- Auth
    public Task<AuthResponse> RegisterParentAsync(RegisterParentRequest request, CancellationToken ct = default)
        => PostAsync<RegisterParentRequest, AuthResponse>("/auth/register-parent", request, ct);

    public Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
        => PostAsync<LoginRequest, AuthResponse>("/auth/login", request, ct);

    public Task<AuthResponse> RefreshAsync(CancellationToken ct = default)
        => PostAsync<AuthResponse>("/auth/refresh", ct);

    public Task LogoutAsync(CancellationToken ct = default)
        => PostNoContentAsync("/auth/logout", ct);

    public Task ForgotPasswordAsync(string email, CancellationToken ct = default)
        => PostNoContentAsync("/auth/forgot-password", new ForgotPasswordRequest(email), ct);

    public Task ResetPasswordAsync(string token, string newPassword, CancellationToken ct = default)
        => PostNoContentAsync("/auth/reset-password", new ResetPasswordRequest(token, newPassword), ct);

    public Task<MeResponse> GetMeAsync(CancellationToken ct = default)
        => GetAsync<MeResponse>("/me", ct);

    // ---------------------------------------------------------- Curriculum
    public Task<IReadOnlyList<CurriculumVersionDto>> GetCurriculumVersionsAsync(CancellationToken ct = default)
        => GetAsync<IReadOnlyList<CurriculumVersionDto>>("/curriculum/versions", ct);

    public Task<IReadOnlyList<SchoolProfileDto>> GetSchoolProfilesAsync(CancellationToken ct = default)
        => GetAsync<IReadOnlyList<SchoolProfileDto>>("/school-profiles", ct);

    public Task<IReadOnlyList<SubjectDto>> GetSubjectsAsync(CurriculumFilter filter, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(filter.CurriculumVersionCode))
            query.Add($"curriculumVersionCode={Uri.EscapeDataString(filter.CurriculumVersionCode)}");
        if (filter.Level is { } level) query.Add($"level={level}");
        if (filter.SchoolType is { } st) query.Add($"schoolType={st}");
        if (filter.Language is { } lang) query.Add($"language={lang}");
        if (filter.DlpMode is { } dlp) query.Add($"dlpMode={dlp}");
        var qs = query.Count > 0 ? "?" + string.Join("&", query) : string.Empty;
        return GetAsync<IReadOnlyList<SubjectDto>>($"/subjects{qs}", ct);
    }

    public Task<IReadOnlyList<LearningStandardDto>> GetStandardsAsync(Guid subjectId, CancellationToken ct = default)
        => GetAsync<IReadOnlyList<LearningStandardDto>>($"/subjects/{subjectId}/standards", ct);

    // -------------------------------------------------------------- Lessons
    public Task<LessonDto> GetLessonAsync(Guid lessonId, CancellationToken ct = default)
        => GetAsync<LessonDto>($"/lessons/{lessonId}", ct);

    // ------------------------------------------------------------ Attempts
    /// <summary>
    /// Fetches an activity with its (client-safe) questions. Not in the core
    /// endpoint list but required to render a quiz; the server omits correct
    /// answers from <see cref="QuestionDto"/>.
    /// </summary>
    public Task<ActivityDto> GetActivityAsync(Guid activityId, CancellationToken ct = default)
        => GetAsync<ActivityDto>($"/activities/{activityId}", ct);

    public Task<AttemptDto> StartActivityAsync(Guid activityId, Guid studentId, CancellationToken ct = default)
        => PostAsync<StartActivityRequest, AttemptDto>($"/activities/{activityId}/start", new StartActivityRequest(studentId), ct);

    public Task<AttemptResultDto> SubmitAttemptAsync(Guid attemptId, IReadOnlyDictionary<string, string> answers, CancellationToken ct = default)
        => PostAsync<SubmitAttemptRequest, AttemptResultDto>($"/attempts/{attemptId}/submit", new SubmitAttemptRequest(answers), ct);

    // ------------------------------------------------------------ Progress
    public Task<ProgressDto> GetStudentProgressAsync(Guid studentId, CancellationToken ct = default)
        => GetAsync<ProgressDto>($"/students/{studentId}/progress", ct);

    public Task<SubjectStandardsMasteryDto> GetStandardsMasteryAsync(Guid studentId, Guid subjectId, CancellationToken ct = default)
        => GetAsync<SubjectStandardsMasteryDto>($"/students/{studentId}/subjects/{subjectId}/standards-mastery", ct);

    public Task<StudentStreakDto> GetStreakAsync(Guid studentId, CancellationToken ct = default)
        => GetAsync<StudentStreakDto>($"/students/{studentId}/streak", ct);

    public Task<IReadOnlyList<ReviewItemDto>> GetReviewsAsync(Guid studentId, CancellationToken ct = default)
        => GetAsync<IReadOnlyList<ReviewItemDto>>($"/students/{studentId}/reviews", ct);

    public Task<StudentInsightsDto> GetInsightsAsync(Guid studentId, CancellationToken ct = default)
        => GetAsync<StudentInsightsDto>($"/students/{studentId}/insights", ct);

    public Task<IReadOnlyList<LessonRecommendationDto>> GetRecommendationsAsync(Guid studentId, int limit = 6, CancellationToken ct = default)
        => GetAsync<IReadOnlyList<LessonRecommendationDto>>($"/students/{studentId}/recommendations?limit={limit}", ct);

    public Task<PlacementTestDto> GetPlacementAsync(Guid studentId, Guid subjectId, CancellationToken ct = default)
        => GetAsync<PlacementTestDto>($"/students/{studentId}/subjects/{subjectId}/placement", ct);

    public Task<PlacementResultDto> SubmitPlacementAsync(Guid studentId, Guid subjectId, IReadOnlyDictionary<string, string> answers, CancellationToken ct = default)
        => PostAsync<PlacementSubmitRequest, PlacementResultDto>($"/students/{studentId}/subjects/{subjectId}/placement", new PlacementSubmitRequest(answers), ct);

    // ---------------------------------------------------------- Mock exams
    public Task<ExamStartDto> StartExamAsync(Guid studentId, Guid subjectId, CancellationToken ct = default)
        => PostAsync<ExamStartDto>($"/students/{studentId}/subjects/{subjectId}/exam/start", ct);

    public Task<ExamResultDto> SubmitExamAsync(Guid studentId, Guid examId, IReadOnlyDictionary<string, string> answers, int elapsedSeconds, CancellationToken ct = default)
        => PostAsync<ExamSubmitRequest, ExamResultDto>($"/students/{studentId}/exam/{examId}/submit", new ExamSubmitRequest(answers, elapsedSeconds), ct);

    public Task<IReadOnlyList<ExamHistoryItemDto>> GetExamHistoryAsync(Guid studentId, CancellationToken ct = default)
        => GetAsync<IReadOnlyList<ExamHistoryItemDto>>($"/students/{studentId}/exams", ct);

    // -------------------------------------------------------------- Parent
    public Task<ParentDashboardDto> GetParentDashboardAsync(CancellationToken ct = default)
        => GetAsync<ParentDashboardDto>("/parents/dashboard", ct);

    public Task<StudyPlanDto> CreateStudyPlanAsync(StudyPlanRequest request, CancellationToken ct = default)
        => PostAsync<StudyPlanRequest, StudyPlanDto>("/parents/study-plans", request, ct);

    public Task<IReadOnlyList<TutorSessionSummaryDto>> GetChildTutorSessionsAsync(Guid studentId, CancellationToken ct = default)
        => GetAsync<IReadOnlyList<TutorSessionSummaryDto>>($"/parents/students/{studentId}/tutor-sessions", ct);

    public Task<TutorSessionDto> GetTutorTranscriptAsync(Guid sessionId, CancellationToken ct = default)
        => GetAsync<TutorSessionDto>($"/parents/tutor-sessions/{sessionId}", ct);

    // --------------------------------------------------------------- Tutor
    public Task<TutorSessionDto> CreateTutorSessionAsync(CreateTutorSessionRequest request, CancellationToken ct = default)
        => PostAsync<CreateTutorSessionRequest, TutorSessionDto>("/tutor/sessions", request, ct);

    /// <summary>Non-streaming tutor reply. Use <see cref="TutorClient"/> for the SSE stream.</summary>
    public Task<TutorReplyDto> SendTutorMessageAsync(Guid sessionId, string content, CancellationToken ct = default)
        => PostAsync<SendTutorMessageRequest, TutorReplyDto>($"/tutor/sessions/{sessionId}/messages", new SendTutorMessageRequest(content), ct);

    // --------------------------------------------------------------- Admin
    public Task<IReadOnlyList<AdminUserDto>> GetAdminUsersAsync(CancellationToken ct = default)
        => GetAsync<IReadOnlyList<AdminUserDto>>("/admin/users", ct);

    public Task<AdminUserDto> CreateAdminUserAsync(CreateAdminUserRequest request, CancellationToken ct = default)
        => PostAsync<CreateAdminUserRequest, AdminUserDto>("/admin/users", request, ct);

    public Task<IReadOnlyList<AdminContentItemDto>> GetAdminContentAsync(CancellationToken ct = default)
        => GetAsync<IReadOnlyList<AdminContentItemDto>>("/admin/content", ct);

    public Task ImportContentAsync(ImportContentRequest request, CancellationToken ct = default)
        => PostNoContentAsync("/admin/content/import", request, ct);

    public Task PublishContentAsync(Guid lessonId, bool publish, CancellationToken ct = default)
        => PostNoContentAsync("/admin/content/publish", new PublishContentRequest(lessonId, publish), ct);

    public Task<CohortAnalyticsDto> GetCohortAnalyticsAsync(CancellationToken ct = default)
        => GetAsync<CohortAnalyticsDto>("/analytics/cohorts", ct);

    public Task<IReadOnlyList<ModerationQueueItemDto>> GetModerationQueueAsync(CancellationToken ct = default)
        => GetAsync<IReadOnlyList<ModerationQueueItemDto>>("/admin/moderation", ct);

    public Task ReviewModerationAsync(Guid eventId, ModerationDecision decision, string? notes, CancellationToken ct = default)
        => PostNoContentAsync("/admin/moderation/review", new ReviewModerationRequest(eventId, decision, notes), ct);

    public Task<IReadOnlyList<WebhookLogDto>> GetPaymentsAsync(CancellationToken ct = default)
        => GetAsync<IReadOnlyList<WebhookLogDto>>("/admin/payments", ct);

    // --------------------------------------------------------------- Media
    public Task<IReadOnlyList<MediaAssetDto>> GetMediaAsync(CancellationToken ct = default)
        => GetAsync<IReadOnlyList<MediaAssetDto>>("/admin/media", ct);

    public async Task<MediaAssetDto> UploadMediaAsync(Stream content, string fileName, string contentType, string? altText, CancellationToken ct = default)
    {
        ApplyAuth();
        using var form = new MultipartFormDataContent();
        var fileContent = new StreamContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);
        if (!string.IsNullOrWhiteSpace(altText))
        {
            form.Add(new StringContent(altText), "altText");
        }
        using var response = await _http.PostAsync("/admin/media", form, ct).ConfigureAwait(false);
        return await ReadAsync<MediaAssetDto>(response, ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------- Billing
    /// <summary>Available subscription plans for the billing plan cards.</summary>
    public Task<IReadOnlyList<BillingPlanDto>> GetBillingPlansAsync(CancellationToken ct = default)
        => GetAsync<IReadOnlyList<BillingPlanDto>>("/billing/plans", ct);

    public Task<CheckoutSessionDto> CreateCheckoutSessionAsync(CheckoutSessionRequest request, CancellationToken ct = default)
        => PostAsync<CheckoutSessionRequest, CheckoutSessionDto>("/billing/checkout-session", request, ct);

    // ------------------------------------------------------------- Privacy
    public Task<PrivacyRequestDto> RequestPrivacyExportAsync(Guid? studentId, CancellationToken ct = default)
        => PostAsync<PrivacyExportRequest, PrivacyRequestDto>("/privacy/export", new PrivacyExportRequest(studentId), ct);

    public Task<PrivacyRequestDto> RequestPrivacyDeleteAsync(Guid? studentId, string? reason, CancellationToken ct = default)
        => PostAsync<PrivacyDeleteRequest, PrivacyRequestDto>("/privacy/delete-request", new PrivacyDeleteRequest(studentId, reason), ct);

    // ------------------------------------------------------- HTTP plumbing
    private async Task<T> GetAsync<T>(string url, CancellationToken ct)
    {
        ApplyAuth();
        using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        return await ReadAsync<T>(response, ct).ConfigureAwait(false);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string url, TRequest body, CancellationToken ct)
    {
        ApplyAuth();
        using var response = await _http.PostAsJsonAsync(url, body, JsonOptions, ct).ConfigureAwait(false);
        return await ReadAsync<TResponse>(response, ct).ConfigureAwait(false);
    }

    private async Task<TResponse> PostAsync<TResponse>(string url, CancellationToken ct)
    {
        ApplyAuth();
        using var response = await _http.PostAsync(url, content: null, ct).ConfigureAwait(false);
        return await ReadAsync<TResponse>(response, ct).ConfigureAwait(false);
    }

    private async Task PostNoContentAsync(string url, CancellationToken ct)
    {
        ApplyAuth();
        using var response = await _http.PostAsync(url, content: null, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    private async Task PostNoContentAsync<TRequest>(string url, TRequest body, CancellationToken ct)
    {
        ApplyAuth();
        using var response = await _http.PostAsJsonAsync(url, body, JsonOptions, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false);
        return result ?? throw new ApiException(response.StatusCode, "The API returned an empty response body.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        string? body = null;
        try { body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false); }
        catch { /* best effort */ }
        var reason = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "You are not signed in or your session has expired.",
            HttpStatusCode.Forbidden => "You do not have permission to perform this action.",
            HttpStatusCode.NotFound => "The requested resource was not found.",
            _ => $"The request failed ({(int)response.StatusCode} {response.ReasonPhrase}).",
        };
        throw new ApiException(response.StatusCode, reason, body);
    }
}
