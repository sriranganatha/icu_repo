namespace Hms.SharedKernel.Compliance;

/// <summary>
/// HIPAA PHI classification levels for data governance.
/// Applied to entity fields and DTO properties for access control.
/// </summary>
public enum PhiClassification
{
    /// <summary>Non-PHI: facility codes, status codes, system metadata.</summary>
    Public = 0,

    /// <summary>De-identified per Safe Harbor: age ranges, zip-3, dates rounded to year.</summary>
    DeIdentified = 1,

    /// <summary>Limited Data Set: dates, zip codes, city/state (requires DUA).</summary>
    LimitedDataSet = 2,

    /// <summary>Full PHI: names, DOB, SSN, MRN, addresses, contact info.</summary>
    ProtectedHealthInfo = 3,

    /// <summary>Highly sensitive: HIV, substance abuse, mental health, genetic data.</summary>
    HighlySensitive = 4
}

/// <summary>Attribute for marking entity properties with PHI classification.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PhiAttribute : Attribute
{
    public PhiClassification Level { get; }
    public string Category { get; }

    public PhiAttribute(PhiClassification level, string category = "General")
    {
        Level = level;
        Category = category;
    }
}