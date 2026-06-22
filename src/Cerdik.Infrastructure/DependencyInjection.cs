using Cerdik.Application.Abstractions;
using Cerdik.Application.Features;
using Cerdik.Domain;
using Cerdik.Infrastructure.Ai;
using Cerdik.Infrastructure.Auth;
using Cerdik.Infrastructure.Common;
using Cerdik.Infrastructure.Email;
using Cerdik.Infrastructure.Jobs;
using Cerdik.Infrastructure.Options;
using Cerdik.Infrastructure.Payments;
using Cerdik.Infrastructure.Persistence;
using Cerdik.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Cerdik.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers EF Core, auth, storage, AI, payments, jobs and feature flags.
    /// Reads flat env-style keys (DATABASE_URL, JWT_*, S3_*, AI_*…) and maps them to typed options.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        BindOptions(services, config);

        var connectionString = config["DATABASE_URL"]
            ?? config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("DATABASE_URL / ConnectionStrings:Default is not configured.");

        services.AddDbContext<AppDbContext>(opt =>
            opt.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null)));

        // Core services
        services.AddHttpContextAccessor();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<Observability.AiMetrics>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();

        // Feature flags (defaults + FEATURE_FLAGS overrides)
        services.AddSingleton<IFeatureFlags>(_ =>
            new FeatureFlags(FeatureFlags.Parse(config["FEATURE_FLAGS"])));

        AddStorage(services, config);
        AddAi(services);
        AddPayments(services);

        // RAG + jobs
        services.AddScoped<IVectorRetriever, VectorRetriever>();
        services.AddScoped<ContentIndexer>();
        services.AddScoped<BackgroundJobs>();
        services.AddScoped<DemoDataSeeder>();
        services.AddScoped<AdminBootstrapper>();

        return services;
    }

    private static void BindOptions(IServiceCollection services, IConfiguration config)
    {
        services.Configure<JwtOptions>(o =>
        {
            o.AccessSecret = config["JWT_ACCESS_SECRET"] ?? o.AccessSecret;
            o.RefreshSecret = config["JWT_REFRESH_SECRET"] ?? o.RefreshSecret;
            o.Issuer = config["JWT_ISSUER"] ?? o.Issuer;
            o.Audience = config["JWT_AUDIENCE"] ?? o.Audience;
            o.CookieDomain = config["SESSION_COOKIE_DOMAIN"] ?? o.CookieDomain;
            if (int.TryParse(config["JWT_ACCESS_TTL_MINUTES"], out var a)) o.AccessTtlMinutes = a;
            if (int.TryParse(config["JWT_REFRESH_TTL_DAYS"], out var r)) o.RefreshTtlDays = r;
        });

        services.Configure<StorageOptions>(o =>
        {
            o.Provider = config["STORAGE_PROVIDER"] ?? o.Provider;
            o.S3Endpoint = config["S3_ENDPOINT"] ?? o.S3Endpoint;
            o.S3Region = config["S3_REGION"] ?? o.S3Region;
            o.S3AccessKey = config["S3_ACCESS_KEY"] ?? o.S3AccessKey;
            o.S3SecretKey = config["S3_SECRET_KEY"] ?? o.S3SecretKey;
            o.Bucket = config["S3_BUCKET"] ?? o.Bucket;
            o.AzureConnectionString = config["AZURE_STORAGE_CONNECTION_STRING"] ?? o.AzureConnectionString;
        });

        services.Configure<AiOptions>(o =>
        {
            o.Provider = config["AI_PROVIDER"] ?? o.Provider;
            o.OpenAiApiKey = config["OPENAI_API_KEY"] ?? o.OpenAiApiKey;
            o.OpenAiChatModel = config["OPENAI_CHAT_MODEL"] ?? o.OpenAiChatModel;
            o.OpenAiEmbedModel = config["OPENAI_EMBED_MODEL"] ?? o.OpenAiEmbedModel;
            o.AzureEndpoint = config["AZURE_OPENAI_ENDPOINT"] ?? o.AzureEndpoint;
            o.AzureApiKey = config["AZURE_OPENAI_API_KEY"] ?? o.AzureApiKey;
            o.AzureDeployment = config["AZURE_OPENAI_DEPLOYMENT"] ?? o.AzureDeployment;
            o.AnthropicApiKey = config["ANTHROPIC_API_KEY"] ?? o.AnthropicApiKey;
            o.AnthropicChatModel = config["ANTHROPIC_CHAT_MODEL"] ?? o.AnthropicChatModel;
        });

        services.Configure<PaymentOptions>(o =>
        {
            o.Provider = config["PAYMENT_PROVIDER"] ?? o.Provider;
            o.BillplzApiKey = config["BILLPLZ_API_KEY"] ?? o.BillplzApiKey;
            o.BillplzCollectionId = config["BILLPLZ_COLLECTION_ID"] ?? o.BillplzCollectionId;
            o.CurlecKeyId = config["CURLEC_KEY_ID"] ?? o.CurlecKeyId;
            o.CurlecKeySecret = config["CURLEC_KEY_SECRET"] ?? o.CurlecKeySecret;
            o.StripeSecretKey = config["STRIPE_SECRET_KEY"] ?? o.StripeSecretKey;
            o.StripeWebhookSecret = config["STRIPE_WEBHOOK_SECRET"] ?? o.StripeWebhookSecret;
        });

        services.Configure<MailOptions>(o =>
        {
            o.From = config["MAIL_FROM"] ?? o.From;
            o.SmtpUrl = config["SMTP_URL"] ?? o.SmtpUrl;
        });
    }

    private static void AddStorage(IServiceCollection services, IConfiguration config)
    {
        var provider = (config["STORAGE_PROVIDER"] ?? "s3").ToLowerInvariant();
        if (provider == "azure")
        {
            services.AddSingleton<IStorageService, AzureBlobStorageService>();
        }
        else
        {
            services.AddSingleton<IStorageService, S3StorageService>();
        }
    }

    private static void AddAi(IServiceCollection services)
    {
        // Keyed registrations so the factory can resolve a provider by name.
        services.AddKeyedSingleton<IAiProvider, MockAiProvider>("mock");
        services.AddHttpClient();

        services.AddKeyedScoped<IAiProvider>("openai", (sp, _) =>
            new OpenAiProvider(sp.GetRequiredService<IHttpClientFactory>().CreateClient("openai"),
                sp.GetRequiredService<IOptions<AiOptions>>()));
        services.AddKeyedScoped<IAiProvider>("azureopenai", (sp, _) =>
            new OpenAiProvider(sp.GetRequiredService<IHttpClientFactory>().CreateClient("azure"),
                sp.GetRequiredService<IOptions<AiOptions>>(), azure: true));
        services.AddKeyedScoped<IAiProvider>("anthropic", (sp, _) =>
            new AnthropicProvider(sp.GetRequiredService<IHttpClientFactory>().CreateClient("anthropic"),
                sp.GetRequiredService<IOptions<AiOptions>>()));

        services.AddScoped<IAiProviderFactory, AiProviderFactory>();
        services.AddScoped<IModerationService, ModerationService>();

        // Embedding provider: real OpenAI/Azure if configured, else the offline local hashing embedder.
        services.AddScoped<IEmbeddingProvider>(sp =>
        {
            var opt = sp.GetRequiredService<IOptions<AiOptions>>();
            var v = opt.Value;
            return v.Provider.ToLowerInvariant() switch
            {
                "openai" when !string.IsNullOrWhiteSpace(v.OpenAiApiKey) =>
                    new OpenAiEmbeddingProvider(sp.GetRequiredService<IHttpClientFactory>().CreateClient("openai"), opt),
                "azureopenai" when !string.IsNullOrWhiteSpace(v.AzureApiKey) =>
                    new OpenAiEmbeddingProvider(sp.GetRequiredService<IHttpClientFactory>().CreateClient("azure"), opt, azure: true),
                _ => new LocalEmbeddingProvider(opt),
            };
        });
    }

    private static void AddPayments(IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddKeyedScoped<IPaymentProvider>(PaymentProvider.Billplz, (sp, _) =>
            new BillplzPaymentProvider(sp.GetRequiredService<IHttpClientFactory>().CreateClient("billplz"),
                sp.GetRequiredService<IOptions<PaymentOptions>>()));
        services.AddKeyedScoped<IPaymentProvider>(PaymentProvider.Curlec, (sp, _) =>
            new CurlecPaymentProvider(sp.GetRequiredService<IHttpClientFactory>().CreateClient("curlec"),
                sp.GetRequiredService<IOptions<PaymentOptions>>()));
        services.AddKeyedScoped<IPaymentProvider>(PaymentProvider.Stripe, (sp, _) =>
            new StripePaymentProvider(sp.GetRequiredService<IHttpClientFactory>().CreateClient("stripe"),
                sp.GetRequiredService<IOptions<PaymentOptions>>()));
        services.AddScoped<IPaymentProviderFactory, PaymentProviderFactory>();
    }
}
