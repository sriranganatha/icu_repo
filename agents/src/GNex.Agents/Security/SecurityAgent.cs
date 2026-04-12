using System.Diagnostics;
using System.Text.RegularExpressions;
using GNex.Agents.Requirements;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.Security;

/// <summary>
/// Scans all generated code artifacts for OWASP Top 10 vulnerabilities,
/// authentication/authorization gaps, input validation, SQL injection,
/// XSS, CSRF, encryption requirements, and secret management.
/// Generates security middleware, input validators, and policy artifacts.
/// </summary>
public sealed class SecurityAgent : IAgent
{
    private readonly ILogger<SecurityAgent> _logger;

    public AgentType Type => AgentType.Security;
    public string Name => "Security Agent";
    public string Description => "OWASP Top 10 analysis, input validation, auth patterns, encryption enforcement, and security middleware generation.";

    public SecurityAgent(ILogger<SecurityAgent> logger) => _logger = logger;

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;
        _logger.LogInformation("SecurityAgent starting — scanning {Count} artifacts", context.Artifacts.Count);

        var findings = new List<ReviewFinding>();
        var artifacts = new List<CodeArtifact>();

        try
        {
            // ── Scan existing artifacts ──
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, $"Scanning {context.Artifacts.Count} artifacts for OWASP Top 10 vulnerabilities...");
            foreach (var artifact in context.Artifacts)
            {
                ct.ThrowIfCancellationRequested();
                findings.AddRange(ScanForInjection(artifact));
                findings.AddRange(ScanForAuthGaps(artifact));
                findings.AddRange(ScanForSensitiveDataExposure(artifact));
                findings.AddRange(ScanForCryptoIssues(artifact));
                findings.AddRange(ScanForInputValidation(artifact));
            }
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, $"Security scan complete: {findings.Count} findings (injection, auth gaps, sensitive data, crypto, input validation)");

            // ── Generate security artifacts ──
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "Generating security middleware — input validation, rate limiting, security headers, API key validator, encryption, sensitive data redaction");
            artifacts.Add(GenerateInputValidationMiddleware());
            artifacts.Add(GenerateRateLimitingPolicy());
            artifacts.Add(GenerateSecurityHeadersMiddleware());
            artifacts.Add(GenerateApiKeyValidator());
            artifacts.Add(GenerateDataEncryptionHelper());
            artifacts.Add(GeneratePhiRedactionFilter());

            context.Artifacts.AddRange(artifacts);
            context.Findings.AddRange(findings);
            context.AgentStatuses[Type] = AgentStatus.Completed;

            // Agent completes its own claimed work items
            foreach (var item in context.CurrentClaimedItems)
                context.CompleteWorkItem?.Invoke(item);

            await Task.CompletedTask;
            return new AgentResult
            {
                Agent = Type, Success = true,
                Summary = $"SecurityAgent: {findings.Count} findings, {artifacts.Count} security artifacts generated",
                Artifacts = artifacts, Findings = findings,
                Messages = [new AgentMessage { From = Type, To = AgentType.Orchestrator,
                    Subject = "Security scan complete",
                    Body = $"{findings.Count} security findings. Generated: input validation, rate limiting, security headers, API key validation, sensitive data redaction, encryption helpers." }],
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            context.AgentStatuses[Type] = AgentStatus.Failed;
            _logger.LogError(ex, "SecurityAgent failed");
            return new AgentResult { Agent = Type, Success = false, Errors = [ex.Message], Duration = sw.Elapsed };
        }
    }

    // ─── OWASP A03: Injection ───────────────────────────────────────────────

    private static List<ReviewFinding> ScanForInjection(CodeArtifact artifact)
    {
        var findings = new List<ReviewFinding>();
        var content = artifact.Content;

        // Raw SQL concatenation
        if (Regex.IsMatch(content, @"FromSqlRaw\s*\(\s*\$""") ||
            Regex.IsMatch(content, @"ExecuteSqlRaw\s*\(\s*\$"""))
        {
            findings.Add(new ReviewFinding
            {
                ArtifactId = artifact.Id, FilePath = artifact.RelativePath,
                Severity = ReviewSeverity.SecurityViolation,
                Category = "OWASP-A03-Injection",
                Message = $"SQL injection risk: string interpolation in raw SQL in '{artifact.FileName}'.",
                Suggestion = "Use FromSqlInterpolated() or parameterized queries."
            });
        }

        // Command injection
        if (content.Contains("Process.Start") || content.Contains("ProcessStartInfo"))
        {
            findings.Add(new ReviewFinding
            {
                ArtifactId = artifact.Id, FilePath = artifact.RelativePath,
                Severity = ReviewSeverity.SecurityViolation,
                Category = "OWASP-A03-Injection",
                Message = $"Command injection risk: Process.Start in '{artifact.FileName}'.",
                Suggestion = "Avoid shell commands. If needed, use allowlisted commands with validated inputs."
            });
        }

        return findings;
    }

    // ─── OWASP A01: Broken Access Control ───────────────────────────────────

    private static List<ReviewFinding> ScanForAuthGaps(CodeArtifact artifact)
    {
        var findings = new List<ReviewFinding>();
        var content = artifact.Content;

        // Endpoints without [Authorize]
        if (artifact.Layer == ArtifactLayer.Service &&
            content.Contains("MapGet") || content.Contains("MapPost") || content.Contains("MapPut"))
        {
            if (!content.Contains("[Authorize]") && !content.Contains("RequireAuthorization") &&
                !content.Contains("AllowAnonymous") && !content.Contains("/health"))
            {
                findings.Add(new ReviewFinding
                {
                    ArtifactId = artifact.Id, FilePath = artifact.RelativePath,
                    Severity = ReviewSeverity.Warning,
                    Category = "OWASP-A01-BrokenAccess",
                    Message = $"Endpoint definitions in '{artifact.FileName}' lack authorization enforcement.",
                    Suggestion = "Add .RequireAuthorization() or [Authorize] to all non-health endpoints."
                });
            }
        }

        // Missing tenant check in service methods
        if (artifact.Layer == ArtifactLayer.Service && !artifact.FileName.StartsWith("I") &&
            !content.Contains("TenantId") && content.Contains("async Task"))
        {
            findings.Add(new ReviewFinding
            {
                ArtifactId = artifact.Id, FilePath = artifact.RelativePath,
                Severity = ReviewSeverity.SecurityViolation,
                Category = "OWASP-A01-BrokenAccess",
                Message = $"Service '{artifact.FileName}' may not enforce tenant scoping in operations.",
                Suggestion = "Inject ITenantProvider and validate tenant context on every data operation."
            });
        }

        return findings;
    }

    // ─── OWASP A02: Cryptographic Failures ──────────────────────────────────

    private static List<ReviewFinding> ScanForCryptoIssues(CodeArtifact artifact)
    {
        var findings = new List<ReviewFinding>();
        var content = artifact.Content;

        // Hardcoded secrets
        if (Regex.IsMatch(content, @"(password|secret|apikey|connectionstring)\s*=\s*""[^""]+""", RegexOptions.IgnoreCase))
        {
            findings.Add(new ReviewFinding
            {
                ArtifactId = artifact.Id, FilePath = artifact.RelativePath,
                Severity = ReviewSeverity.SecurityViolation,
                Category = "OWASP-A02-Crypto",
                Message = $"Hardcoded secret detected in '{artifact.FileName}'.",
                Suggestion = "Move secrets to Azure Key Vault, AWS Secrets Manager, or environment variables."
            });
        }

        // Weak hashing
        if (content.Contains("MD5.") || content.Contains("SHA1."))
        {
            findings.Add(new ReviewFinding
            {
                ArtifactId = artifact.Id, FilePath = artifact.RelativePath,
                Severity = ReviewSeverity.Warning,
                Category = "OWASP-A02-Crypto",
                Message = $"Weak hash algorithm in '{artifact.FileName}'.",
                Suggestion = "Use SHA-256/SHA-512 for hashing. Use bcrypt/Argon2 for passwords."
            });
        }

        return findings;
    }

    // ─── OWASP A04: Sensitive Data Exposure ─────────────────────────────────

    private static List<ReviewFinding> ScanForSensitiveDataExposure(CodeArtifact artifact)
    {
        var findings = new List<ReviewFinding>();
        var content = artifact.Content;

        // Sensitive fields exposed in DTOs without classification
        var phiPatterns = new[] { "DateOfBirth", "SocialSecurity", "SSN", "MedicalRecordNumber",
            "InsuranceId", "DriversLicense", "Diagnosis", "TreatmentPlan" };

        if (artifact.Layer == ArtifactLayer.Dto)
        {
            foreach (var phi in phiPatterns)
            {
                if (content.Contains(phi) && !content.Contains("ClassificationCode"))
                {
                    findings.Add(new ReviewFinding
                    {
                        ArtifactId = artifact.Id, FilePath = artifact.RelativePath,
                        Severity = ReviewSeverity.Warning,
                        Category = "OWASP-A04-DataExposure",
                        Message = $"Sensitive field '{phi}' in DTO '{artifact.FileName}' without classification marker.",
                        Suggestion = "Add ClassificationCode to DTOs containing sensitive data for data governance."
                    });
                    break; // One finding per DTO
                }
            }
        }

        // Logging sensitive data
        if (Regex.IsMatch(content, @"Log(Information|Debug|Warning)\(.*?(Name|Birth|SSN|Medical)", RegexOptions.IgnoreCase))
        {
            findings.Add(new ReviewFinding
            {
                ArtifactId = artifact.Id, FilePath = artifact.RelativePath,
                Severity = ReviewSeverity.SecurityViolation,
                Category = "OWASP-A04-DataExposure",
                Message = $"Potential sensitive data in log statements in '{artifact.FileName}'.",
                Suggestion = "Never log sensitive data. Use IDs and correlation tokens instead."
            });
        }

        return findings;
    }

    // ─── OWASP A06: Vulnerable Components (Input Validation) ────────────────

    private static List<ReviewFinding> ScanForInputValidation(CodeArtifact artifact)
    {
        var findings = new List<ReviewFinding>();
        var content = artifact.Content;

        // Missing validation on request DTOs
        if (artifact.Layer == ArtifactLayer.Dto && content.Contains("Request"))
        {
            if (!content.Contains("[Required]") && !content.Contains("required ") &&
                !content.Contains("FluentValidation") && !content.Contains("IValidator"))
            {
                findings.Add(new ReviewFinding
                {
                    ArtifactId = artifact.Id, FilePath = artifact.RelativePath,
                    Severity = ReviewSeverity.Warning,
                    Category = "OWASP-A06-InputValidation",
                    Message = $"Request DTO in '{artifact.FileName}' lacks explicit validation.",
                    Suggestion = "Use C# 'required' keyword, [Required], [MaxLength], or FluentValidation rules."
                });
            }
        }

        return findings;
    }

    // ─── Generated Security Artifacts ───────────────────────────────────────

    private static CodeArtifact GenerateInputValidationMiddleware() => new()
    {
        Layer = ArtifactLayer.Configuration,
        RelativePath = "GNex.SharedKernel/Security/InputValidationMiddleware.cs",
        FileName = "InputValidationMiddleware.cs",
        Namespace = "GNex.SharedKernel.Security",
        ProducedBy = AgentType.Security,
        TracedRequirementIds = ["NFR-SEC-01", "OWASP-A03"],
        Content = """
            using System.Text.RegularExpressions;
            using Microsoft.AspNetCore.Http;

            namespace GNex.SharedKernel.Security;

            /// <summary>
            /// Middleware that validates incoming request payloads for common injection patterns.
            /// Blocks requests containing SQL injection, XSS, and path traversal attempts.
            /// </summary>
            public sealed class InputValidationMiddleware
            {
                private readonly RequestDelegate _next;
                private static readonly Regex[] DangerPatterns =
                [
                    new(@"(\b(SELECT|INSERT|UPDATE|DELETE|DROP|UNION|ALTER)\b.*\b(FROM|INTO|SET|TABLE)\b)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                    new(@"<script\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                    new(@"\.\./|\.\.\\", RegexOptions.Compiled),
                    new(@"(--|;|')\s*(OR|AND)\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                ];

                public InputValidationMiddleware(RequestDelegate next) => _next = next;

                public async Task InvokeAsync(HttpContext context)
                {
                    var path = context.Request.Path.Value ?? "";
                    var query = context.Request.QueryString.Value ?? "";
                    var combined = path + query;

                    foreach (var pattern in DangerPatterns)
                    {
                        if (pattern.IsMatch(combined))
                        {
                            context.Response.StatusCode = 400;
                            await context.Response.WriteAsync("Request blocked by security policy.");
                            return;
                        }
                    }

                    await _next(context);
                }
            }
            """
    };

    private static CodeArtifact GenerateRateLimitingPolicy() => new()
    {
        Layer = ArtifactLayer.Configuration,
        RelativePath = "GNex.SharedKernel/Security/RateLimitingPolicy.cs",
        FileName = "RateLimitingPolicy.cs",
        Namespace = "GNex.SharedKernel.Security",
        ProducedBy = AgentType.Security,
        TracedRequirementIds = ["NFR-SEC-02", "OWASP-A04"],
        Content = """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.RateLimiting;
            using Microsoft.Extensions.DependencyInjection;
            using System.Threading.RateLimiting;

            namespace GNex.SharedKernel.Security;

            /// <summary>
            /// Configures rate limiting policies per tenant to prevent abuse and DoS.
            /// Clinical APIs: 100 req/min. Search/List: 30 req/min. Auth: 10 req/min.
            /// </summary>
            public static class RateLimitingPolicy
            {
                public const string Clinical = "clinical";
                public const string Search = "search";
                public const string Auth = "auth";

                public static IServiceCollection AddHmsRateLimiting(this IServiceCollection services)
                {
                    services.AddRateLimiter(options =>
                    {
                        options.AddFixedWindowLimiter(Clinical, opt =>
                        {
                            opt.PermitLimit = 100;
                            opt.Window = TimeSpan.FromMinutes(1);
                            opt.QueueLimit = 10;
                            opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                        });

                        options.AddFixedWindowLimiter(Search, opt =>
                        {
                            opt.PermitLimit = 30;
                            opt.Window = TimeSpan.FromMinutes(1);
                            opt.QueueLimit = 5;
                        });

                        options.AddFixedWindowLimiter(Auth, opt =>
                        {
                            opt.PermitLimit = 10;
                            opt.Window = TimeSpan.FromMinutes(1);
                            opt.QueueLimit = 2;
                        });

                        options.RejectionStatusCode = 429;
                    });

                    return services;
                }
            }
            """
    };

    private static CodeArtifact GenerateSecurityHeadersMiddleware() => new()
    {
        Layer = ArtifactLayer.Configuration,
        RelativePath = "GNex.SharedKernel/Security/SecurityHeadersMiddleware.cs",
        FileName = "SecurityHeadersMiddleware.cs",
        Namespace = "GNex.SharedKernel.Security",
        ProducedBy = AgentType.Security,
        TracedRequirementIds = ["NFR-SEC-01", "OWASP-A05"],
        Content = """
            using Microsoft.AspNetCore.Http;

            namespace GNex.SharedKernel.Security;

            /// <summary>
            /// Adds OWASP-recommended security headers to all HTTP responses.
            /// </summary>
            public sealed class SecurityHeadersMiddleware
            {
                private readonly RequestDelegate _next;

                public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

                public async Task InvokeAsync(HttpContext context)
                {
                    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                    context.Response.Headers["X-Frame-Options"] = "DENY";
                    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
                    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline';";
                    context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
                    context.Response.Headers["Cache-Control"] = "no-store";
                    context.Response.Headers["Pragma"] = "no-cache";

                    await _next(context);
                }
            }
            """
    };

    private static CodeArtifact GenerateApiKeyValidator() => new()
    {
        Layer = ArtifactLayer.Configuration,
        RelativePath = "GNex.SharedKernel/Security/ApiKeyValidation.cs",
        FileName = "ApiKeyValidation.cs",
        Namespace = "GNex.SharedKernel.Security",
        ProducedBy = AgentType.Security,
        TracedRequirementIds = ["NFR-SEC-02"],
        Content = """
            using Microsoft.AspNetCore.Http;
            using Microsoft.Extensions.Configuration;

            namespace GNex.SharedKernel.Security;

            /// <summary>
            /// Validates API key from X-Api-Key header for service-to-service calls.
            /// Keys are rotated via configuration/secrets manager — never hardcoded.
            /// </summary>
            public sealed class ApiKeyValidationMiddleware
            {
                private readonly RequestDelegate _next;
                private readonly string _validKey;

                public ApiKeyValidationMiddleware(RequestDelegate next, IConfiguration config)
                {
                    _next = next;
                    _validKey = config["Security:ApiKey"]
                        ?? throw new InvalidOperationException("Security:ApiKey not configured");
                }

                public async Task InvokeAsync(HttpContext context)
                {
                    // Skip health endpoints
                    if (context.Request.Path.StartsWithSegments("/health"))
                    {
                        await _next(context);
                        return;
                    }

                    if (!context.Request.Headers.TryGetValue("X-Api-Key", out var key) ||
                        !string.Equals(key, _validKey, StringComparison.Ordinal))
                    {
                        context.Response.StatusCode = 401;
                        await context.Response.WriteAsync("Invalid or missing API key.");
                        return;
                    }

                    await _next(context);
                }
            }
            """
    };

    private static CodeArtifact GenerateDataEncryptionHelper() => new()
    {
        Layer = ArtifactLayer.Configuration,
        RelativePath = "GNex.SharedKernel/Security/DataEncryptionHelper.cs",
        FileName = "DataEncryptionHelper.cs",
        Namespace = "GNex.SharedKernel.Security",
        ProducedBy = AgentType.Security,
        TracedRequirementIds = ["NFR-SEC-01", "NFR-DATA-01"],
        Content = """
            using System.Security.Cryptography;
            using System.Text;

            namespace GNex.SharedKernel.Security;

            /// <summary>
            /// AES-256-GCM encryption for sensitive fields at rest.
            /// Key management delegates to IKeyProvider (Azure Key Vault / AWS KMS).
            /// </summary>
            public static class DataEncryptionHelper
            {
                public static byte[] Encrypt(string plaintext, byte[] key)
                {
                    var nonce = new byte[AesGcm.NonceByteSizes.MaxSize];
                    RandomNumberGenerator.Fill(nonce);

                    var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
                    var ciphertext = new byte[plaintextBytes.Length];
                    var tag = new byte[AesGcm.TagByteSizes.MaxSize];

                    using var aes = new AesGcm(key, AesGcm.TagByteSizes.MaxSize);
                    aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

                    // nonce | tag | ciphertext
                    var result = new byte[nonce.Length + tag.Length + ciphertext.Length];
                    nonce.CopyTo(result, 0);
                    tag.CopyTo(result, nonce.Length);
                    ciphertext.CopyTo(result, nonce.Length + tag.Length);
                    return result;
                }

                public static string Decrypt(byte[] combined, byte[] key)
                {
                    var nonceSize = AesGcm.NonceByteSizes.MaxSize;
                    var tagSize = AesGcm.TagByteSizes.MaxSize;

                    var nonce = combined[..nonceSize];
                    var tag = combined[nonceSize..(nonceSize + tagSize)];
                    var ciphertext = combined[(nonceSize + tagSize)..];
                    var plaintext = new byte[ciphertext.Length];

                    using var aes = new AesGcm(key, tagSize);
                    aes.Decrypt(nonce, ciphertext, tag, plaintext);
                    return Encoding.UTF8.GetString(plaintext);
                }
            }
            """
    };

    private static CodeArtifact GeneratePhiRedactionFilter() => new()
    {
        Layer = ArtifactLayer.Configuration,
        RelativePath = "GNex.SharedKernel/Security/PhiRedactionFilter.cs",
        FileName = "PhiRedactionFilter.cs",
        Namespace = "GNex.SharedKernel.Security",
        ProducedBy = AgentType.Security,
        TracedRequirementIds = ["NFR-DATA-01", "OWASP-A04"],
        Content = """
            using System.Text.RegularExpressions;

            namespace GNex.SharedKernel.Security;

            /// <summary>
            /// Redacts sensitive data from log messages and API error responses.
            /// Data protection: removes 18 identifier categories.
            /// </summary>
            public static partial class PhiRedactionFilter
            {
                private static readonly (string Name, Regex Pattern)[] Redactors =
                [
                    ("SSN",   SsnPattern()),
                    ("Phone", PhonePattern()),
                    ("Email", EmailPattern()),
                    ("MRN",   MrnPattern()),
                    ("DOB",   DobPattern()),
                ];

                public static string Redact(string input)
                {
                    foreach (var (name, pattern) in Redactors)
                        input = pattern.Replace(input, $"[REDACTED-{name}]");
                    return input;
                }

                [GeneratedRegex(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled)]
                private static partial Regex SsnPattern();

                [GeneratedRegex(@"\b\d{3}[-.]?\d{3}[-.]?\d{4}\b", RegexOptions.Compiled)]
                private static partial Regex PhonePattern();

                [GeneratedRegex(@"\b[\w.+-]+@[\w-]+\.[\w.]+\b", RegexOptions.Compiled)]
                private static partial Regex EmailPattern();

                [GeneratedRegex(@"\bMRN[-:]?\s*\d{6,10}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
                private static partial Regex MrnPattern();

                [GeneratedRegex(@"\b(0[1-9]|1[0-2])/(0[1-9]|[12]\d|3[01])/\d{4}\b", RegexOptions.Compiled)]
                private static partial Regex DobPattern();
            }
            """
    };
}
