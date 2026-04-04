# Hospital Management System API Specification

## 1. Purpose

This document defines the API design approach for the Hospital Management System, including service boundaries, cross-service conventions, multi-tenant context propagation, core resource contracts, command patterns, event publication expectations, and security requirements.

It is intended as the baseline contract style for internal services, backend-for-frontend layers, partner integrations, and patient-facing APIs.

## 2. API Design Principles

- API-first design with explicit versioning.
- Clear separation between command APIs, query APIs, and event contracts.
- Tenant-aware and region-aware request processing.
- No cross-domain direct database access.
- Clinically material state changes produce auditable domain events.
- External partner APIs are stable and compatibility-managed.
- Patient-facing APIs expose only minimum-necessary data.

## 3. Multi-Tenancy Design Requirements

Every request must be processed in an explicit tenant context.

### Required Context Dimensions

- Tenant.
- Region or residency domain.
- Facility.
- Department or operational scope where applicable.
- User identity.
- User role set.
- Device or channel context.

### Multi-Tenant Flavors Supported

#### Flavor A: Shared Application and Shared Database with Tenant Partitioning

- Suitable for smaller tenants and cost-sensitive deployments.
- Requires strict tenant keys on all regulated records.
- Requires row-level enforcement and tenant-safe indexing.

#### Flavor B: Shared Application with Schema-Per-Tenant or Database-Per-Tenant

- Suitable for medium or higher-regulation tenants.
- Supports stronger operational isolation and easier tenant-scoped backup or migration.
- Shared control plane still manages tenancy, identity, feature flags, and observability.

#### Flavor C: Dedicated Deployment Per Tenant or Tenant Group

- Suitable for large hospital groups, national regulatory constraints, or custom integration needs.
- Supports full compute and storage isolation.
- Control plane services may remain centrally governed if permitted by policy.

#### Flavor D: Hybrid Regional Tenancy

- Tenant may use shared services for low-sensitivity modules while using dedicated regional data stores for clinical records or AI evidence.
- Recommended when residency rules or service-line sensitivity vary by domain.

## 4. API Request Conventions

### Required Headers

- Authorization: bearer token or equivalent approved credential.
- X-Tenant-Id: resolved tenant identifier unless embedded and trusted via gateway claims.
- X-Region-Id: region or residency boundary where applicable.
- X-Facility-Id: facility context for facility-scoped operations.
- X-Correlation-Id: request trace identifier.
- X-Client-Channel: staff-web, staff-mobile, kiosk, patient-portal, integration, or partner-app.

### Gateway Rule

External clients should not set privileged tenancy headers directly unless using an approved partner integration pattern. Public or partner traffic should typically terminate at a gateway that resolves and injects trusted context.

### Idempotency

- Command endpoints for creates or financial actions should support idempotency keys.
- Integration endpoints should require idempotent partner reference handling.

## 5. API Styles by Use Case

### 5.1 Staff and Workflow APIs

- Prefer REST for predictable resource and command semantics.
- Use task-oriented command endpoints for workflow transitions.

### 5.2 Dashboard and Workspace APIs

- Prefer aggregated query APIs exposed by backend-for-frontend services.
- Avoid chatty client composition across many domain services.

### 5.3 Partner and Registry APIs

- Support standards-based integration using FHIR, HL7 adapters, and other approved healthcare protocols.
- Non-standard partner APIs should be adapter-based and versioned.

### 5.4 Event Contracts

- Domain events should be versioned independently from REST APIs.
- Consumers must tolerate additive changes and explicit deprecation windows.

## 6. Core Platform APIs

### 6.1 Tenant and Facility API

Purpose:
Manage organizational hierarchy, facilities, departments, wards, rooms, and tenant-scoped configuration.

Representative endpoints:

- GET /api/v1/tenants/{tenantId}
- GET /api/v1/tenants/{tenantId}/facilities
- GET /api/v1/facilities/{facilityId}/departments
- POST /api/v1/facilities/{facilityId}/wards
- POST /api/v1/tenants/{tenantId}/feature-flags

### 6.2 Identity and Access API

