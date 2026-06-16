namespace Cerdik.Infrastructure.Options;

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public string AccessSecret { get; set; } = default!;
    public string RefreshSecret { get; set; } = default!;
    public string Issuer { get; set; } = "cerdikmy";
    public string Audience { get; set; } = "cerdikmy-clients";
    public int AccessTtlMinutes { get; set; } = 15;
    public int RefreshTtlDays { get; set; } = 30;
    public string CookieDomain { get; set; } = "localhost";
}

public sealed class StorageOptions
{
    public const string Section = "Storage";
    /// <summary>s3 | azure</summary>
    public string Provider { get; set; } = "s3";
    public string S3Endpoint { get; set; } = "http://localhost:9000";
    public string S3Region { get; set; } = "ap-southeast-1";
    public string S3AccessKey { get; set; } = "minioadmin";
    public string S3SecretKey { get; set; } = "minioadmin";
    public string Bucket { get; set; } = "cerdik-media";
    public bool ForcePathStyle { get; set; } = true; // required for MinIO
    public string AzureConnectionString { get; set; } = string.Empty;
}

public sealed class AiOptions
{
    public const string Section = "Ai";
    /// <summary>openai | azureopenai | anthropic | mock</summary>
    public string Provider { get; set; } = "mock";
    public string OpenAiApiKey { get; set; } = string.Empty;
    public string OpenAiChatModel { get; set; } = "gpt-4o-mini";
    public string OpenAiEmbedModel { get; set; } = "text-embedding-3-small";
    public string AzureEndpoint { get; set; } = string.Empty;
    public string AzureApiKey { get; set; } = string.Empty;
    public string AzureDeployment { get; set; } = string.Empty;
    public string AnthropicApiKey { get; set; } = string.Empty;
    public string AnthropicChatModel { get; set; } = "claude-sonnet-4-6";
    public int EmbeddingDimensions { get; set; } = 384;
}

public sealed class PaymentOptions
{
    public const string Section = "Payments";
    /// <summary>billplz | curlec | stripe</summary>
    public string Provider { get; set; } = "billplz";
    public string BillplzApiKey { get; set; } = string.Empty;
    public string BillplzCollectionId { get; set; } = string.Empty;
    public string CurlecKeyId { get; set; } = string.Empty;
    public string CurlecKeySecret { get; set; } = string.Empty;
    public string StripeSecretKey { get; set; } = string.Empty;
    public string StripeWebhookSecret { get; set; } = string.Empty;
}

public sealed class MailOptions
{
    public const string Section = "Mail";
    public string From { get; set; } = "cerdikMY <no-reply@cerdik.my>";
    public string SmtpUrl { get; set; } = "smtp://localhost:1025";
}
