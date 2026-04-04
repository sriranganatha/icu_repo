-- Row-Level Security policy for multi-tenant isolation
-- Apply to each regulated table schema

-- Enable RLS on patient_profile
ALTER TABLE cl_mpi.patient_profile ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation_patient ON cl_mpi.patient_profile
    USING (tenant_id = current_setting('app.current_tenant_id'));

ALTER TABLE cl_encounter.encounter ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation_encounter ON cl_encounter.encounter
    USING (tenant_id = current_setting('app.current_tenant_id'));

ALTER TABLE cl_inpatient.admission ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation_admission ON cl_inpatient.admission
    USING (tenant_id = current_setting('app.current_tenant_id'));

ALTER TABLE cl_emergency.emergency_arrival ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation_emergency ON cl_emergency.emergency_arrival
    USING (tenant_id = current_setting('app.current_tenant_id'));

ALTER TABLE cl_diagnostics.result_record ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation_result ON cl_diagnostics.result_record
    USING (tenant_id = current_setting('app.current_tenant_id'));

ALTER TABLE op_revenue.claim ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation_claim ON op_revenue.claim
    USING (tenant_id = current_setting('app.current_tenant_id'));

ALTER TABLE gov_audit.audit_event ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation_audit ON gov_audit.audit_event
    USING (tenant_id = current_setting('app.current_tenant_id'));

ALTER TABLE gov_ai.ai_interaction ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation_ai ON gov_ai.ai_interaction
    USING (tenant_id = current_setting('app.current_tenant_id'));