# HMS API Changelog

All notable API changes will be documented in this file.
Follows [Keep a Changelog](https://keepachangelog.com/) and [Semantic Versioning](https://semver.org/).

## [1.0.0] — YYYY-MM-DD

### Added
- **Patient Service**: CRUD endpoints for PatientProfile, PatientIdentifier, PatientContact
- **Encounter Service**: Encounter lifecycle (create, update, close, list by patient)
- **Inpatient Service**: InpatientStay, BedAssignment, NursingNote, DietaryOrder
- **Emergency Service**: EmergencyArrival, TriageAssessment, TraumaCaseLog
- **Diagnostics Service**: DiagnosticOrder, DiagnosticResult, SpecimenTracking
- **Revenue Service**: Claim, Payment, InsuranceVerification
- **Audit Service**: AuditEntry query and export
- **AI Service**: AiInteraction, CopilotSession, GovernanceLog

### Security
- X-Tenant-Id header required on all endpoints
- JWT Bearer authentication
- PHI field-level access control per HIPAA Minimum Necessary
- All PHI access logged to audit trail

### Compliance
- HIPAA Technical Safeguards (45 CFR §164.312)
- SOC 2 Type II controls (CC1-CC9)
- FHIR R4 resource compatibility annotations