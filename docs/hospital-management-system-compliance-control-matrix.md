# Hospital Management System Compliance Control Matrix

## 1. Purpose

This document maps the Hospital Management System requirements and architecture to a practical compliance control framework aligned to HIPAA safeguards and SOC 2 trust services criteria. It is intended to support design reviews, audit preparation, control ownership, implementation planning, and evidence collection.

This is a product and platform control matrix, not a legal certification statement.

## 2. Control Objectives

- Protect protected health information and sensitive operational data.
- Ensure only authorized users can access the minimum necessary information.
- Preserve integrity, provenance, and non-repudiation of clinical and administrative records.
- Maintain service availability and resilience for patient-care workflows.
- Support privacy, retention, disclosure, and legal hold obligations.
- Govern AI usage for PHI and clinically consequential assistance.

## 3. Control Domains

The control set is organized under these domains:

- Governance and risk management.
- Identity and access management.
- Audit logging and monitoring.
- Cryptography and key management.
- Data privacy and disclosure management.
- Secure development and change management.
- Infrastructure and endpoint security.
- Resilience, backup, and disaster recovery.
- Vendor and integration risk.
- AI governance and model risk management.

## 4. Control Matrix

| Control ID | Control Objective | Control Requirement | Relevant Standard Alignment | System Implementation Approach | Primary Evidence |
| --- | --- | --- | --- | --- | --- |
| GOV-01 | Security governance | Establish documented security, privacy, and acceptable-use policies reviewed on a defined cadence. | HIPAA Administrative Safeguards, SOC 2 CC1 | Policies governed by compliance program and linked to product controls and release processes. | Approved policy set, policy review log |
| GOV-02 | Risk management | Maintain periodic risk assessments covering PHI, integrations, AI features, and operational dependencies. | HIPAA Risk Analysis, SOC 2 CC3 | Annual and change-triggered risk reviews tied to roadmap and architecture changes. | Risk register, review meeting records |
| GOV-03 | Workforce responsibility | Assign control owners for security, privacy, audit, AI, and operational compliance. | HIPAA Administrative Safeguards, SOC 2 CC1 | Ownership matrix across product, platform, security, compliance, and operations teams. | RACI matrix, ownership registry |
| IAM-01 | Centralized identity | Use centralized identity providers for workforce authentication. | HIPAA Technical Safeguards, SOC 2 CC6 | SSO using SAML or OIDC for staff identities. | IdP configuration, integration docs |
| IAM-02 | Strong authentication | Require MFA for remote, privileged, and high-risk access. | HIPAA Technical Safeguards, SOC 2 CC6 | MFA enforced by policy engine and IdP for defined user classes. | MFA policy, test evidence |
| IAM-03 | Least privilege | Grant access based on role, department, facility, and patient relationship. | HIPAA Minimum Necessary, SOC 2 CC6 | RBAC with optional ABAC overlays and context-aware authorization. | Role matrix, access policy definitions |
| IAM-04 | Emergency access | Provide break-glass access with justification and enhanced logging. | HIPAA Emergency Access Procedure, SOC 2 CC6 | Break-glass workflow with short-lived elevation and review queue. | Break-glass logs, review reports |
| IAM-05 | Shared device security | Protect shared clinical workstations and kiosks against unattended access. | HIPAA Technical Safeguards, SOC 2 CC6 | Badge or biometric login, quick-lock, session timeout, kiosk session isolation. | Device policy, timeout config, audit logs |
| IAM-06 | Periodic access review | Review access entitlements and privileged accounts on a defined cadence. | HIPAA Administrative Safeguards, SOC 2 CC6 | Quarterly certification workflows with exception remediation. | Access review reports, remediation tickets |
| AUD-01 | Comprehensive audit logging | Log view, create, update, delete, export, print, login, denial, override, and AI-assisted actions. | HIPAA Technical Safeguards, SOC 2 CC7 | Immutable compliance-grade audit logs generated across all domains. | Audit schema, sample logs, retention config |
| AUD-02 | Patient record access history | Provide patient-level access history for investigations and privacy review. | HIPAA Accounting and Audit Expectations, SOC 2 CC7 | Patient-chart access history exposed to authorized compliance users. | Access history reports |
| AUD-03 | Separation of logs | Separate mutable operational logs from compliance-grade audit storage. | SOC 2 CC7, CC8 | Dedicated immutable audit store and controlled retention policies. | Log architecture diagrams, storage config |
| AUD-04 | Monitoring and alerting | Detect anomalous access, suspicious login patterns, and exfiltration indicators. | HIPAA Security Rule, SOC 2 CC7 | SIEM or monitoring pipeline with rules for anomalous behavior and alert workflows. | Alert rules, incident tickets |
| CRY-01 | Encryption in transit | Encrypt all traffic carrying PHI or sensitive data. | HIPAA Technical Safeguards, SOC 2 CC6 | TLS enforced across browser, API, integration, and service-to-service traffic. | TLS configs, scan results |
| CRY-02 | Encryption at rest | Encrypt databases, backups, logs, and object storage containing sensitive data. | HIPAA Technical Safeguards, SOC 2 CC6 | Storage encryption with managed keys and service-specific enforcement. | Storage settings, key policy docs |
| CRY-03 | Key lifecycle control | Manage encryption keys with rotation, separation of duties, and controlled access. | HIPAA Technical Safeguards, SOC 2 CC6 | Central key management service and periodic rotation schedule. | KMS configs, rotation records |
| PRIV-01 | Consent-aware access | Enforce consent, proxy, and purpose-of-use rules before disclosure or retrieval. | HIPAA Privacy Rule, SOC 2 P-series | Consent policy service and consent-aware retrieval or disclosure filters. | Consent rules, test cases |
| PRIV-02 | Minimum necessary use | Limit access and retrieval to the minimum necessary data set. | HIPAA Privacy Rule, SOC 2 P-series | Scoped APIs, masked views, PHI-safe AI retrieval, and role-aware workspaces. | Access traces, masking configs |
| PRIV-03 | Sensitive record segmentation | Apply additional controls to specially protected data classes. | HIPAA, state or local privacy laws, SOC 2 P-series | Segmented note classes, masking rules, and restricted disclosure policies. | Segmentation rules, audit samples |
| PRIV-04 | Release of information | Manage third-party disclosures through controlled intake, approval, redaction, and fulfillment. | HIPAA Privacy Rule, SOC 2 P-series | ROI workflow with request tracking, approval states, and disclosure audit trail. | ROI logs, request records |
| PRIV-05 | Data residency and jurisdiction | Keep regulated data in approved regions and enforce residency rules. | Data residency obligations, SOC 2 C-series | Regional deployment units, tenant-region binding, and controlled cross-region replication. | Hosting map, deployment policy |
| RET-01 | Retention by record class | Apply retention rules by clinical, financial, audit, legal, and AI evidence record class. | HIPAA retention-related obligations, SOC 2 CC8 | Policy-driven retention schedules and archival workflows. | Retention policy, storage lifecycle config |
| RET-02 | Legal hold | Preserve records subject to investigations, disputes, or litigation. | HIPAA documentation expectations, SOC 2 CC8 | Legal-hold flags and deletion suppression workflows. | Hold logs, case records |
| SECDEV-01 | Secure SDLC | Apply secure development, peer review, secrets control, and deployment approval processes. | SOC 2 CC8 | Secure SDLC with code review, branch protection, secret scanning, and change approvals. | SDLC policy, PR evidence |
| SECDEV-02 | Vulnerability management | Scan code, dependencies, containers, and infrastructure for vulnerabilities. | HIPAA Security Rule, SOC 2 CC7 | Automated scanning and risk-based remediation SLAs. | Scan reports, remediation backlog |
| SECDEV-03 | Change management | Track regulated changes and retain approval evidence across environments. | SOC 2 CC8 | Controlled promotion pipeline with release approval and evidence retention. | Change tickets, deployment logs |
| INF-01 | Network security | Isolate public, partner, and clinical internal traffic using controlled network zones. | HIPAA Technical Safeguards, SOC 2 CC6 | Segmented network topology and service-to-service auth. | Network diagrams, policy configs |
| INF-02 | Secret management | Prevent hard-coded secrets and centralize secret rotation. | SOC 2 CC6 | Managed secret stores with rotation and scoped retrieval. | Secret management config |
| INF-03 | Endpoint hardening | Manage workstation, tablet, kiosk, and scanner security baselines. | HIPAA Administrative and Technical Safeguards, SOC 2 CC6 | MDM hooks, device posture checks, quick-lock, and kiosk restrictions. | Device baseline docs, MDM reports |
| RES-01 | Backup and recovery | Maintain tested backups for critical systems and data stores. | HIPAA Contingency Plan, SOC 2 A1 | Encrypted backups with retention and recovery testing. | Backup logs, restore test records |
| RES-02 | Disaster recovery | Define recovery targets and test failover for critical patient-care workflows. | HIPAA Contingency Plan, SOC 2 A1 | DR procedures and periodic failover exercises. | DR plan, exercise evidence |
| RES-03 | Downtime procedures | Support read-only or delayed-sync operations during service disruption. | HIPAA Contingency Plan, SOC 2 A1 | Business continuity modes for chart access, bedside capture, and sync recovery. | Downtime procedure docs, exercise logs |
| VEND-01 | Vendor due diligence | Assess external vendors handling PHI or critical workflows. | HIPAA BAAs, SOC 2 CC3 | Security review, data-flow review, and contractual controls before onboarding. | Vendor assessments, approval records |
| VEND-02 | Business associate governance | Maintain BAAs and equivalent contractual privacy or security terms where required. | HIPAA contractual safeguards | Procurement and legal control for PHI-processing vendors. | Executed agreements, vendor inventory |
| INT-01 | Integration reliability | Monitor interface errors, retries, and data reconciliation for clinical integrations. | SOC 2 Processing Integrity, CC7 | Integration engine monitoring and structured error queues. | Interface dashboards, runbooks |
| INT-02 | Outbound data controls | Apply consent, masking, and mapping validation before external disclosures. | HIPAA Privacy Rule, SOC 2 P-series | Consent-aware outbound filters and versioned transformation mappings. | Mapping configs, disclosure audit samples |
| AI-01 | Governed AI access | Route all AI through approved gateways, prompts, and retrieval policies. | HIPAA Privacy and Security considerations, SOC 2 CC7 | AI inference gateway, prompt registry, and retrieval control layer. | AI architecture evidence, gateway configs |
| AI-02 | Human oversight | Require human review before clinically consequential AI output is committed or acted upon. | Clinical safety expectation, SOC 2 CC7 | Review and approval workflow for notes, recommendations, and care-impacting suggestions. | Workflow config, audit events |
| AI-03 | AI auditability | Log prompts, retrieved context, model version, output, and user action. | SOC 2 CC7 | AI audit and safety review service with immutable evidence capture. | AI audit records |
| AI-04 | Bias and drift management | Monitor model behavior across demographics, facilities, and specialties. | Responsible AI governance, SOC 2 CC3 | Model monitoring and segmented outcome dashboards. | Monitoring reports, review minutes |
| AI-05 | PHI-safe retrieval | Prevent unauthorized retrieval of sensitive notes or protected classes for AI context assembly. | HIPAA Minimum Necessary, SOC 2 P-series | Policy-checked retrieval with sensitive note class enforcement and tenant isolation. | Retrieval policy tests, audit traces |

## 5. Control Ownership Model

- Product engineering owns application controls, workflow enforcement, and audit instrumentation.
- Platform engineering owns identity integration, secrets, infrastructure, backup, observability, and deployment controls.
- Security owns control design assurance, monitoring rules, vulnerability management, and incident response.
- Compliance and privacy own policy interpretation, evidence review, audit support, ROI governance, and disclosure policies.
- Data and AI governance owners manage terminology, lineage, model governance, and monitoring reviews.

## 6. Evidence Collection Expectations

Evidence should be collected and retained for:

- Policies and periodic reviews.
- Access reviews and privileged account certifications.
- Break-glass approvals and post-event review.
- Audit log integrity and retention settings.
- Encryption and key management configurations.
- Backup and disaster recovery test results.
- Vulnerability scanning and remediation tracking.
- Vendor reviews and contractual controls.
- AI model approvals, prompt reviews, and monitoring output.

## 7. Known Follow-On Deliverables

This matrix should be paired with:

- A formal risk register.
- A data classification and retention policy.
- An incident response and breach notification runbook.
- An access review procedure.
- A vendor risk and BAA register.
- An AI governance and model risk management specification.