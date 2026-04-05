namespace Hms.SharedKernel.Compliance.Soc2;

public sealed record Soc2Control(string Id, string Criteria, string Description, string Evidence, string Owner);

public static class Soc2ControlMatrix
{
    public static readonly Soc2Control[] Controls =
    [
        new("CC1.1", "CC1", "Security policies documented and communicated", "Policy repository + signed acknowledgments", "CISO"),
        new("CC5.1", "CC5", "Segregation of duties in change management", "PR approvals, deploy gates, no self-approve", "Engineering Lead"),
        new("CC5.2", "CC5", "Least privilege access", "RBAC audit logs, quarterly access reviews", "Security Team"),
        new("CC6.1", "CC6", "Logical access controls", "MFA, session timeout, API key rotation", "Platform Team"),
        new("CC6.2", "CC6", "Encryption in transit and at rest", "TLS 1.2+ certs, AES-256 DB encryption", "Platform Team"),
        new("CC7.1", "CC7", "System monitoring and alerting", "Prometheus metrics, PagerDuty alerts", "SRE"),
        new("CC7.2", "CC7", "Incident response procedures", "Runbooks, post-incident reviews, SLA tracking", "SRE"),
        new("CC7.3", "CC7", "Backup and recovery", "Automated backups, quarterly DR tests", "DBA"),
        new("CC8.1", "CC8", "Change management process", "PR reviews, CI/CD gates, staging validation", "Engineering Lead"),
        new("CC8.2", "CC8", "Testing before deployment", "Unit/integration/E2E tests, coverage gates", "QA"),
        new("CC9.1", "CC9", "Risk assessment", "Annual pentest, vulnerability scans, threat model", "Security Team"),
        new("CC9.2", "CC9", "Vendor risk management", "Third-party security questionnaires", "Compliance"),
    ];
}