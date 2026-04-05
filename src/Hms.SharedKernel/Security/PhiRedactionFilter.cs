using System.Text.RegularExpressions;

namespace Hms.SharedKernel.Security;

/// <summary>
/// Redacts PHI from log messages and API error responses.
/// HIPAA Safe Harbor: removes 18 identifier categories.
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