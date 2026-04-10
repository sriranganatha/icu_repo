using System.Diagnostics;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.UiUx;

/// <summary>
/// UI/UX agent — validates generated Razor Pages for WCAG 2.1 AA accessibility,
/// responsive design patterns, Bootstrap best practices, and scaffolds reusable
/// UI components (data tables, forms, navigation, modals) for all HMS modules.
/// </summary>
public sealed class UiUxAgent : IAgent
{
    private readonly ILlmProvider _llm;
    private readonly ILogger<UiUxAgent> _logger;

    public AgentType Type => AgentType.UiUx;
    public string Name => "UI/UX Agent";
    public string Description => "Accessibility (WCAG), responsive validation, and reusable UI component scaffolding.";

    public UiUxAgent(ILlmProvider llm, ILogger<UiUxAgent> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;
        _logger.LogInformation("UiUxAgent starting");

        var artifacts = new List<CodeArtifact>();
        var findings = new List<ReviewFinding>();

        try
        {
            var outputPath = context.OutputBasePath;

            // ── Step 1: Scan Razor/HTML files for accessibility issues ──
            var razorFiles = Directory.Exists(outputPath)
                ? Directory.GetFiles(outputPath, "*.cshtml", SearchOption.AllDirectories)
                    .Where(f => !f.Contains("obj") && !f.Contains("bin")).ToArray()
                : [];

            foreach (var file in razorFiles)
            {
                ct.ThrowIfCancellationRequested();
                var content = await File.ReadAllTextAsync(file, ct);
                var relPath = Path.GetRelativePath(outputPath, file);

                // WCAG checks
                CheckAccessibility(content, relPath, findings);
            }

            // ── Step 2: Generate shared UI component library ──
            var components = new (string Name, string Desc)[]
            {
                ("DataTable", "Responsive data table with sorting, pagination, search, and ARIA labels"),
                ("FormSection", "Accessible form layout with validation messages and field groups"),
                ("PatientCard", "Patient summary card with demographics, photo placeholder, and quick actions"),
                ("NavigationMenu", "Side navigation with role-based menu items and keyboard navigation"),
                ("AlertBanner", "Dismissible alert banner with severity levels and screen reader announcements"),
                ("ConfirmModal", "Confirmation modal dialog with focus trap and ARIA attributes"),
                ("StatusBadge", "Color-coded status badge component with semantic meaning"),
                ("LoadingSpinner", "Accessible loading spinner with aria-busy and status announcements"),
            };

            foreach (var (name, desc) in components)
            {
                ct.ThrowIfCancellationRequested();
                var prompt = $"""
                    Generate a Razor Page partial view (_Components/{name}.cshtml) for a .NET 8 Hospital Management System.
                    Component: {desc}
                    
                    Requirements:
                    - Bootstrap 5.3 classes
                    - WCAG 2.1 AA compliant (proper ARIA attributes, keyboard navigation, focus management)
                    - Responsive (mobile-first)
                    - Use tag helpers where appropriate
                    - Include @model parameter class if needed
                    - Clean, semantic HTML5
                    
                    Return ONLY the Razor markup, no explanations.
                    """;

                var markup = await _llm.GenerateAsync(prompt, ct);
                markup = markup.Replace("```html", "").Replace("```cshtml", "")
                    .Replace("```razor", "").Replace("```", "").Trim();

                artifacts.Add(new CodeArtifact
                {
                    Layer = ArtifactLayer.RazorPage,
                    RelativePath = $"src/GNex.SharedKernel/Pages/Shared/_Components/{name}.cshtml",
                    FileName = $"{name}.cshtml",
                    Namespace = "GNex.SharedKernel.Pages",
                    ProducedBy = Type,
                    TracedRequirementIds = ["NFR-UIX-01"],
                    Content = markup
                });
            }

            // ── Step 3: Generate CSS theme with accessibility variables ──
            var cssPrompt = """
                Generate a CSS file (hms-theme.css) for a Hospital Management System with:
                - CSS custom properties for colors, spacing, typography
                - Light and dark theme support via prefers-color-scheme and [data-theme] attribute
                - WCAG AA contrast ratios for all text/background combinations
                - Focus-visible styles for keyboard navigation
                - Skip-to-content link styles
                - Reduced motion media query support
                - Print stylesheet section
                - Variables: --hms-primary, --hms-success, --hms-danger, --hms-warning, --hms-info
                - Healthcare-appropriate color palette (calming blues/greens)
                
                Return ONLY the CSS, no explanations.
                """;
            var css = await _llm.GenerateAsync(cssPrompt, ct);
            css = css.Replace("```css", "").Replace("```", "").Trim();
            artifacts.Add(new CodeArtifact
            {
                Layer = ArtifactLayer.RazorPage,
                RelativePath = "src/GNex.SharedKernel/wwwroot/css/hms-theme.css",
                FileName = "hms-theme.css",
                Namespace = string.Empty,
                ProducedBy = Type,
                TracedRequirementIds = ["NFR-UIX-02"],
                Content = css
            });

            // ── Step 4: Generate _Layout.cshtml with accessibility ──
            var layoutPrompt = """
                Generate a _Layout.cshtml for a .NET 8 Hospital Management System.
                Include:
                - Skip-to-content link
                - Semantic HTML5 landmarks (header, nav, main, footer)
                - Bootstrap 5.3 navbar with role-based menu structure
                - aria-label on navigation regions
                - Dark mode toggle button
                - Breadcrumb navigation with aria-label
                - Toast notification container with aria-live="polite"
                - @RenderBody() and @RenderSection("Scripts", required: false)
                - Reference to hms-theme.css
                - lang attribute on html tag
                
                Return ONLY the Razor markup.
                """;
            var layout = await _llm.GenerateAsync(layoutPrompt, ct);
            layout = layout.Replace("```html", "").Replace("```cshtml", "")
                .Replace("```razor", "").Replace("```", "").Trim();
            artifacts.Add(new CodeArtifact
            {
                Layer = ArtifactLayer.RazorPage,
                RelativePath = "src/GNex.SharedKernel/Pages/Shared/_Layout.cshtml",
                FileName = "_Layout.cshtml",
                Namespace = "GNex.SharedKernel.Pages",
                ProducedBy = Type,
                TracedRequirementIds = ["NFR-UIX-03"],
                Content = layout
            });

            context.Artifacts.AddRange(artifacts);
            context.Findings.AddRange(findings);
            context.AgentStatuses[Type] = AgentStatus.Completed;

            // Agent completes its own claimed work items
            foreach (var item in context.CurrentClaimedItems)
                context.CompleteWorkItem?.Invoke(item);

            return new AgentResult
            {
                Agent = Type, Success = true,
                Summary = $"UI/UX Agent: {artifacts.Count} artifacts ({components.Length} components, theme, layout), {findings.Count} accessibility findings",
                Artifacts = artifacts, Findings = findings, Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            context.AgentStatuses[Type] = AgentStatus.Failed;
            _logger.LogError(ex, "UiUxAgent failed");
            return new AgentResult { Agent = Type, Success = false, Errors = [ex.Message], Duration = sw.Elapsed };
        }
    }

    private static void CheckAccessibility(string content, string relPath, List<ReviewFinding> findings)
    {
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineNum = i + 1;

            // Images without alt
            if (line.Contains("<img") && !line.Contains("alt="))
                findings.Add(A11yFinding(relPath, lineNum, "Image missing alt attribute.", "Add descriptive alt text or alt=\"\" for decorative images."));

            // Form inputs without labels
            if (line.Contains("<input") && !line.Contains("aria-label") && !line.Contains("id="))
                findings.Add(A11yFinding(relPath, lineNum, "Form input may lack associated label.", "Add <label for=\"...\"> or aria-label attribute."));

            // Buttons without accessible text
            if (line.Contains("<button") && !line.Contains("aria-label") && line.Contains("/>"))
                findings.Add(A11yFinding(relPath, lineNum, "Button without accessible text.", "Add aria-label or visible text content."));

            // Missing lang attribute
            if (line.Contains("<html") && !line.Contains("lang="))
                findings.Add(A11yFinding(relPath, lineNum, "HTML element missing lang attribute.", "Add lang=\"en\" to <html> element."));

            // onclick without keyboard equivalent
            if (line.Contains("onclick=") && !line.Contains("onkeydown") && !line.Contains("onkeypress") && !line.Contains("role=\"button\""))
                findings.Add(A11yFinding(relPath, lineNum, "onclick handler may not be keyboard accessible.", "Add role=\"button\" tabindex=\"0\" and keyboard handler, or use <button>."));

            // Color-only indicators
            if (line.Contains("color:") && line.Contains("red") && !line.Contains("aria-") && !line.Contains("title="))
                findings.Add(A11yFinding(relPath, lineNum, "Color may be sole indicator of information.", "Add text label or icon alongside color indicator."));
        }
    }

    private static ReviewFinding A11yFinding(string file, int line, string msg, string fix) => new()
    {
        FilePath = file, LineNumber = line,
        Severity = ReviewSeverity.Warning,
        Category = "Accessibility-WCAG",
        Message = msg, Suggestion = fix
    };
}
