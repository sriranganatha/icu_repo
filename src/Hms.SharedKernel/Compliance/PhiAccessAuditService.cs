Here is the complete C# implementation for the PHI Access Audit Service. 

This code is designed to comply with **HIPAA 45 CFR §164.312(b) (Audit Controls)**, which requires covered entities to implement hardware, software, and/or procedural mechanisms that record and examine activity in information systems that contain or use electronic protected health information (ePHI).

### C# Implementation

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Hms.SharedKernel.Compliance
{
    /// <summary>
    /// Represents the type of action performed on the Protected Health Information (PHI).
    /// </summary>
    public enum PhiAction
    {
        Access,
        Modification,
        Deletion,
        Export
    }

    /// <summary>
    /// Entity representing a single HIPAA-compliant audit log entry.
    /// Maps to the PhiAuditEntry table in the database.
    /// </summary>
    public class PhiAuditEntry
    {
        public Guid Id { get; set; }
        
        // 45 CFR §164.312(b) requires exact time of access. UTC is mandatory for standardized auditing.
        public DateTimeOffset TimestampUtc { get; set; }
        
        // The identity of the user/system accessing the PHI
        public string UserId { get; set; }
        
        // The identity of the patient whose PHI is being accessed
        public string PatientId { get; set; }
        
        // The type of record (e.g., "ClinicalNotes", "LabResults", "Billing")
        public string ResourceType { get; set; }
        
        // The specific unique identifier of the record accessed
        public string ResourceId { get; set; }
        
        // The action performed
        public PhiAction Action { get; set; }
        
        // Additional context (e.g., "Exported to PDF", "Modified blood pressure reading")
        public string Details { get; set; }
        
        // Network origin of the request, critical for security incident investigations
        public string IpAddress { get; set; }
    }

    /// <summary>
    /// Interface for logging PHI access events to comply with HIPAA Audit Controls.
    /// </summary>
    public interface IPhiAccessAuditService
    {
        Task LogAccess(string userId, string patientId, string resourceType, string resourceId, string details = null);
        Task LogModification(string userId, string patientId, string resourceType, string resourceId, string details = null);
        Task LogDeletion(string userId, string patientId, string resourceType, string resourceId, string details = null);
        Task LogExport(string userId, string patientId, string resourceType, string resourceId, string destination, string details = null);
    }

    /// <summary>
    /// Repository interface to abstract the database implementation (e.g., EF Core).
    /// </summary>
    public interface IPhiAuditRepository
    {
        Task InsertAsync(PhiAuditEntry entry, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Implementation of the PHI Access Audit Service.
    /// </summary>
    public class PhiAccessAuditService : IPhiAccessAuditService
    {
        private readonly IPhiAuditRepository _repository;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<PhiAccessAuditService> _logger;

        public PhiAccessAuditService(
            IPhiAuditRepository repository,
            IHttpContextAccessor httpContextAccessor,
            ILogger<PhiAccessAuditService> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _httpContextAccessor = httpContextAccessor;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task LogAccess(string userId, string patientId, string resourceType, string resourceId, string details = null)
        {
            return RecordAuditAsync(PhiAction.Access, userId, patientId, resourceType, resourceId, details);
        }

        public Task LogModification(string userId, string patientId, string resourceType, string resourceId, string details = null)
        {
            return RecordAuditAsync(PhiAction.Modification, userId, patientId, resourceType, resourceId, details);
        }

        public Task LogDeletion(string userId, string patientId, string resourceType, string resourceId, string details = null)
        {
            return RecordAuditAsync(PhiAction.Deletion, userId, patientId, resourceType, resourceId, details);
        }

        public Task LogExport(string userId, string patientId, string resourceType, string resourceId, string destination, string details = null)
        {
            var exportDetails = $"Destination: {destination}. {details}".Trim();
            return RecordAuditAsync(PhiAction.Export, userId, patientId, resourceType, resourceId, exportDetails);
        }

        private async Task RecordAuditAsync(
            PhiAction action, 
            string userId, 
            string patientId, 
            string resourceType, 
            string resourceId, 
            string details)
        {
            try
            {
                var entry = new PhiAuditEntry
                {
                    Id = Guid.NewGuid(),
                    TimestampUtc = DateTimeOffset.UtcNow,
                    UserId = userId,
                    PatientId = patientId,
                    ResourceType = resourceType,
                    ResourceId = resourceId,
                    Action = action,
                    Details = details,
                    IpAddress = GetClientIpAddress()
                };

                await _repository.InsertAsync(entry);
            }
            catch (Exception ex)
            {
                // In a strict HIPAA environment, if the audit log fails, the transaction should ideally be aborted.
                // At a minimum, we must log a critical alert to the standard application logger.
                _logger.LogCritical(ex, "CRITICAL: Failed to write to PHI Audit Log. Action: {Action}, User: {UserId}, Patient: {PatientId}", 
                    action, userId, patientId);
                
                // Depending on your organization's strictness, you may want to rethrow the exception here 
                // to prevent the PHI operation from completing un-audited.
                throw new InvalidOperationException("Failed to record PHI audit log. Operation aborted for compliance.", ex);
            }
        }

        private string GetClientIpAddress()
        {
            return _httpContextAccessor?.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "Unknown";
        }
    }
}
```

### Key HIPAA Compliance Features Included:

1. **Who, What, When, Where:** The `PhiAuditEntry` captures the exact User (`UserId`), the exact Patient (`PatientId`), the specific record (`ResourceType` & `ResourceId`), the exact time in UTC (`TimestampUtc`), and the network origin (`IpAddress`).
2. **Action Categorization:** Differentiating between `Access` (viewing), `Modification`, `Deletion`, and `Export` is crucial. Exporting PHI carries a higher risk of data exfiltration, so the `LogExport` method specifically requires a `destination` parameter.
3. **Fail-Safe Auditing:** In the `catch` block, if the database fails to write the audit log, a `Critical` log is generated, and an exception is thrown. Under strict HIPAA interpretations, **if you cannot audit the access, you should not permit the access**.
4. **Immutability (Architectural Note):** While this C# code handles the application layer, ensure that the underlying `PhiAuditEntry` SQL table is configured with **Append-Only** permissions. The database user credentials used by this application should have `INSERT` rights, but absolutely no `UPDATE` or `DELETE` rights on the audit table.