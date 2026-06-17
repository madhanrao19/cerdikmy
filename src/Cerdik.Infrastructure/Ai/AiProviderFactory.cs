using Cerdik.Application.Abstractions;
using Cerdik.Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Cerdik.Infrastructure.Ai;

/// <summary>Resolves the configured <see cref="IAiProvider"/> by name. Defaults to the value of
/// Ai:Provider but callers may override per-request.</summary>
public sealed class AiProviderFactory : IAiProviderFactory
{
    private readonly IServiceProvider _sp;
    private readonly string _default;

    public AiProviderFactory(IServiceProvider sp, IOptions<AiOptions> opt)
    {
        _sp = sp;
        _default = opt.Value.Provider;
    }

    public IAiProvider Resolve(string? providerName = null)
    {
        var name = (providerName ?? _default).Trim().ToLowerInvariant();
        return name switch
        {
            "openai" => _sp.GetRequiredKeyedService<IAiProvider>("openai"),
            "azureopenai" or "azure" => _sp.GetRequiredKeyedService<IAiProvider>("azureopenai"),
            "anthropic" => _sp.GetRequiredKeyedService<IAiProvider>("anthropic"),
            _ => _sp.GetRequiredKeyedService<IAiProvider>("mock"),
        };
    }
}
