-- Migration: AuditService initial schema
-- Schema: gov_audit
-- Bounded Context: Immutable audit trail for compliance (HIPAA, SOC2)

CREATE SCHEMA IF NOT EXISTS gov_audit;

CREATE TABLE IF NOT EXISTS gov_audit.audit_event (
    id                      VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
    tenant_id               VARCHAR(64) NOT NULL,
    region_id               VARCHAR(64) NOT NULL,
    facility_id             VARCHAR(64),
    event_type              VARCHAR(64) NOT NULL,
    entity_type             VARCHAR(64) NOT NULL,
    entity_id               VARCHAR(128) NOT NULL,
    actor_type              VARCHAR(32) NOT NULL,
    actor_id                VARCHAR(128) NOT NULL,
    correlation_id          VARCHAR(128) NOT NULL,
    classification_code     VARCHAR(64) NOT NULL,
    occurred_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
    payload_json            JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_gov_audit_audit_tenant_entity ON gov_audit.audit_event (tenant_id, entity_type, entity_id);
CREATE INDEX IF NOT EXISTS idx_gov_audit_audit_correlation ON gov_audit.audit_event (correlation_id);