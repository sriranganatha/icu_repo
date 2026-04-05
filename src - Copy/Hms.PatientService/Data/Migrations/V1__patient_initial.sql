-- Migration: PatientService initial schema
-- Schema: cl_mpi
-- Bounded Context: Master Patient Index — patient demographics, identifiers, and matching

CREATE SCHEMA IF NOT EXISTS cl_mpi;

CREATE TABLE IF NOT EXISTS cl_mpi.patient_profile (
    id                      VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
    tenant_id               VARCHAR(64) NOT NULL,
    region_id               VARCHAR(64) NOT NULL,
    facility_id             VARCHAR(64),
    enterprise_person_key   VARCHAR(128) NOT NULL,
    legal_given_name        VARCHAR(256) NOT NULL,
    legal_family_name       VARCHAR(256) NOT NULL,
    preferred_name          VARCHAR(256),
    date_of_birth           DATE NOT NULL,
    sex_at_birth            VARCHAR(16),
    primary_language        VARCHAR(16),
    status_code             VARCHAR(32) NOT NULL DEFAULT 'active',
    classification_code     VARCHAR(64) NOT NULL DEFAULT 'clinical_restricted',
    legal_hold_flag         BOOLEAN NOT NULL DEFAULT FALSE,
    source_system           VARCHAR(64),
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              VARCHAR(128) NOT NULL,
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_by              VARCHAR(128) NOT NULL,
    version_no              INTEGER NOT NULL DEFAULT 1
);
CREATE UNIQUE INDEX IF NOT EXISTS idx_cl_mpi_patient_tenant_epk ON cl_mpi.patient_profile (tenant_id, enterprise_person_key);

CREATE TABLE IF NOT EXISTS cl_mpi.patient_identifier (
    id                      VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
    tenant_id               VARCHAR(64) NOT NULL,
    patient_id              VARCHAR(32) NOT NULL REFERENCES cl_mpi.patient_profile(id),
    identifier_type         VARCHAR(64) NOT NULL,
    identifier_value_hash   VARCHAR(256) NOT NULL,
    issuer                  VARCHAR(128),
    status_code             VARCHAR(32) NOT NULL DEFAULT 'active',
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX IF NOT EXISTS idx_cl_mpi_pid_type_hash ON cl_mpi.patient_identifier (tenant_id, identifier_type, identifier_value_hash);