Purpose:
Manage workforce identities, roles, coverage assignments, break-glass workflows, and access policy resolution.

Representative endpoints:

- GET /api/v1/users/me
- GET /api/v1/users/{userId}/roles
- POST /api/v1/users/{userId}/delegations
- POST /api/v1/access/break-glass
- GET /api/v1/access/review-queues

### 6.3 Master Patient Identity API

Purpose:
Manage patient registration, identity resolution, guardian relationships, proxy access, and merge workflows.

Representative endpoints:

- GET /api/v1/patients?mrn=&name=&dob=
- POST /api/v1/patients
- GET /api/v1/patients/{patientId}
- PATCH /api/v1/patients/{patientId}
- POST /api/v1/patients/{patientId}/guardians
- POST /api/v1/patient-identity-merges

### 6.4 Scheduling and Registration API

Representative endpoints:

- GET /api/v1/appointments
- POST /api/v1/appointments
- POST /api/v1/appointments/{appointmentId}/reschedule
- POST /api/v1/check-ins
- GET /api/v1/queues/{queueId}

### 6.5 Encounter and Documentation API

Representative endpoints:

- POST /api/v1/encounters
- GET /api/v1/encounters/{encounterId}
- POST /api/v1/encounters/{encounterId}/notes
- POST /api/v1/encounters/{encounterId}/diagnoses
- POST /api/v1/encounters/{encounterId}/allergies
- GET /api/v1/patients/{patientId}/chart-summary

### 6.6 Orders and Results API

Representative endpoints:

- POST /api/v1/orders
- GET /api/v1/orders/{orderId}
- GET /api/v1/results/{resultId}
- POST /api/v1/results/{resultId}/acknowledge
- GET /api/v1/patients/{patientId}/biomarkers

### 6.7 Inpatient and ADT API

Representative endpoints:

- POST /api/v1/admissions
- GET /api/v1/admissions/{admissionId}
- POST /api/v1/admissions/{admissionId}/transfers
- POST /api/v1/admissions/{admissionId}/discharge-initiate
- POST /api/v1/admission-eligibility-evaluations
- GET /api/v1/facilities/{facilityId}/bed-board

### 6.8 Emergency API

Representative endpoints:

- POST /api/v1/emergency-arrivals
- POST /api/v1/emergency-triage-assessments
- POST /api/v1/emergency-pathways/{pathwayId}/activate
- GET /api/v1/facilities/{facilityId}/emergency-board
- POST /api/v1/emergency-observations/{observationId}/convert

### 6.9 Nursing and eMAR API

Representative endpoints:

- GET /api/v1/nursing-worklists
- POST /api/v1/care-plans
- POST /api/v1/medication-administrations
- POST /api/v1/medication-barcode-verifications
- POST /api/v1/shift-handovers

### 6.10 ICU Telemetry API

Representative endpoints:

- POST /api/v1/device-observations
- GET /api/v1/patients/{patientId}/telemetry-trends
- GET /api/v1/icu-alerts
- POST /api/v1/icu-alerts/{alertId}/acknowledge

### 6.11 Surgery API

Representative endpoints:

- POST /api/v1/ot-schedules
- POST /api/v1/preop-checklists/{checklistId}/complete
- POST /api/v1/surgeries/{surgeryId}/intraop-notes
- POST /api/v1/surgeries/{surgeryId}/implants
- POST /api/v1/pacu-stays

### 6.12 Diagnostics API

Representative endpoints:

- POST /api/v1/lab-specimens
- POST /api/v1/lab-results
- POST /api/v1/radiology-reports
- POST /api/v1/transfusions
- GET /api/v1/patients/{patientId}/biomarker-trends

### 6.13 Pharmacy API

Representative endpoints:

- GET /api/v1/formulary/items
- POST /api/v1/dispensations
- POST /api/v1/compounding-jobs
- POST /api/v1/controlled-substance-events
- POST /api/v1/medication-recalls

### 6.14 Revenue Cycle API

Representative endpoints:

- POST /api/v1/charges
- GET /api/v1/invoices/{invoiceId}
- POST /api/v1/payments
- POST /api/v1/eligibility-checks
- POST /api/v1/claims
- POST /api/v1/claims/{claimId}/denials

