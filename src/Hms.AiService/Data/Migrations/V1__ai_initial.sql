-- Migration: AiService initial schema
-- Schema: gov_ai
-- Bounded Context: AI interaction governance, model versioning, human-in-the-loop tracking

CREATE SCHEMA IF NOT EXISTS gov_ai;

CREATE TABLE IF NOT EXISTS gov_ai.ai_interaction (
    id                      VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
    tenant_id               VARCHAR(64) NOT NULL,
    region_id               VARCHAR(64) NOT NULL,
    facility_id             VARCHAR(64),
    interaction_type        VARCHAR(64) NOT NULL,
    encounter_id            VARCHAR(32),
    patient_id              VARCHAR(32),
    model_version           VARCHAR(64) NOT NULL,
    prompt_version          VARCHAR(64) NOT NULL,
    input_summary_json      JSONB,
    output_summary_json     JSONB,
    outcome_code            VARCHAR(32) NOT NULL,
    accepted_by             VARCHAR(128),
    rejected_by             VARCHAR(128),
    override_reason         TEXT,
    classification_code     VARCHAR(64) NOT NULL DEFAULT 'ai_evidence',
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              VARCHAR(128) NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_gov_ai_ai_tenant_encounter ON gov_ai.ai_interaction (tenant_id, encounter_id);