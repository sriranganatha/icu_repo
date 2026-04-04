-- Migration: RevenueService initial schema
-- Schema: op_revenue
-- Bounded Context: Claims, billing, payer reconciliation

CREATE SCHEMA IF NOT EXISTS op_revenue;

CREATE TABLE IF NOT EXISTS op_revenue.claim (
    id                      VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
    tenant_id               VARCHAR(64) NOT NULL,
    region_id               VARCHAR(64) NOT NULL,
    facility_id             VARCHAR(64) NOT NULL,
    patient_id              VARCHAR(32) NOT NULL,
    encounter_ref           VARCHAR(32) NOT NULL,
    payer_ref               VARCHAR(128) NOT NULL,
    claim_status            VARCHAR(32) NOT NULL,
    billed_amount           NUMERIC(14,2) NOT NULL DEFAULT 0,
    allowed_amount          NUMERIC(14,2),
    classification_code     VARCHAR(64) NOT NULL DEFAULT 'financial_sensitive',
    legal_hold_flag         BOOLEAN NOT NULL DEFAULT FALSE,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              VARCHAR(128) NOT NULL,
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_by              VARCHAR(128) NOT NULL,
    version_no              INTEGER NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS idx_op_revenue_claim_tenant_patient ON op_revenue.claim (tenant_id, patient_id);