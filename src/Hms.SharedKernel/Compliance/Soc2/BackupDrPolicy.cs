Here is a complete, modern C# implementation for the `BackupDrPolicy` tailored for SOC 2 compliance. 

This code uses C# `record` types for immutable configuration data, which is a best practice for compliance policies. It also includes a factory method that generates a standard SOC 2 compliant baseline configuration.

```csharp
using System;
using System.Collections.Generic;

namespace Hms.SharedKernel.Compliance.Soc2
{
    /// <summary>
    /// Represents the Backup and Disaster Recovery (DR) Policy required for SOC 2 Availability and Security criteria (CC9.1, A1.2).
    /// </summary>
    public class BackupDrPolicy
    {
        public string PolicyId { get; init; } = Guid.NewGuid().ToString();
        public string Version { get; init; } = "1.0";
        public DateTimeOffset LastReviewedDate { get; init; }
        public string PolicyOwnerRole { get; init; } = "Chief Information Security Officer (CISO)";

        /// <summary>
        /// Defines the Recovery Point Objective (RPO) and Recovery Time Objective (RTO) per service tier.
        /// </summary>
        public Dictionary<ServiceTier, RecoveryObjective> RecoveryObjectives { get; init; } = new();

        /// <summary>
        /// Defines the backup schedules and retention policies per service tier.
        /// </summary>
        public Dictionary<ServiceTier, List<BackupSchedule>> BackupSchedules { get; init; } = new();

        /// <summary>
        /// Defines the failover procedures to be executed during a declared disaster.
        /// </summary>
        public Dictionary<ServiceTier, FailoverProcedure> FailoverProcedures { get; init; } = new();

        /// <summary>
        /// Defines the schedule and tracking for mandatory DR recovery testing.
        /// </summary>
        public RecoveryTestSchedule TestSchedule { get; init; }

        /// <summary>
        /// Generates a baseline SOC 2 compliant Backup and DR Policy.
        /// </summary>
        public static BackupDrPolicy CreateSoc2BaselinePolicy()
        {
            return new BackupDrPolicy
            {
                LastReviewedDate = DateTimeOffset.UtcNow,
                
                RecoveryObjectives = new Dictionary<ServiceTier, RecoveryObjective>
                {
                    { ServiceTier.Critical, new RecoveryObjective(TimeSpan.FromMinutes(15), TimeSpan.FromHours(1)) },
                    { ServiceTier.High, new RecoveryObjective(TimeSpan.FromHours(1), TimeSpan.FromHours(4)) },
                    { ServiceTier.Medium, new RecoveryObjective(TimeSpan.FromHours(4), TimeSpan.FromHours(24)) },
                    { ServiceTier.Low, new RecoveryObjective(TimeSpan.FromHours(24), TimeSpan.FromDays(3)) }
                },

                BackupSchedules = new Dictionary<ServiceTier, List<BackupSchedule>>
                {
                    {
                        ServiceTier.Critical, new List<BackupSchedule>
                        {
                            new BackupSchedule(BackupType.ContinuousTransactionLog, "Continuous", TimeSpan.FromDays(35), true, true, StorageRedundancy.GeoRedundant),
                            new BackupSchedule(BackupType.Incremental, "0 * * * *", TimeSpan.FromDays(35), true, true, StorageRedundancy.GeoRedundant), // Hourly
                            new BackupSchedule(BackupType.Full, "0 0 * * 0", TimeSpan.FromDays(365 * 7), true, true, StorageRedundancy.GeoRedundant) // Weekly, 7-year retention
                        }
                    }
                    // Additional tiers would be configured here...
                },

                FailoverProcedures = new Dictionary<ServiceTier, FailoverProcedure>
                {
                    {
                        ServiceTier.Critical, new FailoverProcedure(
                            ProcedureId: "DR-PROC-01",
                            Name: "Critical Systems Active-Passive Failover",
                            Steps: new List<string>
                            {
                                "1. Declare disaster and notify incident response team.",
                                "2. Halt primary database replication.",
                                "3. Promote secondary geo-replica database to primary.",
                                "4. Update DNS routing via Traffic Manager to secondary region.",
                                "5. Verify system health and data integrity.",
                                "6. Notify stakeholders of successful failover."
                            },
                            RequiredApprovals: new List<string> { "CTO", "VP of Engineering" },
                            EstimatedDuration: TimeSpan.FromMinutes(45),
                            RunbookUrl: "https://wiki.hms.local/dr/runbooks/critical-failover"
                        )
                    }
                },

                TestSchedule = new RecoveryTestSchedule(
                    Frequency: TestFrequency.BiAnnually,
                    LastTestedDate: DateTimeOffset.UtcNow.AddMonths(-2),
                    NextScheduledDate: DateTimeOffset.UtcNow.AddMonths(4),
                    RequiresThirdPartyAudit: true,
                    LastTestReportUrl: "https://wiki.hms.local/dr/reports/latest"
                )
            };
        }
    }

    // --- Supporting Enums and Records ---

    public enum ServiceTier
    {
        Critical, // Core infrastructure, databases, authentication
        High,     // Customer-facing APIs, web portals
        Medium,   // Internal reporting, background processing
        Low       // Archival systems, dev/test environments
    }

    public enum BackupType
    {
        Full,
        Incremental,
        Differential,
        ContinuousTransactionLog
    }

    public enum StorageRedundancy
    {
        LocallyRedundant,
        ZoneRedundant,
        GeoRedundant
    }

    public enum TestFrequency
    {
        Monthly,
        Quarterly,
        BiAnnually,
        Annually
    }

    /// <summary>
    /// RPO: Maximum acceptable amount of data loss measured in time.
    /// RTO: Maximum acceptable amount of time to restore the system.
    /// </summary>
    public record RecoveryObjective(TimeSpan Rpo, TimeSpan Rto);

    /// <summary>
    /// Configuration for backups, ensuring SOC 2 requirements like encryption and immutability (ransomware protection) are met.
    /// </summary>
    public record BackupSchedule(
        BackupType Type,
        string CronExpression,
        TimeSpan RetentionPeriod,
        bool IsEncryptedAtRest,
        bool IsImmutable,
        StorageRedundancy RedundancyLevel
    );

    /// <summary>
    /// Step-by-step procedure for failing over systems during an outage.
    /// </summary>
    public record FailoverProcedure(
        string ProcedureId,
        string Name,
        List<string> Steps,
        List<string> RequiredApprovals,
        TimeSpan EstimatedDuration,
        string RunbookUrl
    );

    /// <summary>
    /// Schedule for testing the DR plan. SOC 2 requires regular testing of backup restorations and failover plans.
    /// </summary>
    public record RecoveryTestSchedule(
        TestFrequency Frequency,
        DateTimeOffset? LastTestedDate,
        DateTimeOffset NextScheduledDate,
        bool RequiresThirdPartyAudit,
        string LastTestReportUrl
    );
}
```

### Key SOC 2 Compliance Features Included:
1. **Immutability & Encryption (`IsImmutable`, `IsEncryptedAtRest`)**: Addresses SOC 2 Security criteria (CC6.1, CC7.3) by ensuring backups cannot be altered by ransomware and are encrypted at rest.
2. **Geo-Redundancy (`StorageRedundancy`)**: Addresses Availability criteria (A1.2) by ensuring backups survive a localized physical disaster.
3. **Strict RPO/RTO Definitions**: Demonstrates to auditors that the business has formally defined and quantified its availability commitments.
4. **Mandatory Testing Schedule (`RecoveryTestSchedule`)**: SOC 2 requires that backups and DR plans aren't just created, but *tested* regularly (CC9.1). The `LastTestReportUrl` provides the exact artifact an auditor will ask for.
5. **Approval Workflows (`RequiredApprovals`)**: Ensures failovers are authorized, satisfying Logical Access and Change Management criteria.