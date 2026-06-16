using Cerdik.Application.Dtos;
using Cerdik.Domain;

namespace Cerdik.Web.Services;

/// <summary>
/// Scoped (per SignalR circuit) holder of the authenticated user's state.
/// Wraps <see cref="ApiClient"/> for login/logout and exposes the current
/// <see cref="MeResponse"/>, role, students and feature flags to components.
/// </summary>
public sealed class AuthStateService
{
    private readonly ApiClient _api;

    public AuthStateService(ApiClient api) => _api = api;

    public MeResponse? Me { get; private set; }
    public UserDto? User => Me?.User;
    public bool IsAuthenticated => Me is not null;
    public UserRole? Role => Me?.User.Role;
    public IReadOnlyList<StudentSummaryDto> Students => Me?.Students ?? Array.Empty<StudentSummaryDto>();
    public IReadOnlyDictionary<string, bool> Features => Me?.Features ?? new Dictionary<string, bool>();

    /// <summary>Raised whenever the authenticated state changes (login, logout, refresh).</summary>
    public event Action? OnChange;

    /// <summary>Returns true if a named feature flag is present and enabled.</summary>
    public bool HasFeature(string key) => Features.TryGetValue(key, out var on) && on;

    /// <summary>Logs in, stores the bearer token, then loads <c>/me</c>.</summary>
    public async Task<AuthResponse> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var auth = await _api.LoginAsync(new LoginRequest(email, password), ct).ConfigureAwait(false);
        _api.SetAccessToken(auth.AccessToken);
        await RefreshMeAsync(ct).ConfigureAwait(false);
        return auth;
    }

    /// <summary>Registers a new parent household, stores the token, then loads <c>/me</c>.</summary>
    public async Task<AuthResponse> RegisterParentAsync(RegisterParentRequest request, CancellationToken ct = default)
    {
        var auth = await _api.RegisterParentAsync(request, ct).ConfigureAwait(false);
        _api.SetAccessToken(auth.AccessToken);
        await RefreshMeAsync(ct).ConfigureAwait(false);
        return auth;
    }

    /// <summary>Re-loads the current user, students and feature flags from <c>/me</c>.</summary>
    public async Task RefreshMeAsync(CancellationToken ct = default)
    {
        Me = await _api.GetMeAsync(ct).ConfigureAwait(false);
        OnChange?.Invoke();
    }

    /// <summary>
    /// Attempts to silently restore a session via the refresh cookie, then loads
    /// <c>/me</c>. Returns true if a session was restored. Never throws.
    /// </summary>
    public async Task<bool> TryRestoreSessionAsync(CancellationToken ct = default)
    {
        try
        {
            var auth = await _api.RefreshAsync(ct).ConfigureAwait(false);
            _api.SetAccessToken(auth.AccessToken);
            await RefreshMeAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (ApiException)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        try { await _api.LogoutAsync(ct).ConfigureAwait(false); }
        catch (ApiException) { /* clearing local state regardless */ }
        finally
        {
            _api.SetAccessToken(null);
            Me = null;
            OnChange?.Invoke();
        }
    }
}
