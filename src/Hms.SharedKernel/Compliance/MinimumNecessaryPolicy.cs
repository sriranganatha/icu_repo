Here is a complete C# implementation that enforces the HIPAA Minimum Necessary standard. 

To make this generic and reusable across your application, this solution uses a custom `[Phi]` attribute to classify properties into categories (Demographic, Clinical, Financial). The `FilterFields<T>` method uses reflection to evaluate these attributes against the user's role and nullifies unauthorized fields.

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Hms.SharedKernel.Compliance
{
    /// <summary>
    /// Classifies the type of Protected Health Information (PHI).
    /// </summary>
    public enum PhiCategory
    {
        Demographic,
        Clinical,
        Financial
    }

    /// <summary>
    /// Attribute used to tag properties with their respective PHI category.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class PhiAttribute : Attribute
    {
        public PhiCategory Category { get; }

        public PhiAttribute(PhiCategory category)
        {
            Category = category;
        }
    }

    /// <summary>
    /// Enforces the HIPAA Minimum Necessary standard by filtering entity fields based on role access.
    /// </summary>
    public static class MinimumNecessaryPolicy
    {
        /// <summary>
        /// Nullifies PHI fields on the entity that the specified role is not authorized to view.
        /// WARNING: This mutates the passed entity. Do not save the mutated entity back to the database.
        /// </summary>
        /// <typeparam name="T">The type of the entity.</typeparam>
        /// <param name="entity">The entity containing PHI.</param>
        /// <param name="role">The role of the user requesting access.</param>
        /// <returns>The filtered entity.</returns>
        public static T FilterFields<T>(T entity, string role) where T : class
        {
            if (entity == null) return null;

            var allowedCategories = GetAllowedCategories(role);

            // If the role has access to everything, bypass reflection for performance
            if (allowedCategories.Count == Enum.GetValues(typeof(PhiCategory)).Length)
            {
                return entity;
            }

            var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                      .Where(p => p.CanWrite);

            foreach (var prop in properties)
            {
                var phiAttribute = prop.GetCustomAttribute<PhiAttribute>();
                
                // If the property is tagged as PHI and the role doesn't have the required category access
                if (phiAttribute != null && !allowedCategories.Contains(phiAttribute.Category))
                {
                    // Determine the safe default value (null for reference types/Nullable<T>, default for value types)
                    object defaultValue = prop.PropertyType.IsValueType 
                        ? Activator.CreateInstance(prop.PropertyType) 
                        : null;

                    prop.SetValue(entity, defaultValue);
                }
            }

            return entity;
        }

        /// <summary>
        /// Maps roles to their minimum necessary PHI categories.
        /// </summary>
        private static HashSet<PhiCategory> GetAllowedCategories(string role)
        {
            var normalizedRole = role?.Trim().ToLowerInvariant() ?? string.Empty;

            return normalizedRole switch
            {
                // Physicians need full access to treat the patient
                "physician" => new HashSet<PhiCategory> 
                { 
                    PhiCategory.Demographic, 
                    PhiCategory.Clinical, 
                    PhiCategory.Financial 
                },
                
                // Nurses need clinical data and demographics to identify the patient, but no financial data
                "nurse" => new HashSet<PhiCategory> 
                { 
                    PhiCategory.Demographic, 
                    PhiCategory.Clinical 
                },
                
                // Billing needs financial data and demographics to process claims, but no clinical notes
                "billing" => new HashSet<PhiCategory> 
                { 
                    PhiCategory.Demographic, 
                    PhiCategory.Financial 
                },
                
                // Admins/Clerks only need demographics for scheduling and registration
                "admin" => new HashSet<PhiCategory> 
                { 
                    PhiCategory.Demographic 
                },
                
                // Default fallback: Deny all PHI access
                _ => new HashSet<PhiCategory>()
            };
        }
    }
}
```

### Example Usage

Here is how you would apply this to an entity in your application:

```csharp
using Hms.SharedKernel.Compliance;

public class PatientRecord
{
    public Guid Id { get; set; } // Non-PHI, always visible

    [Phi(PhiCategory.Demographic)]
    public string PatientName { get; set; }

    [Phi(PhiCategory.Demographic)]
    public DateTime? DateOfBirth { get; set; }

    [Phi(PhiCategory.Clinical)]
    public string DiagnosisCodes { get; set; }

    [Phi(PhiCategory.Clinical)]
    public string PhysicianNotes { get; set; }

    [Phi(PhiCategory.Financial)]
    public string SocialSecurityNumber { get; set; }

    [Phi(PhiCategory.Financial)]
    public string InsurancePolicyNumber { get; set; }
}

// --- In your Application Service / API Controller ---

var patient = _repository.GetPatient(patientId);
var userRole = _currentUser.Role; // e.g., "Nurse"

// Enforce Minimum Necessary Standard before returning to the client
var redactedPatient = MinimumNecessaryPolicy.FilterFields(patient, userRole);

// If userRole is "Nurse":
// - PatientName: Visible
// - DiagnosisCodes: Visible
// - SocialSecurityNumber: null
// - InsurancePolicyNumber: null
```

### Important HIPAA Compliance Notes:
1. **Mutation Warning:** The `FilterFields` method mutates the object in memory. Ensure you call this **right before serialization** (e.g., returning an API response) and **never** call `SaveChanges()` on your DbContext after running this filter, or you will accidentally erase PHI from your database.
2. **Nullable Types:** For best results, ensure value-type PHI fields (like `DateTime` or `int`) are made nullable (e.g., `DateTime?`). If they are not nullable, the reflection code will set them to their default value (e.g., `01/01/0001`), which could be misinterpreted by front-end clients. Null is the safest indicator of redacted data.