-- Migration: EmergencyService initial schema
-- Schema: cl_emergency
-- Bounded Context: Emergency arrivals, triage assessments, and ED tracking

CREATE SCHEMA IF NOT EXISTS cl_emergency;

CREATE TABLE IF NOT EXISTS cl_emergency.emergency_arrival (
    id                      VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
    tenant_id               VARCHAR(64) NOT NULL,
    region_id               VARCHAR(64) NOT NULL,
    facility_id             VARCHAR(64) NOT NULL,
    patient_id              VARCHAR(32),
    temporary_identity_alias VARCHAR(128),
    arrival_mode            VARCHAR(64),
    chief_complaint         TEXT,
    handoff_source          VARCHAR(128),
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              VARCHAR(128) NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_cl_emergency_ea_tenant_facility ON cl_emergency.emergency_arrival (tenant_id, facility_id);

CREATE TABLE IF NOT EXISTS cl_emergency.triage_assessment (
    id                      VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
    tenant_id               VARCHAR(64) NOT NULL,
    arrival_id              VARCHAR(32) NOT NULL REFERENCES cl_emergency.emergency_arrival(id),
    patient_id              VARCHAR(32),
    acuity_level            VARCHAR(16) NOT NULL,
    chief_complaint         TEXT,
    vital_snapshot_json     JSONB NOT NULL DEFAULT '{}'::jsonb,
    re_triage_flag          BOOLEAN NOT NULL DEFAULT FALSE,
    pathway_recommendation  VARCHAR(64),
    performed_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    performed_by            VARCHAR(128) NOT NULL,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);