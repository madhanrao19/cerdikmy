using Cerdik.Domain;

namespace Cerdik.Application.Dtos;

public sealed record RegisterParentRequest(string Email, string Password, string FullName, string HouseholdName, string PreferredLanguage);
public sealed record RegisterStudentRequest(Guid HouseholdId, string DisplayName, string? Email, string? Password, Level Level, SchoolType SchoolType, Language PrimaryLanguage, DlpMode DlpMode, DateOnly? DateOfBirth);
public sealed record LoginRequest(string Email, string Password);
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Token, string NewPassword);

/// <summary>Returned in the response body; tokens are also set as httpOnly cookies.</summary>
public sealed record AuthResponse(UserDto User, string AccessToken, DateTimeOffset AccessExpiresAt);

public sealed record UserDto(
    Guid Id,
    string Email,
    string? FullName,
    UserRole Role,
    Guid? HouseholdId,
    Guid? StudentId,
    Guid OrganizationId);

public sealed record MeResponse(UserDto User, IReadOnlyList<StudentSummaryDto> Students, IReadOnlyDictionary<string, bool> Features);

public sealed record StudentSummaryDto(
    Guid Id,
    string DisplayName,
    string? Avatar,
    Level Level,
    SchoolType SchoolType,
    Language PrimaryLanguage,
    DlpMode DlpMode,
    int Points);