### 6.15 Utilization and Care Coordination API

Representative endpoints:

- POST /api/v1/continued-stay-reviews
- POST /api/v1/discharge-barriers
- POST /api/v1/post-acute-referrals
- POST /api/v1/care-transition-tasks

### 6.16 Compliance and Governance API

Representative endpoints:

- POST /api/v1/consents
- POST /api/v1/release-of-information-requests
- GET /api/v1/audit-events
- POST /api/v1/legal-holds
- POST /api/v1/retention-policy-evaluations

### 6.17 AI Governance API

Representative endpoints:

- POST /api/v1/ai/note-drafts
- POST /api/v1/ai/chart-summaries
- POST /api/v1/ai/risk-scores
- POST /api/v1/ai/differential-support
- POST /api/v1/ai/treatment-recommendations
- POST /api/v1/ai/care-plan-drafts
- POST /api/v1/ai/automation-proposals
- GET /api/v1/ai/audit-records
- POST /api/v1/ai/incidents
- POST /api/v1/ai/model-approvals

## 7. Canonical Resource Attributes

Every regulated resource should support the following metadata where applicable:

- id
- tenantId
- regionId
- facilityId
- createdAt
- createdBy
- updatedAt
- updatedBy
- version
- sourceSystem
- classification
- legalHoldFlag
- auditTraceId

## 8. Command and State Transition Pattern

For clinically material workflows, use explicit commands instead of generic field patching.

Examples:

- POST /admissions/{id}/approve
- POST /admissions/{id}/transfer
- POST /claims/{id}/submit
- POST /roi-requests/{id}/fulfill
- POST /ai/note-drafts/{id}/accept
- POST /ai/treatment-recommendations/{id}/accept
- POST /ai/automation-proposals/{id}/approve

Rationale:

- Improves audit clarity.
- Preserves workflow intent.
- Simplifies policy enforcement and event generation.

## 9. Error Contract

Standard error payload should include:

- code
- message
- category
- correlationId
- tenantId if resolvable
- retryable flag
- details array

Representative categories:

- authorization_denied
- tenant_context_invalid
- facility_scope_invalid
- validation_failed
- policy_blocked
- legal_hold_conflict
- region_constraint_violation
- integration_dependency_failure

## 10. Event Contract Requirements

Each event should include:

- eventId
- eventType
- eventVersion
- occurredAt
- tenantId
- regionId
- facilityId if applicable
- entityType
- entityId
- actorId or system actor
- correlationId
- payload

Representative events:

- patient.registered
- patient.merged
- admission.created
- bed.assigned
- triage.completed
- medication.administered
- biomarker.result-recorded
- claim.submitted
- roi.request-fulfilled
- ai.note-draft-accepted

## 11. Security Requirements for APIs

- All protected endpoints require authenticated identity.
- Authorization is evaluated using RBAC plus ABAC.
- Sensitive endpoints require purpose-of-use or workflow-context validation.
- Bulk export, ROI, audit access, and AI audit access require elevated permissions.
- Public patient endpoints must be separately rate-limited and abuse-monitored.

## 12. Multi-Tenant Routing and Isolation Requirements

### Shared Data Plane Tenants

- Every primary key or unique business key lookup must be tenant-scoped.
- No API may return cross-tenant identifiers in error messages or pagination artifacts.

### Dedicated Tenant Deployments

- Tenant context remains explicit for observability, governance, and shared control-plane workflows.
- APIs may omit X-Tenant-Id externally if bound by deployment, but internal events must still carry tenant identity.

### Cross-Tenant Administration

- Reserved for approved control-plane services.
- Requires elevated admin role, purpose-of-use, and strong audit logging.

## 13. Versioning Policy

- Public REST APIs version through URI or gateway version policy.
- Event versions are explicit and immutable.
- Breaking changes require migration guidance and defined deprecation windows.

## 14. Minimum Deliverables Per API

- OpenAPI contract or equivalent.
- Authorization matrix.
- Audit event definitions.
- Idempotency and retry behavior.
- Tenancy and residency handling notes.
- Error catalog.
- Example payloads.