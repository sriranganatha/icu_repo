# Hospital Management System Domain Model and Data Design

## 1. Purpose

This document defines the conceptual domain model, aggregate boundaries, core entities, multi-tenant data patterns, and persistence guidance for the Hospital Management System.

The goal is to provide an implementation-oriented data design without collapsing the system into one oversized schema.

## 2. Data Design Principles

- Each bounded context owns its transactional data.
- Shared identity is achieved through stable identifiers and event-driven synchronization, not shared mutable tables.
- Tenant and residency context are first-class data attributes.
- Clinically material records preserve provenance, authorship, and version history.
- Sensitive data classes are explicitly tagged for privacy controls.
- Analytics and search use derived read models, not direct transactional joins across domains.

## 3. Multi-Tenant Data Strategy

### 3.1 Common Metadata Required on Regulated Records

- tenant_id
- region_id
- facility_id where relevant
- classification_code
- source_system
- legal_hold_flag
- created_at
- created_by
- updated_at
- updated_by
- version_no

### 3.2 Supported Tenancy Patterns

#### Pattern A: Shared Tables with Tenant Partition Keys

- Use for control-plane data and smaller tenants.
- All unique indexes must include tenant_id.
- Row-level access control or equivalent enforcement is required.

#### Pattern B: Schema-Per-Tenant

- Use for higher-isolation regulated workloads when operational overhead is acceptable.
- Shared metadata catalogs can remain in control-plane schemas.

#### Pattern C: Database-Per-Tenant or Data-Store-Per-Tenant

- Use for larger hospital groups or strict residency boundaries.
- Recommended for clinical records, AI evidence, and heavily customized tenants.

#### Pattern D: Hybrid Domain-Specific Isolation

- Use shared tenancy for low-risk operational modules.
- Use dedicated stores for clinical record, audit, ROI, or AI evidence domains.

## 4. Core Shared Reference Model

These entities are shared conceptually but may be projected into multiple bounded contexts.

### Tenant

- tenant_id
- tenant_name
- tenancy_flavor
- default_region_id
- status

### Region

- region_id
- name
- residency_policy_code

### Facility

- facility_id
- tenant_id
- facility_type
- name
- address
- timezone

### Department

- department_id
- facility_id
- specialty_code
- operational_type

### UserIdentity

- user_id
- tenant_id
- workforce_or_patient_flag
- identity_provider_ref
- status

### RoleAssignment

- assignment_id
- user_id
- role_code
- facility_scope
- department_scope
- valid_from
- valid_to

## 5. Bounded Context Aggregates

### 5.1 Master Patient Identity Domain

Primary aggregates:

- PatientProfile
- PatientIdentifier
- GuardianRelationship
- ProxyAccessGrant
- IdentityMergeCase

Representative PatientProfile fields:

- patient_id
- tenant_id
- enterprise_person_key
- legal_name
- preferred_name
- date_of_birth
- sex_at_birth
- gender_identity where allowed
- contact_methods
- primary_language
- death_indicator

Notes:

- enterprise_person_key can support cross-facility identity inside a tenant.
- Cross-tenant patient identity should not be assumed unless a governed federation model is explicitly implemented.

### 5.2 Scheduling and Registration Domain

Primary aggregates:

- Appointment
- ScheduleSlot
- QueueTicket
- CheckInSession

Representative Appointment fields:

- appointment_id
- tenant_id
- facility_id
- patient_id
- provider_ref
- specialty_code
- visit_type
- status
- scheduled_start_at
- scheduled_end_at
- booking_channel

### 5.3 Encounter and Charting Domain

Primary aggregates:

- Encounter
- ClinicalNote
- ProblemEntry
- AllergyEntry
- DiagnosisEntry
- ClinicalTask

Representative Encounter fields:

- encounter_id
- tenant_id
- facility_id
- patient_id
- encounter_type
- source_pathway
- attending_provider_ref
- start_at
- end_at
- status

### 5.4 Inpatient Domain

Primary aggregates:

- Admission
- BedAssignment
- AdmissionEligibilityEvaluation
- DischargePlan

Representative Admission fields:

- admission_id
- tenant_id
- facility_id
- patient_id
- encounter_id
- admit_source
- admit_class
- status
- expected_discharge_at
- utilization_status

Representative AdmissionEligibilityEvaluation fields:

- evaluation_id
- admission_candidate_ref
- decision
- medical_necessity_score_or_basis
- payer_authorization_status
- override_flag
- approved_by

### 5.5 Emergency Domain

Primary aggregates:

- EmergencyArrival
- TriageAssessment
- EmergencyPathwayActivation
- ObservationStay

Representative TriageAssessment fields:

- triage_assessment_id
- arrival_id
- acuity_level
- chief_complaint
- vital_snapshot
- re_triage_indicator
- performed_by
- performed_at

### 5.6 Nursing and eMAR Domain

Primary aggregates:

- CarePlan
- NursingTask
- MedicationAdministration
- HandoverRecord

Representative MedicationAdministration fields:

- medication_administration_id
- tenant_id
- patient_id
- order_id
- scheduled_at
- administered_at
- dose_given
- route_code
- barcode_verification_status
- administering_user_id

### 5.7 ICU and Telemetry Domain

Primary aggregates:

- DeviceAssignment
- TelemetryObservation
- AlarmEvent
- HighAcuityAlert

Representative TelemetryObservation fields:

