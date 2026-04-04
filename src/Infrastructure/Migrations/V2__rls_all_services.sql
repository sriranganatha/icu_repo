-- Row-Level Security policies for all microservices
-- Ensures tenant isolation at the database level

ALTER TABLE cl_mpi.patient_profile ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation_patient_profile ON cl_mpi.patient_profile;
CREATE POLICY tenant_isolation_patient_profile ON cl_mpi.patient_profile
    USING (tenant_id = current_setting('app.current_tenant_id', true));

ALTER TABLE cl_mpi.patient_identifier ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation_patient_identifier ON cl_mpi.patient_identifier;
CREATE POLICY tenant_isolation_patient_identifier ON cl_mpi.patient_identifier
    USING (tenant_id = current_setting('app.current_tenant_id', true));

ALTER TABLE cl_encounter.encounter ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation_encounter ON cl_encounter.encounter;
CREATE POLICY tenant_isolation_encounter ON cl_encounter.encounter
    USING (tenant_id = current_setting('app.current_tenant_id', true));

ALTER TABLE cl_encounter.clinical_note ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation_clinical_note ON cl_encounter.clinical_note;
CREATE POLICY tenant_isolation_clinical_note ON cl_encounter.clinical_note
    USING (tenant_id = current_setting('app.current_tenant_id', true));

ALTER TABLE cl_inpatient.admission ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation_admission ON cl_inpatient.admission;
CREATE POLICY tenant_isolation_admission ON cl_inpatient.admission
    USING (tenant_id = current_setting('app.current_tenant_id', true));

ALTER TABLE cl_inpatient.admission_eligibility ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation_admission_eligibility ON cl_inpatient.admission_eligibility;
CREATE POLICY tenant_isolation_admission_eligibility ON cl_inpatient.admission_eligibility
    USING (tenant_id = current_setting('app.current_tenant_id', true));

ALTER TABLE cl_emergency.emergency_arrival ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation_emergency_arrival ON cl_emergency.emergency_arrival;
CREATE POLICY tenant_isolation_emergency_arrival ON cl_emergency.emergency_arrival
    USING (tenant_id = current_setting('app.current_tenant_id', true));

ALTER TABLE cl_emergency.triage_assessment ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation_triage_assessment ON cl_emergency.triage_assessment;
CREATE POLICY tenant_isolation_triage_assessment ON cl_emergency.triage_assessment
    USING (tenant_id = current_setting('app.current_tenant_id', true));

ALTER TABLE cl_diagnostics.result_record ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation_result_record ON cl_diagnostics.result_record;
CREATE POLICY tenant_isolation_result_record ON cl_diagnostics.result_record
    USING (tenant_id = current_setting('app.current_tenant_id', true));

ALTER TABLE op_revenue.claim ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation_claim ON op_revenue.claim;
CREATE POLICY tenant_isolation_claim ON op_revenue.claim
    USING (tenant_id = current_setting('app.current_tenant_id', true));

ALTER TABLE gov_audit.audit_event ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation_audit_event ON gov_audit.audit_event;
CREATE POLICY tenant_isolation_audit_event ON gov_audit.audit_event
    USING (tenant_id = current_setting('app.current_tenant_id', true));

ALTER TABLE gov_ai.ai_interaction ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation_ai_interaction ON gov_ai.ai_interaction;
CREATE POLICY tenant_isolation_ai_interaction ON gov_ai.ai_interaction
    USING (tenant_id = current_setting('app.current_tenant_id', true));