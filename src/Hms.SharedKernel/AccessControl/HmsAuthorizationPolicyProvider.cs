Here is a complete, enterprise-grade implementation tailored for a Healthcare Management System (HMS). 

In healthcare applications, Role-Based Access Control (RBAC) is often insufficient due to complex HIPAA/GDPR requirements. This implementation uses **Permission-Based Access Control (PBAC)**. 

By using a custom `IAuthorizationPolicyProvider`, we avoid the overhead of registering hundreds of individual permission policies at startup. Instead, policies are generated dynamically when the `[RequirePermission]` attribute is evaluated.

### Implementation

```csharp
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hms.SharedKernel.AccessControl
{
    /// <summary>
    /// Defines the granular permissions for the Healthcare Management System.
    /// </summary>
    public enum HmsPermission
    {
        None = 0,
        
        // Patient Data
        ViewPatientDemographics = 100,
        EditPatientDemographics = 101,
        ViewClinicalRecords = 102,
        EditClinicalRecords = 103,
        
        // Clinical Actions
        PrescribeMedication = 200,
        OrderLabTests = 201,
        
        // Administrative & Auditing (HIPAA requirements)
        ViewAuditLogs = 300,
        ManageUserRoles = 301,
        OverrideBreakTheGlass = 302
    }

    /// <summary>
    /// Custom attribute to enforce granular HMS permissions on controllers and actions.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public class RequirePermissionAttribute : AuthorizeAttribute
    {
        internal const string PolicyPrefix = "HMS_PERM_";

        public RequirePermissionAttribute(HmsPermission permission)
        {
            // Maps the enum to a string policy name (e.g., "HMS_PERM_ViewClinicalRecords")
            Policy = $"{PolicyPrefix}{permission}";
        }
    }

    /// <summary>
    /// The authorization requirement that holds the requested permission.
    /// </summary>
    public class PermissionRequirement : IAuthorizationRequirement
    {
        public string Permission { get; }

        public PermissionRequirement(string permission)
        {
            Permission = permission;
        }
    }

    /// <summary>
    /// Dynamically provides authorization policies based on the requested permission.
    /// Prevents the need to register every single permission policy in Program.cs.
    /// </summary>
    public class HmsAuthorizationPolicyProvider : DefaultAuthorizationPolicyProvider
    {
        public HmsAuthorizationPolicyProvider(IOptions<AuthorizationOptions> options) : base(options) { }

        public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
        {
            // Check if the policy is one of our dynamic permission policies
            if (policyName.StartsWith(RequirePermissionAttribute.PolicyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var permissionName = policyName.Substring(RequirePermissionAttribute.PolicyPrefix.Length);
                
                return new AuthorizationPolicyBuilder()
                    .AddRequirements(new PermissionRequirement(permissionName))
                    .Build();
            }

            // Fallback to default policies (like standard [Authorize])
            return await base.GetPolicyAsync(policyName);
        }
    }

    /// <summary>
    /// Evaluates the PermissionRequirement against the user's actual permissions.
    /// </summary>
    public class HmsAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
    {
        private readonly IHmsPermissionMatrixService _permissionMatrix;
        private readonly ILogger<HmsAuthorizationHandler> _logger;

        public HmsAuthorizationHandler(
            IHmsPermissionMatrixService permissionMatrix, 
            ILogger<HmsAuthorizationHandler> logger)
        {
            _permissionMatrix = permissionMatrix;
            _logger = logger;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context, 
            PermissionRequirement requirement)
        {
            if (context.User.Identity?.IsAuthenticated != true)
            {
                return; // Fail closed
            }

            // Extract the user's unique identifier (e.g., from JWT 'sub' or 'nameid' claim)
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) 
                      ?? context.User.FindFirstValue("sub");

            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("Authorization failed: User is authenticated but missing a NameIdentifier/Sub claim.");
                return;
            }

            // Query the centralized permission matrix (Database, Cache, or Claims)
            bool hasPermission = await _permissionMatrix.HasPermissionAsync(userId, requirement.Permission);

            if (hasPermission)
            {
                context.Succeed(requirement);
            }
            else
            {
                // HIPAA Compliance: Log unauthorized access attempts to sensitive PHI endpoints
                _logger.LogWarning(
                    "SECURITY AUDIT: Unauthorized access attempt. User {UserId} attempted to access a resource requiring {Permission}.", 
                    userId, 
                    requirement.Permission);
            }
        }
    }

    /// <summary>
    /// Interface representing the service that checks the actual permission matrix.
    /// You will implement this to check your database, Redis cache, or user claims.
    /// </summary>
    public interface IHmsPermissionMatrixService
    {
        Task<bool> HasPermissionAsync(string userId, string permissionName);
    }
}
```

### How to Register and Use It

**1. Registration in `Program.cs`:**
```csharp
using Hms.SharedKernel.AccessControl;
using Microsoft.AspNetCore.Authorization;

// Register the custom policy provider
builder.Services.AddSingleton<IAuthorizationPolicyProvider, HmsAuthorizationPolicyProvider>();

// Register the authorization handler
builder.Services.AddScoped<IAuthorizationHandler, HmsAuthorizationHandler>();

// Register your implementation of the permission matrix lookup
builder.Services.AddScoped<IHmsPermissionMatrixService, MyDatabasePermissionMatrixService>();

builder.Services.AddAuthorization();
```

**2. Usage in a Controller:**
```csharp
using Microsoft.AspNetCore.Mvc;
using Hms.SharedKernel.AccessControl;

[ApiController]
[Route("api/patients")]
public class PatientClinicalController : ControllerBase
{
    [HttpGet("{patientId}/clinical-records")]
    [RequirePermission(HmsPermission.ViewClinicalRecords)]
    public IActionResult GetClinicalRecords(Guid patientId)
    {
        // Only users with the ViewClinicalRecords permission will reach this code
        return Ok(new { PatientId = patientId, Data = "PHI Data..." });
    }

    [HttpPost("{patientId}/prescriptions")]
    [RequirePermission(HmsPermission.PrescribeMedication)]
    public IActionResult Prescribe(Guid patientId, [FromBody] PrescriptionDto dto)
    {
        // Only authorized prescribers will reach this code
        return Ok();
    }
}
```

### Healthcare Security Highlights in this Design:
1. **Fail-Closed Architecture:** If the user lacks an ID, or the matrix service fails, the handler simply returns without calling `context.Succeed()`, resulting in a `403 Forbidden`.
2. **Strong Typing:** Developers use the `HmsPermission` enum rather than magic strings, preventing typos that could accidentally expose Protected Health Information (PHI).
3. **Audit Logging:** The `HmsAuthorizationHandler` explicitly logs unauthorized attempts. This is a critical requirement for HIPAA Security Rule compliance (Audit Controls § 164.312(b)).
4. **Decoupled Matrix:** By injecting `IHmsPermissionMatrixService`, you can easily implement caching (e.g., Redis) to ensure authorization checks don't bottleneck your database, while still allowing immediate revocation of permissions if a clinician's employment is terminated.