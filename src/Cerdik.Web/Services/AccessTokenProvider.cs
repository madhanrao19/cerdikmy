namespace Cerdik.Web.Services;

/// <summary>Per-circuit (scoped) holder of the current bearer access token. Because typed HttpClients
/// are transient, every <see cref="ApiClient"/>/<see cref="TutorClient"/> instance created within a
/// SignalR circuit reads the token from this single scoped instance — so auth is shared within a user's
/// circuit but never leaks across users. The web app authenticates the API purely via this bearer
/// token (no shared cookie jar).</summary>
public sealed class AccessTokenProvider
{
    public string? Token { get; set; }
}
