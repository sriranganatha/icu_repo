Here is a complete C# implementation of the Incident Response Service tailored for Healthcare SOC 2 compliance. 

In a SOC 2 and HIPAA-regulated environment, incident response must satisfy **Common Criteria (CC) 7.3 and 7.4** (Incident Detection and Response). The code below includes strict audit logging, explicit handling of Protected Health Information (PHI), and structured escalation paths.

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Hms.SharedKernel.Compliance.Soc2
{
    #region Enums
    public enum IncidentSeverity
    {
        P4_Low = 4,
        P3_Medium = 3,
        P2_High = 2,
        P1_Critical = 1
    }

    public enum IncidentStatus
    {
        Reported,
        Investigating,
        Contained,
        Mitigated,
        Resolved,
        Closed
    }
    #endregion

    #region Entities
    /// <summary>
    /// Represents a security or operational incident record.
    /// Designed to meet SOC 2 CC7.3 and CC7.4 audit requirements.
    /// </summary>
    public class IncidentRecord
    {
        public Guid Id { get; private set; }
        public string Title { get; private set; }
        public string Description { get; private set; }
        public string ReportedBy { get; private set; }
        public DateTimeOffset ReportedAt { get; private set; }
        public bool IsPhiCompromised { get; private set; }
        public bool IsSystemDowntime { get; private set; }
        
        public IncidentSeverity Severity { get; set; }
        public IncidentStatus Status { get; set; }
        public List<string> EscalationContacts { get; set; } = new();
        
        public string? ResolutionDetails { get; set; }
        public string? PostIncidentReviewNotes { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }

        public IncidentRecord(string title, string description, string reportedBy, bool isPhiCompromised, bool isSystemDowntime)
        {
            Id = Guid.NewGuid();
            Title = title;
            Description = description;
            ReportedBy = reportedBy;
            ReportedAt = DateTimeOffset.UtcNow;
            IsPhiCompromised = isPhiCompromised;
            IsSystemDowntime = isSystemDowntime;
            Status = IncidentStatus.Reported;
        }
    }
    #endregion

    #region Interfaces (Dependencies)
    public interface IIncidentRepository
    {
        Task SaveAsync(IncidentRecord incident);
        Task<IncidentRecord?> GetByIdAsync(Guid id);
        Task UpdateAsync(IncidentRecord incident);
    }

    public interface ISoc2AuditLogger
    {
        Task LogSecurityEventAsync(string eventType, string description, string user, Guid? resourceId);
    }

    public interface INotificationService
    {
        Task SendEscalationAlertAsync(List<string> contacts, IncidentRecord incident);
    }
    #endregion

    #region Service
    /// <summary>
    /// Handles Incident Response workflows in compliance with SOC 2 Type II requirements.
    /// </summary>
    public class IncidentResponseService
    {
        private readonly IIncidentRepository _repository;
        private readonly ISoc2AuditLogger _auditLogger;
        private readonly INotificationService _notificationService;
        private readonly ILogger<IncidentResponseService> _logger;

        public IncidentResponseService(
            IIncidentRepository repository,
            ISoc2AuditLogger auditLogger,
            INotificationService notificationService,
            ILogger<IncidentResponseService> logger)
        {
            _repository = repository;
            _auditLogger = auditLogger;
            _notificationService = notificationService;
            _logger = logger;
        }

        /// <summary>
        /// 1. Reports a new incident, classifies it, and triggers escalations.
        /// </summary>
        public async Task<IncidentRecord> ReportIncidentAsync(string title, string description, string reportedBy, bool isPhiCompromised, bool isSystemDowntime)
        {
            _logger.LogInformation("New incident reported by {ReportedBy}: {Title}", reportedBy, title);

            var incident = new IncidentRecord(title, description, reportedBy, isPhiCompromised, isSystemDowntime);

            // 2. Classify Severity
            incident.Severity = ClassifySeverity(incident);

            // 3. Determine Escalation Path
            incident.EscalationContacts = GetEscalationPath(incident.Severity);

            await _repository.SaveAsync(incident);

            // SOC 2 Requirement: Immutable audit log of security events
            await _auditLogger.LogSecurityEventAsync(
                "IncidentReported", 
                $"Severity {incident.Severity} incident reported. PHI Compromised: {isPhiCompromised}", 
                reportedBy, 
                incident.Id);

            // Trigger Notifications
            await _notificationService.SendEscalationAlertAsync(incident.EscalationContacts, incident);

            return incident;
        }

        /// <summary>
        /// 2. Classifies severity based on healthcare and system impact.
        /// </summary>
        public IncidentSeverity ClassifySeverity(IncidentRecord incident)
        {
            // Any potential breach of Protected Health Information (PHI) is an automatic P1 in healthcare.
            if (incident.IsPhiCompromised)
            {
                return IncidentSeverity.P1_Critical;
            }

            // Complete system downtime affecting patient care is P1.
            if (incident.IsSystemDowntime)
            {
                return IncidentSeverity.P1_Critical;
            }

            // Keyword-based heuristic for P2 (High)
            var desc = incident.Description.ToLower();
            if (desc.Contains("ransomware") || desc.Contains("malware") || desc.Contains("unauthorized access"))
            {
                return IncidentSeverity.P2_High;
            }

            // Default to Medium for standard anomalies, Low for minor bugs
            return desc.Contains("bug") ? IncidentSeverity.P4_Low : IncidentSeverity.P3_Medium;
        }

        /// <summary>
        /// 3. Returns the required escalation contacts based on severity.
        /// </summary>
        public List<string> GetEscalationPath(IncidentSeverity severity)
        {
            return severity switch
            {
                IncidentSeverity.P1_Critical => new List<string> 
                { 
                    "ciso@hms.com", 
                    "privacy-officer@hms.com", // Required for HIPAA/PHI breaches
                    "legal@hms.com", 
                    "executive-team@hms.com" 
                },
                IncidentSeverity.P2_High => new List<string> 
                { 
                    "security-manager@hms.com", 
                    "it-director@hms.com" 
                },
                IncidentSeverity.P3_Medium => new List<string> 
                { 
                    "soc-analysts@hms.com", 
                    "it-ops@hms.com" 
                },
                IncidentSeverity.P4_Low => new List<string> 
                { 
                    "helpdesk@hms.com" 
                },
                _ => new List<string> { "helpdesk@hms.com" }
            };
        }

        /// <summary>
        /// 4. Completes the Post-Incident Review (PIR), a strict SOC 2 requirement for continuous monitoring.
        /// </summary>
        public async Task PostIncidentReviewAsync(Guid incidentId, string resolutionDetails, string reviewNotes, string reviewedBy)
        {
            var incident = await _repository.GetByIdAsync(incidentId);
            if (incident == null)
            {
                throw new ArgumentException("Incident not found.", nameof(incidentId));
            }

            if (incident.Status == IncidentStatus.Closed)
            {
                throw new InvalidOperationException("Incident is already closed.");
            }

            incident.ResolutionDetails = resolutionDetails;
            incident.PostIncidentReviewNotes = reviewNotes;
            incident.Status = IncidentStatus.Closed;
            incident.ClosedAt = DateTimeOffset.UtcNow;

            await _repository.UpdateAsync(incident);

            // SOC 2 Requirement: Log the closure and review of the incident
            await _auditLogger.LogSecurityEventAsync(
                "IncidentClosedAndReviewed", 
                $"Incident PIR completed. Status changed to Closed.", 
                reviewedBy, 
                incident.Id);

            _logger.LogInformation("Post-Incident Review completed for Incident {IncidentId} by {ReviewedBy}", incidentId, reviewedBy);
        }
    }
    #endregion
}
```

### Key SOC 2 & Healthcare Compliance Features Included:
1. **PHI Awareness (`IsPhiCompromised`)**: In healthcare, any incident touching Protected Health Information (PHI) triggers immediate HIPAA breach notification protocols. The `ClassifySeverity` method automatically elevates these to `P1_Critical`.
2. **Role-Based Escalation (`GetEscalationPath`)**: SOC 2 requires predefined communication channels for incidents. P1s automatically loop in the Privacy Officer (DPO) and Legal, which auditors look for during Type II observations.
3. **Immutable Audit Logging (`ISoc2AuditLogger`)**: Every major state change (Creation, Closure) is logged to an external audit trail, satisfying SOC 2 Common Criteria 7.2 and 7.3.
4. **Post-Incident Review (PIR)**: SOC 2 CC7.4 requires organizations to not just fix incidents, but analyze them to prevent recurrence. The `PostIncidentReviewAsync` method enforces the capture of resolution details and review notes before an incident can be marked `Closed`.