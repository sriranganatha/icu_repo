# Hospital Management System Event Catalog

## 1. Purpose

This document defines the baseline event catalog for the Hospital Management System. It provides a versioned event model for cross-domain coordination, analytics, audit enrichment, and integration processing.

It is intentionally aligned to the bounded contexts in the architecture and to the multi-tenant API and data design documents.

## 2. Event Design Principles

- Events represent facts that already happened.
- Events are immutable.
- Events are versioned independently from REST APIs.
- All events must carry tenant and residency context.
- Domain services publish their own events and must not publish on behalf of other domains.
- Consumers must be idempotent and version-aware.

## 3. Standard Event Envelope

Every event should include:

- event_id
- event_type
- event_version
- occurred_at
- producer_service
- tenant_id
- region_id
- facility_id when applicable
- entity_type
- entity_id
- actor_type
- actor_id
- correlation_id
- causation_id where applicable
- classification_code
- payload

## 4. Event Reliability Rules

- Publishing should be tied to local transaction completion using an outbox or equivalent durable pattern.
- Replay must preserve original event_id and occurred_at.
- Sensitive payloads should include only minimum-necessary data for downstream consumers.
- High-risk patient-safety events should support dead-letter routing and operator-visible failure alerts.

## 5. Domain Event Families

### 5.1 Tenant and Facility Events

- tenant.created.v1
- tenant.updated.v1
- tenant.tenancy-flavor-changed.v1
- facility.created.v1
- facility.updated.v1
- facility.configuration-updated.v1

Core payload fields:

- tenant_id
- tenancy_flavor
- region_id
- facility_id
- operational_status

### 5.2 Identity and Access Events

- user.provisioned.v1
- user.role-assigned.v1
- user.role-revoked.v1
- delegation.created.v1
- break-glass.requested.v1
- break-glass.approved.v1
- break-glass.closed.v1

Core payload fields:

- user_id
- role_code
- scope
- approval_status
- justification

### 5.3 Patient Identity Events

- patient.registered.v1
- patient.updated.v1
- patient.guardian-linked.v1
- patient.proxy-access-granted.v1
- patient.merge-requested.v1
- patient.merged.v1
- patient.unmerged.v1

Core payload fields:

- patient_id
- enterprise_person_key
- identifiers
- legal_status_flags

### 5.4 Scheduling and Registration Events

- appointment.booked.v1
- appointment.rescheduled.v1
- appointment.cancelled.v1
- patient.checked-in.v1
- queue.ticket-issued.v1
- queue.status-changed.v1

### 5.5 Encounter and Charting Events

- encounter.created.v1
- encounter.status-changed.v1
- note.created.v1
- note.amended.v1
- diagnosis.recorded.v1
- allergy.recorded.v1
- chart-summary-generated.v1

### 5.6 Inpatient and ADT Events

- admission.created.v1
- admission.eligibility-evaluated.v1
- admission.approved.v1
- bed.assigned.v1
- patient.transferred.v1
- discharge.initiated.v1
- discharge.completed.v1

Core payload fields:

- admission_id
- patient_id
- encounter_id
- admit_class
- eligibility_decision
- bed_ref
- transfer_reason

### 5.7 Emergency Events

- emergency.arrival-registered.v1
- emergency.triage-completed.v1
- emergency.re-triage-completed.v1
- emergency.pathway-activated.v1
- emergency.observation-converted.v1
- emergency.board-status-changed.v1
- emergency.disposition-recorded.v1

Core payload fields:

- arrival_id
- triage_level
- pathway_code
- board_status
- disposition_code

### 5.8 Nursing and eMAR Events

- care-plan.created.v1
- nursing-task.completed.v1
- medication.barcode-verified.v1
- medication.administered.v1
- medication.missed.v1
- shift.handover-submitted.v1

### 5.9 ICU and Telemetry Events

- telemetry.observation-recorded.v1
- telemetry.feed-stale.v1
- alarm.raised.v1
- alarm.acknowledged.v1
- high-acuity-alert.generated.v1

### 5.10 Surgery Events

- surgery.case-scheduled.v1
- surgery.preop-cleared.v1
- surgery.started.v1
- surgery.completed.v1
- implant.recorded.v1
- pacu.admitted.v1
- pacu.discharged.v1

### 5.11 Diagnostics Events

- order.placed.v1
- specimen.collected.v1
- specimen.received.v1
- result.recorded.v1
- result.critical.v1
- biomarker.trend-updated.v1
- radiology.report-finalized.v1
- transfusion.administered.v1

Core payload fields:

- order_id
- result_id
- analyte_code
- critical_flag
- delta_value

### 5.12 Pharmacy Events

- formulary.item-updated.v1
- dispensation.completed.v1
- compounding.job-completed.v1
- controlled-substance.logged.v1
- medication.recall-opened.v1
- medication.return-processed.v1

### 5.13 Revenue Cycle Events

- charge.posted.v1
- invoice.generated.v1
- payment.collected.v1
- eligibility.completed.v1
- claim.submitted.v1
- claim.denied.v1
- remittance.posted.v1

Core payload fields:

- charge_id
- invoice_id
- claim_id
- payer_ref
- financial_amounts

### 5.14 Utilization and Care Coordination Events

- continued-stay-review.completed.v1
- discharge-barrier.recorded.v1
- transition-plan-approved.v1
- post-acute-referral.sent.v1

### 5.15 Compliance and HIM Events

- consent.recorded.v1
- consent.revoked.v1
- roi.request-created.v1
- roi.request-fulfilled.v1
- legal-hold-applied.v1
- chart-amendment-requested.v1
- disclosure-recorded.v1

### 5.16 Quality and Safety Events

- incident.reported.v1
- infection.case-opened.v1
- safety-signal-raised.v1
- accreditation-evidence-linked.v1

### 5.17 Workforce Events

- staffing.roster-published.v1
- coverage.assignment-created.v1
- credential.expiring.v1
- privilege.granted.v1
- privilege.revoked.v1

### 5.18 Supply Chain Events

- inventory.item-received.v1
- inventory.lot-depleted.v1
- material.reserved.v1
- implant.recalled.v1

### 5.19 AI Governance Events

- ai.interaction-recorded.v1
- ai.output-accepted.v1
- ai.output-rejected.v1
- ai.output-overridden.v1
- ai.incident-opened.v1
- ai.model-approved.v1
- ai.model-disabled.v1

Core payload fields:

- ai_interaction_id
- model_version
- prompt_version
- workflow_type
- user_action
- evidence_refs

## 6. Priority Consumers

- audit and compliance store
- analytics and metrics projections
- staff workspace aggregators
- notification service
- AI monitoring service
- integration engine
- search indexing pipeline

## 7. Multi-Tenant Event Isolation Rules

- Shared event buses must carry tenant metadata in the envelope.
- Consumers must validate tenant context before projection or outbound publication.
- Cross-tenant analytics aggregation must occur only in approved governance contexts.
- Tenant migration workflows must preserve event lineage and replay safety.

## 8. Event Versioning Policy

- Additive payload changes may increment minor internal schema versions while retaining event_version where compatibility is preserved.
- Breaking payload or semantic changes require a new event version.
- Deprecated events must remain supported through a defined migration window.

## 9. Suggested First Implementation Set

- patient.registered.v1
- encounter.created.v1
- admission.created.v1
- admission.eligibility-evaluated.v1
- emergency.triage-completed.v1
- medication.administered.v1
- result.recorded.v1
- result.critical.v1
- charge.posted.v1
- claim.submitted.v1
- consent.recorded.v1
- ai.interaction-recorded.v1