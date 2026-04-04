# Hospital Management System Physical Schema Blueprint

## 1. Purpose

This document translates the conceptual domain model into a physical schema blueprint for the first implementation wave. It focuses on schema organization, tenancy enforcement patterns, indexing strategy, auditability, and migration readiness.

## 2. Physical Design Principles

- Separate schemas or stores by bounded context where practical.
- Enforce tenant scope in every regulated table.
- Keep business identifiers tenant-scoped.
- Prefer append-only history for clinically material changes.
- Use outbox tables for event publication.
- Partition large tables by tenant, time, or both depending on workload.

## 3. Recommended Schema Layout

### Control Plane Schemas

- cp_tenancy
- cp_identity
- cp_policy
- cp_reference

### Clinical Data Schemas

- cl_mpi
- cl_schedule
- cl_encounter
- cl_inpatient
- cl_emergency
- cl_nursing
- cl_diagnostics
- cl_pharmacy

### Operations and Governance Schemas

- op_revenue
- op_workforce
- op_supply
- gov_consent
- gov_audit
- gov_ai
- gov_quality

## 4. Mandatory Column Set for Regulated Tables

Each regulated table should include, unless technically inapplicable:

- id
- tenant_id
- region_id
- facility_id nullable where not applicable
- created_at
- created_by
- updated_at
- updated_by
- version_no
- classification_code
- legal_hold_flag default false
- source_system nullable

## 5. First-Wave Table Blueprint

### 5.1 MPI Tables

#### cl_mpi.patient_profile

- id pk
- tenant_id not null
- region_id not null
- enterprise_person_key not null
- legal_given_name
- legal_family_name
- preferred_name
- date_of_birth
- sex_at_birth
- primary_language
- status_code
- created_at
- created_by
- updated_at
- updated_by
- version_no

Indexes:

- unique (tenant_id, enterprise_person_key)
- index (tenant_id, legal_family_name, date_of_birth)

#### cl_mpi.patient_identifier

- id pk
- tenant_id not null
- patient_id fk
- identifier_type
- identifier_value_hash_or_token
- issuer
- status_code

Indexes:

- unique (tenant_id, identifier_type, identifier_value_hash_or_token)

### 5.2 Encounter Tables

#### cl_encounter.encounter

- id pk
- tenant_id not null
- region_id not null
- facility_id not null
- patient_id not null
- encounter_type not null
- source_pathway
- attending_provider_ref
- start_at
- end_at nullable
- status_code
- classification_code
- created_at
- created_by
- updated_at
- updated_by
- version_no

Indexes:

- index (tenant_id, patient_id, start_at desc)
- index (tenant_id, facility_id, encounter_type, status_code)

#### cl_encounter.clinical_note

- id pk
- tenant_id not null
- encounter_id not null
- patient_id not null
- note_type
- note_classification_code
- content_json not null
- ai_interaction_id nullable
- authored_at
- authored_by
- amended_from_note_id nullable
- version_no
- legal_hold_flag

Indexes:

- index (tenant_id, encounter_id, authored_at)
- index (tenant_id, patient_id, note_type)

History model:

- retain prior note versions or model as immutable note revisions

### 5.3 Inpatient Tables

#### cl_inpatient.admission

- id pk
- tenant_id not null
- region_id not null
- facility_id not null
- patient_id not null
- encounter_id not null
- admit_class
- admit_source
- status_code
- expected_discharge_at nullable
- utilization_status_code
- created_at
- created_by
- updated_at
- updated_by
- version_no

Indexes:

- index (tenant_id, facility_id, status_code)
- index (tenant_id, patient_id, created_at desc)

#### cl_inpatient.admission_eligibility_evaluation

- id pk
- tenant_id not null
- facility_id not null
- patient_id not null
- encounter_id not null
- candidate_class
- decision_code
- rationale_json
- payer_authorization_status
- override_flag
- approved_by nullable
- created_at
- created_by

Indexes:

- index (tenant_id, patient_id, created_at desc)
- index (tenant_id, facility_id, decision_code)

### 5.4 Emergency Tables

#### cl_emergency.emergency_arrival

- id pk
- tenant_id not null
- region_id not null
- facility_id not null
- patient_id nullable
- temporary_identity_alias nullable
- arrival_mode
- chief_complaint
- handoff_source
- created_at
- created_by

#### cl_emergency.triage_assessment