- telemetry_observation_id
- tenant_id
- patient_id
- device_id
- observation_type
- observed_value
- observed_unit
- observed_at
- quality_status

### 5.8 Surgery Domain

Primary aggregates:

- SurgeryCase
- PreOpChecklist
- IntraOpRecord
- ImplantUsage
- PACUStay

### 5.9 Diagnostics Domain

Primary aggregates:

- LabOrderPanel
- Specimen
- ResultRecord
- BiomarkerSeries
- RadiologyReport
- TransfusionEvent

Representative ResultRecord fields:

- result_id
- tenant_id
- patient_id
- order_id
- analyte_code
- measured_value
- reference_range
- abnormal_flag
- critical_flag
- result_at

Representative BiomarkerSeries fields:

- biomarker_series_id
- tenant_id
- patient_id
- analyte_code
- baseline_value
- latest_value
- delta_value
- last_trended_at

### 5.10 Pharmacy Domain

Primary aggregates:

- FormularyItem
- Dispensation
- CompoundPreparation
- ControlledSubstanceLedgerEntry
- RecallCase

### 5.11 Revenue Cycle Domain

Primary aggregates:

- ChargeItem
- Invoice
- PaymentReceipt
- EligibilityCheck
- Claim
- DenialCase

Representative Claim fields:

- claim_id
- tenant_id
- patient_id
- encounter_ref
- payer_ref
- claim_status
- total_billed_amount
- total_allowed_amount
- submitted_at

### 5.12 Utilization and Care Coordination Domain

Primary aggregates:

- ContinuedStayReview
- DischargeBarrier
- TransitionPlan
- PostAcuteReferral

### 5.13 Compliance and HIM Domain

Primary aggregates:

- ConsentRecord
- ROIRequest
- LegalHold
- ChartAmendmentRequest
- DisclosureRecord

### 5.14 Quality and Safety Domain

Primary aggregates:

- IncidentReport
- InfectionCase
- SafetySignal
- AccreditationEvidenceItem

### 5.15 Workforce Domain

Primary aggregates:

- StaffingRoster
- CoverageAssignment
- CredentialRecord
- PrivilegeGrant
- CompetencyRecord

### 5.16 Supply Chain Domain

Primary aggregates:

- InventoryItem
- InventoryLot
- PreferenceCard
- MaterialReservation
- RecallTrackingRecord

### 5.17 AI Governance Domain

Primary aggregates:

- AIInteractionRecord
- ModelRegistration
- PromptTemplateVersion
- AIRiskReview
- AIIncident

Representative AIInteractionRecord fields:

- ai_interaction_id
- tenant_id
- workflow_type
- patient_id nullable where appropriate
- model_version
- prompt_version
- retrieval_scope_ref
- user_action
- created_at

## 6. Canonical Identifier Strategy

- Use globally unique opaque identifiers for domain entities.
- Preserve business identifiers separately from primary keys.
- Business identifiers such as MRN, accession number, invoice number, and claim number must be tenant-scoped.
- External identifiers should include source system reference and effective validity.

## 7. Recommended Persistence Patterns by Domain

### Relational-First Domains

- MPI
- Scheduling
- Encounters
- Inpatient
- Emergency workflow state
- Revenue cycle
- Compliance and HIM
- Workforce
- Supply chain

### Time-Series or Stream-Optimized Domains

- ICU telemetry
- RPM observations
- event analytics

### Object Storage-Backed Domains

- document binaries
- scanned consents
- discharge packets
- diagnostic image references
- audit evidence artifacts

### Search Projections

- patient search
- clinician workspace search
- emergency board projections
- longitudinal chart summary index

## 8. Data Isolation Rules

- No domain may rely on implicit tenancy from deployment alone.
- All cross-domain references must include enough scope to validate tenant ownership.
- Analytics exports must preserve tenant boundary and classification metadata.
- Shared search indexes must be tenant-partitioned or separately provisioned.

## 9. Audit and History Requirements by Entity

The following entities require full create or update history or append-only event history:

- ClinicalNote
- DiagnosisEntry
- AllergyEntry
- MedicationAdministration
- AdmissionEligibilityEvaluation
- ConsentRecord
- ROIRequest
- LegalHold
- Claim
- ControlledSubstanceLedgerEntry
- AIInteractionRecord

## 10. Suggested Database Packaging

### Control Plane Databases

- tenancy and facility metadata
- feature flags
- role and policy metadata
- terminology and reference data

### Clinical Data Plane Databases

- MPI store
- encounter and charting store
- inpatient store
- emergency store
- nursing store
- diagnostics store
- pharmacy store

### Governance and Evidence Databases

- audit store
- compliance and HIM store
- AI governance store

### Financial and Operations Databases

- revenue cycle store
- workforce store
- supply chain store
- quality and safety store

## 11. Multi-Tenant Migration and Portability Requirements

- Tenant export and migration must be possible without corrupting identifiers.
- Shared-tenant deployments should support promotion of a tenant into dedicated storage if growth or regulation requires it.
- Residency reassignment must be handled as a governed migration workflow.
- Backup and restore must support tenant-scoped recovery where storage model allows.

## 12. Engineering Deliverables Derived from This Model

- Logical entity catalog.
- Physical schema by bounded context.
- tenant_id and region_id enforcement strategy per store.
- event catalog and outbox tables.
- search and analytics projection design.
- archival and retention mapping.