-- Migration: EncounterService initial schema
-- Schema: cl_encounter
-- Bounded Context: Clinical encounters, clinical notes, and visit tracking

CREATE SCHEMA IF NOT EXISTS cl_encounter;

CREATE TABLE IF NOT EXISTS cl_encounter.encounter (
    id                      VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
    tenant_id               VARCHAR(64) NOT NULL,
    region_id               VARCHAR(64) NOT NULL,
    facility_id             VARCHAR(64) NOT NULL,
    patient_id              VARCHAR(32) NOT NULL,
    encounter_type          VARCHAR(64) NOT NULL,
    source_pathway          VARCHAR(64),
    attending_provider_ref  VARCHAR(128),
    start_at                TIMESTAMPTZ NOT NULL,
    end_at                  TIMESTAMPTZ,
    status_code             VARCHAR(32) NOT NULL DEFAULT 'active',
    classification_code     VARCHAR(64) NOT NULL DEFAULT 'clinical_restricted',
    legal_hold_flag         BOOLEAN NOT NULL DEFAULT FALSE,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              VARCHAR(128) NOT NULL,
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_by              VARCHAR(128) NOT NULL,
    version_no              INTEGER NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS idx_cl_encounter_enc_tenant_patient ON cl_encounter.encounter (tenant_id, patient_id);

CREATE TABLE IF NOT EXISTS cl_encounter.clinical_note (
    id                      VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
    tenant_id               VARCHAR(64) NOT NULL,
    encounter_id            VARCHAR(32) NOT NULL,
    patient_id              VARCHAR(32) NOT NULL,
    note_type               VARCHAR(64) NOT NULL,
    note_classification_code VARCHAR(64),
    content_json            JSONB NOT NULL DEFAULT '{}'::jsonb,
    ai_interaction_id       VARCHAR(32),
    authored_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
    authored_by             VARCHAR(128) NOT NULL,
    amended_from_note_id    VARCHAR(32),
    version_no              INTEGER NOT NULL DEFAULT 1,
    legal_hold_flag         BOOLEAN NOT NULL DEFAULT FALSE,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_cl_encounter_note_encounter ON cl_encounter.clinical_note (tenant_id, encounter_id);