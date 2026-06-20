namespace Cerdik.Application.Email;

/// <summary>Original, brand-aligned HTML email bodies. Kept dependency-free and simple so they render
/// in any mail client and are easy for safety/comms review.</summary>
public static class EmailTemplates
{
    private const string Accent = "#14b8a6";
    private const string Ink = "#0f172a";
    private const string Muted = "#64748b";

    public static (string Subject, string Html) PasswordReset(string resetUrl, int ttlMinutes)
    {
        var html = Wrap("Reset your password", $$"""
            <p>We received a request to reset your cerdikMY password.</p>
            <p style="margin:24px 0;">
              <a href="{{resetUrl}}" style="background:{{Accent}};color:#fff;text-decoration:none;
                 padding:12px 22px;border-radius:10px;font-weight:700;display:inline-block;">Reset password →</a>
            </p>
            <p style="color:{{Muted}};font-size:13px;">This link expires in {{ttlMinutes}} minutes and can be used once.
            If you didn't request this, you can safely ignore this email — your password won't change.</p>
            <p style="color:{{Muted}};font-size:12px;word-break:break-all;">Or paste this link: {{resetUrl}}</p>
            """);
        return ("Reset your cerdikMY password", html);
    }

    public static (string Subject, string Html) Welcome(string name)
    {
        var html = Wrap("Welcome to cerdikMY", $$"""
            <p>Hi {{name}}, welcome to <strong>cerdikMY</strong> — your family's KPM-aligned learning home.</p>
            <p>Next steps:</p>
            <ul style="color:{{Ink}};">
              <li>Add your children and pick their level, school type and language.</li>
              <li>Set a weekly study plan.</li>
              <li>Let them learn with Cikgu AI — safely, with citations from approved lessons.</li>
            </ul>
            """);
        return ("Welcome to cerdikMY 🎓", html);
    }

    public static (string Subject, string Html) PrivacyRequestReceived(string kind)
    {
        var html = Wrap("Request received", $$"""
            <p>We've received your data <strong>{{kind}}</strong> request and started processing it.</p>
            <p style="color:{{Muted}};font-size:13px;">You'll be able to track its status from your account.
            For export requests, a download link will be available once it's ready.</p>
            """);
        return ($"Your cerdikMY data {kind} request", html);
    }

    private static string Wrap(string heading, string body) => $$"""
        <div style="font-family:'Public Sans',system-ui,sans-serif;max-width:560px;margin:0 auto;padding:24px;color:{{Ink}};">
          <div style="font-size:20px;font-weight:800;margin-bottom:4px;">cerdik<span style="color:{{Accent}};">MY</span></div>
          <h1 style="font-size:18px;margin:16px 0 8px;">{{heading}}</h1>
          {{body}}
          <hr style="border:none;border-top:1px solid #e8edf6;margin:24px 0;">
          <p style="color:{{Muted}};font-size:12px;">cerdikMY · Malaysian Homeschooling Portal</p>
        </div>
        """;
}
