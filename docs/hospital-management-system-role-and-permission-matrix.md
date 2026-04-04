# Hospital Management System Role and Permission Matrix

## 1. Purpose

This document defines the baseline role model, permission boundaries, separation-of-duties expectations, and emergency-access rules for the Hospital Management System.

It is intended to support implementation of RBAC with optional ABAC overlays, privacy enforcement, least-privilege access, access reviews, and audit readiness.

## 2. Access Model Principles

- Least privilege by default.
- Minimum necessary access for PHI.
- Context-aware restrictions by facility, unit, specialty, encounter, shift, and patient relationship.
- No direct privilege bundling across incompatible financial, clinical, and administrative duties without approval.
- Break-glass access only for urgent patient-care needs and always with enhanced logging and review.

## 3. Permission Categories

### Clinical Permissions

- View patient chart.
- Edit clinical documentation.
- Place orders.
- Acknowledge results.
- Administer medications.
- View and trend telemetry.
- Access sensitive note classes.
- Approve discharge and transition documents.

### Operational Permissions

- Register patient.
- Manage appointment and queue.
- Manage bed assignment.
- Manage staffing and handoff assignments.
- Manage inventory and materials.

### Financial Permissions

- View billing data.
- Post charges.
- Collect payments.
- Manage claims and denials.
- View payer contracts.

### Compliance and Governance Permissions

- View audit logs.
- Run disclosure and ROI workflows.
- Approve break-glass reviews.
- Configure retention and legal holds.
- Review AI audit records.

### Technical and Platform Permissions

- Manage integrations.
- View system diagnostics.
- Configure identity and access policy.
- Rotate secrets and certificates.
- Deploy or change system configuration.

## 4. Role Matrix

| Role | Core Access Scope | Allowed Actions | Restricted Actions |
| --- | --- | --- | --- |
| Patient | Own record and approved proxy-linked records | View appointments, results, invoices, messages, telehealth, education, forms | Cannot access internal notes, other patients, audit data, or hidden clinical workflows |
| Caregiver or Proxy | Patient-authorized scope only | View approved dependent or proxy data, forms, payments, messaging | No access outside delegated scope or to restricted note classes unless specifically allowed |
| Front Desk Registrar | Registration and scheduling context | Create or update demographics, verify insurance, manage appointments and check-in | No clinical documentation, no medication access, no broad chart viewing beyond registration minimum |
| Triage Nurse | Emergency or ambulatory triage context | Record vitals, triage notes, acuity, route patients, place protocol-driven preliminary actions where approved | No final diagnosis coding, no unrestricted billing changes |
| Ward Nurse | Assigned patients and unit context | View and update nursing notes, care plans, eMAR, bedside tasks, handoff content | No payer contract administration, no uncontrolled access to unrelated patients |
| Nurse Manager | Unit or department context | View census, staffing, escalations, quality indicators, staffing assignments | No unrestricted financial administration or system security administration |
| Attending Physician | Active patient treatment relationship | View chart, document, place orders, review results, manage discharge, use AI documentation tools | No direct system security admin, no claims adjudication |
| Consulting Physician | Consult-linked patient scope | View consult-relevant chart, document consult notes, review relevant results | No unrelated unit access and no admission override without assigned workflow authority |
| Surgeon | Surgical and perioperative context | Manage OT workflow, document procedures, review related diagnostics, prescribe and order | No unrestricted administrative or payer configuration access |
| Anesthesiologist | Perioperative and recovery context | Pre-op assessment, anesthesia documentation, perioperative orders, PACU handoff | No unrelated inpatient chart access unless treatment relationship exists |
| ICU Clinician | ICU-assigned patients | View and manage high-acuity charting, telemetry, critical orders, care planning | No unrelated facility-wide access by default |
| Pharmacist | Medication, formulary, dispensing, and patient-medication context | Verify orders, dispense, compound, manage recalls, stewardship review, reconcile medication inventory | No broad financial claim management or unrestricted clinical note editing |
| Pharmacy Technician | Dispensing and inventory context | Prepare, dispense under supervision, manage stock, handle returns | No independent order verification or formulary governance approval |
| Lab Technician | Diagnostic workflow context | Manage specimens, process results, instrument workflows, quality control | No unrelated chart editing or financial administration |
| Radiologist | Imaging workflow context | Review images, dictate reports, acknowledge critical findings | No unrelated clinical chart editing beyond imaging responsibilities |
| Billing Specialist | Billing and claims context | Post charges, manage invoices, claims, denials, remittance tasks | No broad clinical note editing or sensitive note access |
| Insurance Coordinator | Authorization and payer workflow context | Eligibility checks, pre-auth, referral validation, payer communications | No unnecessary access to unrelated clinical detail |
| Case Manager | Transition and utilization context | Review medical necessity, discharge barriers, social needs, post-acute planning | No unrestricted technical admin or financial contract management |
| Compliance Officer | Privacy, disclosure, and audit context | View audit logs, ROI records, access anomalies, consent exceptions, retention exceptions | No routine patient-care documentation or billing task execution |
| Security Administrator | Identity, security policy, and monitoring context | Manage access policy, monitor alerts, review anomalies, manage secrets and privileged workflows | No routine clinical editing or financial posting |
| Integration Engineer | Interface and integration context | Manage interface mappings, monitor queue failures, replay approved messages | No broad patient browsing outside support workflow and audit-approved access |
| System Administrator | Platform operations context | Manage system configuration, deployment, tenancy metadata, environment controls | No default access to live PHI beyond support workflows and approved controls |
| AI Governance Reviewer | AI governance context | Review model changes, prompt changes, AI incidents, monitoring reports, safety evidence | No broad patient-care execution or unrestricted chart editing |

## 5. Sensitive Data Access Rules

### Specially Protected Data

Examples:

- Behavioral health records.
- HIV or equivalent specially protected conditions.
- Reproductive health records where jurisdiction requires enhanced controls.
- Medico-legal and sealed records.

Rules:

- Separate permission flags from general chart access.
- Additional disclosure restrictions and masking rules.
- Patient relationship and purpose-of-use checks where required.
- Enhanced audit review and alerting for access.

### Minor and Guardian Rules

- Proxy access must reflect legal relationship, age rules, custody rules, and jurisdiction-specific consent laws.
- Access expires or changes when guardian status, age, or legal context changes.

## 6. Break-Glass Access Policy

- Only available to approved clinical roles and limited support roles under emergency conditions.
- Requires reason selection and optional free-text justification.
- Grants short-lived, scoped elevation.
- Triggers immediate enhanced audit event and retrospective review queue.

## 7. Separation of Duties

The following combinations should be restricted or require explicit exception approval:

- Claims adjudication and payer contract configuration.
- Security administration and compliance approval.
- System administration and unrestricted audit-log management.
- Medication preparation approval and controlled-substance discrepancy closure without oversight.
- Chart amendment approval and release-of-information fulfillment for the same case without dual control.

## 8. ABAC Overlay Dimensions

- Facility.
- Department.
- Unit or ward.
- Specialty.
- Shift.
- Treatment relationship.
- On-call status.
- Patient age or legal status.
- Sensitive note class.
- Device posture and network context.

## 9. Required Platform Capabilities

- Role catalog and versioned permission definitions.
- ABAC policy engine.
- Break-glass workflow and review queue.
- Sensitive-data segmentation flags.
- Delegation and coverage assignment.
- Access review reporting and entitlement attestation exports.

## 10. Implementation Notes

- Roles should be composable but centrally approved.
- Tenant-specific role variants should inherit from controlled templates rather than fork arbitrary permissions.
- Every permission change must be auditable.