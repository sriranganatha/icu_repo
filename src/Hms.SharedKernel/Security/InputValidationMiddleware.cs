using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace Hms.SharedKernel.Security;

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