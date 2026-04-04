-- Migration: DiagnosticsService initial schema
-- Schema: cl_diagnostics
-- Bounded Context: Lab results, orders, and diagnostic records

CREATE SCHEMA IF NOT EXISTS cl_diagnostics;

CREATE TABLE IF NOT EXISTS cl_diagnostics.result_record (
    id                      VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
    tenant_id               VARCHAR(64) NOT NULL,
    region_id               VARCHAR(64) NOT NULL,
    facility_id             VARCHAR(64) NOT NULL,
    patient_id              VARCHAR(32) NOT NULL,
    order_id                VARCHAR(32) NOT NULL,
    analyte_code            VARCHAR(64) NOT NULL,
    measured_value          VARCHAR(256),
    unit_code               VARCHAR(32),
    abnormal_flag           VARCHAR(16),
    critical_flag           BOOLEAN NOT NULL DEFAULT FALSE,
    result_at               TIMESTAMPTZ NOT NULL,
    recorded_by             VARCHAR(128) NOT NULL,
    classification_code     VARCHAR(64) NOT NULL DEFAULT 'clinical_restricted',
    legal_hold_flag         BOOLEAN NOT NULL DEFAULT FALSE,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              VARCHAR(128) NOT NULL,
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_by              VARCHAR(128) NOT NULL,
    version_no              INTEGER NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS idx_cl_diagnostics_rr_tenant_patient ON cl_diagnostics.result_record (tenant_id, patient_id);