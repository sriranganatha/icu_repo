# Hospital Management System Integration Interface Catalog and Failure-Handling Runbook

## 1. Purpose

This document catalogs the major internal and external interfaces used by the Hospital Management System and defines failure-handling expectations for each integration class.

It is intended to support implementation planning, operations readiness, incident response, and regulated data exchange design.

## 2. Integration Principles

- Every interface has an owner, support model, and data-classification profile.
- Every integration is tenant-aware and region-aware.
- Clinical and financial interfaces must support reconciliation and replay where appropriate.
- Message failure must be visible to operators and must not silently corrupt workflow state.
- PHI disclosure through interfaces is consent-aware and minimum-necessary.

## 3. Interface Categories

### 3.1 Clinical Standards Interfaces

- HL7 v2 ADT
- HL7 v2 ORM or ORU
- FHIR REST APIs
- DICOM and RIS or PACS integrations

### 3.2 Device and Telemetry Interfaces

- bedside monitors
- ventilators
- infusion pumps
- RPM device vendors
- barcode scanners and printers

### 3.3 Financial and Administrative Interfaces

- eligibility and payer gateways
- claims clearinghouses
- remittance feeds
- payment gateways
- ERP or procurement systems

### 3.4 Engagement Interfaces

- SMS or email gateways
- push notification platforms
- telehealth vendors
- CRM or outreach systems

### 3.5 Governance Interfaces

- identity providers
- SIEM or security monitoring
- archival or evidence storage systems
- AI vendors or model endpoints

## 4. Interface Catalog Fields

Every interface should be registered with:

- interface_id
- name
- owner_team
- tenant_scope model
- data_classification
- direction inbound, outbound, or bidirectional
- protocol
- source_system
- target_system
- idempotency expectations
- retry policy
- replay support
- reconciliation method
- PHI involvement flag
- residency considerations
- failure escalation path

## 5. Priority Interface Catalog

| Interface ID | Name | Protocol | Data Class | Multi-Tenant Pattern | Criticality | Failure Handling |
| --- | --- | --- | --- | --- | --- | --- |
| IF-ADT-01 | ADT Messaging | HL7 v2 | clinical | tenant and facility scoped | critical | retry, dead-letter, operator queue, reconciliation |
| IF-LAB-01 | Lab Orders and Results | HL7 v2 or FHIR | clinical | tenant scoped | critical | retry, result reconciliation, duplicate detection |
| IF-RAD-01 | Radiology Orders and Reports | HL7 v2, FHIR, DICOM metadata | clinical | tenant scoped | critical | queue retry, report-state reconciliation |
| IF-DEV-01 | ICU Device Ingestion | device protocol adapter | clinical telemetry | facility and tenant scoped | critical | stale-feed alerts, backpressure handling, data quality flags |
| IF-PAY-01 | Eligibility Check | payer API | financial and clinical admin | tenant scoped | high | retry with idempotency, timeout fallback |
| IF-CLM-01 | Claim Submission | clearinghouse API | financial | tenant scoped | high | retry, rejection queue, resubmission support |
| IF-PMT-01 | Payment Gateway | HTTPS API | financial | tenant scoped | high | idempotency key, reconciliation by transaction ref |
| IF-IDP-01 | Identity Federation | SAML or OIDC | workforce identity | control-plane scoped | high | auth fallback and outage banner |
| IF-AI-01 | AI Inference Gateway to Model Provider | HTTPS API | PHI and AI evidence | tenant and region scoped | high | fallback mode, audit preservation, kill switch |
| IF-NOT-01 | Notification Provider | HTTPS API | limited PHI or operational | tenant scoped | medium | retry with suppression rules |

## 6. Failure Modes by Interface Class

### Messaging Failure

- upstream unavailable
- invalid message mapping
- duplicate message replay
- sequence or ordering break

### API Failure

- timeout
- auth failure
- rate limit
- semantic validation rejection

### Data Integrity Failure

- mismatched patient or encounter identifiers
- tenant mismatch
- result arriving without matching order
- charge replay duplication

### Privacy and Policy Failure

- consent block
- residency violation
- unauthorized disclosure target
- misrouted patient communication

## 7. Standard Failure-Handling Pattern

1. detect failure in real time where possible
2. capture correlation id and tenant context
3. classify retryable or non-retryable
4. route retryable messages through bounded retry policy
5. route exhausted or non-retryable failures into operator-visible queue
6. preserve payload or reference for investigation subject to PHI handling rules
7. reconcile downstream state before replay or closure

## 8. Interface-Specific Runbooks

### 8.1 ADT and Clinical Messaging

- pause downstream propagation if tenant routing is uncertain
- prevent duplicate admission or transfer application through idempotency keys
- show reconciliation queue to operations team
- replay only after patient and encounter scope validation

### 8.2 Results Interfaces

- hold critical-result release if patient-match confidence is insufficient
- require manual review for orphaned or ambiguous results
- preserve inbound raw message reference for auditability

### 8.3 Claims and Financial Interfaces

- never auto-replay payment capture without transaction reference validation
- queue denials and rejections with payer-specific reason mapping
- separate transmission failure from business rejection

### 8.4 Device Interfaces

- mark feeds stale when heartbeat or data freshness threshold is exceeded
- avoid fabricating continuity in patient trend views
- escalate if device assignment to patient is uncertain

### 8.5 AI Provider Interfaces

- fail closed for prohibited PHI routing or region mismatch
- fail open only to approved non-AI manual workflow where safe
- preserve prompt and retrieval audit even when output is unavailable

## 9. Multi-Tenant Integration Rules

- tenant identity must be resolved before a message mutates regulated state
- tenant-scoped transformation rules should be versioned and auditable
- shared integration engines must isolate queue state and replay operations by tenant
- dedicated tenants may use isolated connectors while still emitting standardized audit and monitoring metadata

## 10. Operational Dashboards Required

- interface success rate by tenant and interface
- retry backlog
- dead-letter queue depth
- critical orphaned-result count
- payer submission backlog
- stale device feed count
- notification failure count
- AI gateway fallback rate

## 11. Required Deliverables Per Interface

- mapping specification
- payload samples
- error catalog
- retry and replay policy
- reconciliation procedure
- access and PHI classification review
- tenant routing and residency notes