-- Migration: InpatientService initial schema
-- Schema: cl_inpatient
-- Bounded Context: Admission, discharge, transfer (ADT) and bed management

CREATE SCHEMA IF NOT EXISTS cl_inpatient;

CREATE TABLE IF NOT EXISTS cl_inpatient.admission (
    id                      VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
    tenant_id               VARCHAR(64) NOT NULL,
    region_id               VARCHAR(64) NOT NULL,
    facility_id             VARCHAR(64) NOT NULL,
    patient_id              VARCHAR(32) NOT NULL,
    encounter_id            VARCHAR(32) NOT NULL,
    admit_class             VARCHAR(64) NOT NULL,
    admit_source            VARCHAR(64),
    status_code             VARCHAR(32) NOT NULL DEFAULT 'active',
    expected_discharge_at   TIMESTAMPTZ,
    utilization_status_code VARCHAR(32),
    classification_code     VARCHAR(64) NOT NULL DEFAULT 'clinical_restricted',
    legal_hold_flag         BOOLEAN NOT NULL DEFAULT FALSE,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              VARCHAR(128) NOT NULL,
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_by              VARCHAR(128) NOT NULL,
    version_no              INTEGER NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS idx_cl_inpatient_adm_tenant_patient ON cl_inpatient.admission (tenant_id, patient_id);

CREATE TABLE IF NOT EXISTS cl_inpatient.admission_eligibility (
    id                      VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
    tenant_id               VARCHAR(64) NOT NULL,
    facility_id             VARCHAR(64) NOT NULL,
    patient_id              VARCHAR(32) NOT NULL,
    encounter_id            VARCHAR(32) NOT NULL,
    candidate_class         VARCHAR(64),
    decision_code           VARCHAR(32) NOT NULL,
    rationale_json          JSONB,
    payer_authorization_status VARCHAR(32),
    override_flag           BOOLEAN NOT NULL DEFAULT FALSE,
    approved_by             VARCHAR(128),
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              VARCHAR(128) NOT NULL
);