- id pk
- tenant_id not null
- arrival_id not null
- patient_id nullable
- acuity_level
- chief_complaint
- vital_snapshot_json
- re_triage_flag
- pathway_recommendation nullable
- performed_at
- performed_by

Indexes:

- index (tenant_id, arrival_id, performed_at)
- index (tenant_id, facility_id, acuity_level, performed_at desc)

### 5.5 Diagnostics Tables

#### cl_diagnostics.result_record

- id pk
- tenant_id not null
- region_id not null
- facility_id not null
- patient_id not null
- order_id not null
- analyte_code not null
- measured_value_text_or_numeric
- unit_code
- abnormal_flag
- critical_flag
- result_at
- recorded_by
- classification_code

Indexes:

- index (tenant_id, patient_id, analyte_code, result_at desc)
- index (tenant_id, critical_flag, result_at desc)

#### cl_diagnostics.biomarker_series

- id pk
- tenant_id not null
- patient_id not null
- analyte_code not null
- baseline_value nullable
- latest_value nullable
- delta_value nullable
- last_trended_at nullable

Indexes:

- unique (tenant_id, patient_id, analyte_code)

### 5.6 Revenue Tables

#### op_revenue.charge_item

- id pk
- tenant_id not null
- facility_id not null
- patient_id not null
- encounter_ref not null
- charge_type
- quantity
- unit_amount
- currency_code
- service_date
- source_ref nullable
- created_at
- created_by

Indexes:

- index (tenant_id, encounter_ref, service_date)
- unique (tenant_id, source_ref) where source_ref is not null

#### op_revenue.claim

- id pk
- tenant_id not null
- patient_id not null
- encounter_ref not null
- payer_ref not null
- claim_status
- billed_amount
- allowed_amount nullable
- submitted_at nullable
- created_at
- created_by
- updated_at
- updated_by
- version_no

Indexes:

- index (tenant_id, payer_ref, claim_status)
- index (tenant_id, patient_id, created_at desc)

### 5.7 Governance Tables

#### gov_consent.consent_record

- id pk
- tenant_id not null
- patient_id not null
- consent_type
- status_code
- effective_from
- effective_to nullable
- captured_by
- captured_at
- document_ref nullable

#### gov_audit.audit_event

- id pk
- tenant_id not null
- region_id not null
- facility_id nullable
- event_type
- entity_type
- entity_id
- actor_type
- actor_id
- correlation_id
- classification_code
- occurred_at
- payload_json

Partition guidance:

- partition by tenant_id and time bucket where supported

#### gov_ai.ai_interaction_record

- id pk
- tenant_id not null
- region_id not null
- patient_id nullable
- workflow_type
- model_version
- prompt_version
- retrieval_scope_ref nullable
- user_action nullable
- created_at
- created_by
- evidence_refs_json nullable

## 6. Tenancy Enforcement Patterns

### Shared Table Model

- all primary queries require tenant_id predicate
- all unique constraints include tenant_id
- repository layer and database policies enforce tenant visibility

### Schema-Per-Tenant Model

- tenant_id may still be retained for observability and migration
- control plane stores tenant-location mapping

### Dedicated Database Model

- tenant_id retained in audit and event records for shared observability
- physical restore and migration can remain tenant-specific

## 7. Outbox and Inbox Tables

Each event-producing schema should include an outbox table with:

- id
- tenant_id
- event_type
- event_version
- aggregate_type
- aggregate_id
- payload_json
- occurred_at
- published_at nullable
- publication_status

Each idempotent consumer may use inbox or processed-event tables with:

- event_id
- consumer_name
- processed_at
- processing_status

## 8. Indexing and Partition Strategy

- high-volume clinical tables should index by tenant plus patient or facility plus time
- audit and telemetry tables should be time-partitioned
- biomarker and results tables should support patient plus analyte trend queries
- emergency board and bed board projections should rely on denormalized read models rather than deep transactional joins

## 9. Migration Strategy

- use additive schema changes first
- maintain backward-compatible readers during migration windows
- support tenant promotion from shared to dedicated storage through export plus replay or dual-write migration patterns
- preserve audit continuity through migration metadata and immutable lineage records

## 10. Deliverables After This Blueprint

- DDL per first-wave schema
- row-level security or tenancy enforcement examples
- outbox implementation template
- data-retention mapping per table
- seed reference data model