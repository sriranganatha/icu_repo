# Hospital Management System SQL DDL Starter Pack

## 1. Purpose

This document provides a starter SQL blueprint for first-wave services. It is not a full production schema, but it is concrete enough to guide initial implementation and review tenancy enforcement patterns.

The recommended primary engine for this starter pack is PostgreSQL-compatible, based on the database analysis documented in `hospital-management-system-database-analysis.md`.

## 2. Conventions

- UUID-like opaque ids are shown as text for portability in this starter pack.
- every regulated table carries tenant_id and region_id unless not applicable
- example syntax is relational-database oriented and should be adapted to the chosen PostgreSQL-compatible deployment model

## 3. Example DDL

```sql
create schema if not exists cl_mpi;
create schema if not exists cl_encounter;
create schema if not exists cl_inpatient;
create schema if not exists cl_emergency;
create schema if not exists cl_diagnostics;
create schema if not exists op_revenue;
create schema if not exists gov_audit;

create table cl_mpi.patient_profile (
    id text primary key,
    tenant_id text not null,
    region_id text not null,
    facility_id text null,
    enterprise_person_key text not null,
    legal_given_name text not null,
    legal_family_name text not null,
    preferred_name text null,
    date_of_birth date not null,
    sex_at_birth text null,
    primary_language text null,
    status_code text not null,
    classification_code text not null default 'clinical_restricted',
    legal_hold_flag boolean not null default false,
    source_system text null,
    created_at timestamptz not null,
    created_by text not null,
    updated_at timestamptz not null,
    updated_by text not null,
    version_no integer not null default 1,
    unique (tenant_id, enterprise_person_key)
);

create index ix_patient_profile_tenant_name_dob
    on cl_mpi.patient_profile (tenant_id, legal_family_name, date_of_birth);

create table cl_encounter.encounter (
    id text primary key,
    tenant_id text not null,
    region_id text not null,
    facility_id text not null,
    patient_id text not null,
    encounter_type text not null,
    source_pathway text null,
    attending_provider_ref text null,
    start_at timestamptz not null,
    end_at timestamptz null,
    status_code text not null,
    classification_code text not null default 'clinical_restricted',
    legal_hold_flag boolean not null default false,
    source_system text null,
    created_at timestamptz not null,
    created_by text not null,
    updated_at timestamptz not null,
    updated_by text not null,
    version_no integer not null default 1
);

create index ix_encounter_tenant_patient_start
    on cl_encounter.encounter (tenant_id, patient_id, start_at desc);

create table cl_inpatient.admission (
    id text primary key,
    tenant_id text not null,
    region_id text not null,
    facility_id text not null,
    patient_id text not null,
    encounter_id text not null,
    admit_class text not null,
    admit_source text null,
    status_code text not null,
    expected_discharge_at timestamptz null,
    utilization_status_code text null,
    classification_code text not null default 'clinical_restricted',
    legal_hold_flag boolean not null default false,
    created_at timestamptz not null,
    created_by text not null,
    updated_at timestamptz not null,
    updated_by text not null,
    version_no integer not null default 1
);

create index ix_admission_tenant_facility_status
    on cl_inpatient.admission (tenant_id, facility_id, status_code);

create table cl_emergency.triage_assessment (
    id text primary key,
    tenant_id text not null,
    region_id text not null,
    facility_id text not null,
    arrival_id text not null,
    patient_id text null,
    acuity_level text not null,
    chief_complaint text null,
    vital_snapshot_json jsonb not null,
    re_triage_flag boolean not null default false,
    pathway_recommendation text null,
    classification_code text not null default 'clinical_restricted',
    created_at timestamptz not null,
    created_by text not null,
    updated_at timestamptz not null,
    updated_by text not null,
    version_no integer not null default 1
);

create index ix_triage_tenant_facility_acuity_time
    on cl_emergency.triage_assessment (tenant_id, facility_id, acuity_level, created_at desc);

create table cl_diagnostics.result_record (
    id text primary key,
    tenant_id text not null,
    region_id text not null,
    facility_id text not null,
    patient_id text not null,
    order_id text not null,
    analyte_code text not null,
    measured_value text null,
    unit_code text null,
    abnormal_flag text null,
    critical_flag boolean not null default false,
    result_at timestamptz not null,
    recorded_by text not null,
    classification_code text not null default 'clinical_restricted',
    legal_hold_flag boolean not null default false,
    created_at timestamptz not null,
    created_by text not null,
    updated_at timestamptz not null,
    updated_by text not null,
    version_no integer not null default 1
);

create index ix_result_tenant_patient_analyte_time
    on cl_diagnostics.result_record (tenant_id, patient_id, analyte_code, result_at desc);

create table op_revenue.claim (
    id text primary key,
    tenant_id text not null,
    region_id text not null,
    facility_id text not null,
    patient_id text not null,
    encounter_ref text not null,
    payer_ref text not null,
    claim_status text not null,
    billed_amount numeric(18,2) not null,
    allowed_amount numeric(18,2) null,
    classification_code text not null default 'financial_sensitive',
    legal_hold_flag boolean not null default false,
    created_at timestamptz not null,
    created_by text not null,
    updated_at timestamptz not null,
    updated_by text not null,
    version_no integer not null default 1
);

create index ix_claim_tenant_payer_status
    on op_revenue.claim (tenant_id, payer_ref, claim_status);

create table gov_audit.audit_event (
    id text primary key,
    tenant_id text not null,
    region_id text not null,
    facility_id text null,
    event_type text not null,
    entity_type text not null,
    entity_id text not null,
    actor_type text not null,
    actor_id text not null,
    correlation_id text not null,
    classification_code text not null,
    occurred_at timestamptz not null,
    payload_json jsonb not null
);

create index ix_audit_tenant_entity_time
    on gov_audit.audit_event (tenant_id, entity_type, entity_id, occurred_at desc);
```

## 4. Tenancy Enforcement Notes

- every repository query must bind tenant_id explicitly
- every unique business key should include tenant_id
- row-level security or equivalent is strongly recommended in shared-table deployments
- dedicated deployments should still retain tenant_id for observability and portability

## 5. Follow-On Work

- add foreign keys and constrained enums where chosen engine supports the required operational behavior
- add outbox tables per schema
- add partitions for audit and result tables
- add table-level retention and archival mapping