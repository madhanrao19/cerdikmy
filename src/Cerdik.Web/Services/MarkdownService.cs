using Markdig;
using Microsoft.AspNetCore.Components;

namespace Cerdik.Web.Services;

/// <summary>
/// Thin wrapper over Markdig used to render tutor answers and lesson text
/// blocks to HTML. Markdig HTML-encodes raw HTML by default (DisableHtml),
/// so untrusted model/content output cannot inject markup.
/// </summary>
public sealed class MarkdownService
{
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseSoftlineBreakAsHardlineBreak()
        .DisableHtml()
        .Build();

    /// <summary>Renders markdown to an HTML string.</summary>
    public string ToHtml(string? markdown)
        => string.IsNullOrWhiteSpace(markdown) ? string.Empty : Markdown.ToHtml(markdown, _pipeline);

    /// <summary>Renders markdown to a <see cref="MarkupString"/> for direct binding.</summary>
    public MarkupString ToMarkup(string? markdown) => new(ToHtml(markdown));
}
