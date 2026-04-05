# FHIR R4 Mapping Guide for Healthcare Management System (HMS)

This document provides a comprehensive FHIR R4 interoperability mapping guide for the specified HMS entities. It outlines resource selection, element-level mapping, terminology bindings, and data conversion rules to ensure compliance with HL7 FHIR standards.

---

## 1. PatientProfile
Represents the core demographic and administrative data of a patient.

*   **FHIR R4 Resource:** `Patient`
*   **Field-to-Element Mappings:**
    *   `InternalID` → `Patient.id`
    *   `FirstName`, `LastName` → `Patient.name.given`, `Patient.name.family`
    *   `DateOfBirth` → `Patient.birthDate`
    *   `Gender` → `Patient.gender`
    *   `ContactNumber`, `Email` → `Patient.telecom` (with respective `system` values)
    *   `HomeAddress` → `Patient.address`
    *   `ActiveStatus` → `Patient.active`
*   **Required FHIR Extensions:**
    *   `http://hl7.org/fhir/us/core/StructureDefinition/us-core-race` (Race)
    *   `http://hl7.org/fhir/us/core/StructureDefinition/us-core-ethnicity` (Ethnicity)
    *   `http://hl7.org/fhir/us/core/StructureDefinition/us-core-birthsex` (Birth Sex)
*   **Terminology Bindings:**
    *   **Gender:** HL7 AdministrativeGender (`male` | `female` | `other` | `unknown`)
*   **Data Conversion Notes:** Dates must be formatted as `YYYY-MM-DD`. Telecom arrays should specify `use` (e.g., `home`, `work`, `mobile`).

---

## 2. PatientIdentifier
Represents various official identifiers (MRN, SSN, Driver's License) linked to a patient.

*   **FHIR R4 Resource:** `Patient` (Mapped to the `Patient.identifier` array)
*   **Field-to-Element Mappings:**
    *   `IDValue` → `Patient.identifier.value`
    *   `IDType` → `Patient.identifier.type.coding`
    *   `IssuingAuthority` → `Patient.identifier.system` (URI format)
    *   `ValidityPeriod` → `Patient.identifier.period`
*   **Required FHIR Extensions:** None strictly required.
*   **Terminology Bindings:**
    *   **ID Type:** HL7 v2 Identifier Type (e.g., `MR` for Medical Record Number, `SS` for Social Security Number).
*   **Data Conversion Notes:** The `system` must be a valid URI representing the assigning authority (e.g., `http://hl7.org/fhir/sid/us-ssn`).

---

## 3. Encounter
Represents an interaction between a patient and healthcare provider(s).

*   **FHIR R4 Resource:** `Encounter`
*   **Field-to-Element Mappings:**
    *   `EncounterID` → `Encounter.identifier`
    *   `PatientID` → `Encounter.subject` (Reference to `Patient`)
    *   `ProviderID` → `Encounter.participant.individual` (Reference to `Practitioner`)
    *   `StartTime`, `EndTime` → `Encounter.period.start`, `Encounter.period.end`
    *   `Status` → `Encounter.status`
    *   `ChiefComplaint` → `Encounter.reasonCode`
*   **Required FHIR Extensions:** None strictly required.
*   **Terminology Bindings:**
    *   **Status:** HL7 EncounterStatus (`planned`, `arrived`, `in-progress`, `finished`, `cancelled`).
    *   **Encounter Type:** CPT or SNOMED-CT.
    *   **Reason:** SNOMED-CT or ICD-10-CM.
*   **Data Conversion Notes:** Status transitions must follow the FHIR state machine. Timestamps must include time zones (e.g., `YYYY-MM-DDThh:mm:ss+zz:zz`).

---

## 4. ClinicalNote
Represents unstructured or semi-structured clinical documentation.

*   **FHIR R4 Resource:** `DocumentReference` (Preferred over `Composition` for raw HMS notes/PDFs).
*   **Field-to-Element Mappings:**
    *   `NoteID` → `DocumentReference.identifier`
    *   `PatientID` → `DocumentReference.subject`
    *   `AuthorID` → `DocumentReference.author`
    *   `NoteType` → `DocumentReference.type`
    *   `CreationDate` → `DocumentReference.date`
    *   `NoteContent` → `DocumentReference.content.attachment.data` (Base64) OR `DocumentReference.content.attachment.url`
*   **Required FHIR Extensions:** None strictly required.
*   **Terminology Bindings:**
    *   **Note Type:** LOINC (e.g., `11488-4` for Consultation note, `18842-5` for Discharge summary).
*   **Data Conversion Notes:** Plain text or HTML notes should be Base64 encoded in the `attachment.data` field. Ensure `contentType` (e.g., `text/plain`, `application/pdf`) is accurately populated.

---

## 5. Admission
Represents an inpatient hospital stay.

*   **FHIR R4 Resource:** `Encounter` (with `class` = `IMP` for Inpatient).
*   **Field-to-Element Mappings:**
    *   `AdmissionID` → `Encounter.identifier`
    *   `AdmitDate` → `Encounter.period.start`
    *   `DischargeDate` → `Encounter.period.end`
    *   `WardBed` → `Encounter.location.location` (Reference to `Location`)
    *   `DischargeDisposition` → `Encounter.hospitalization.dischargeDisposition`
*   **Required FHIR Extensions:** None strictly required.
*   **Terminology Bindings:**
    *   **Class:** HL7 ActEncounterCode (`IMP`).
    *   **Discharge Disposition:** NUBC (National Uniform Billing Committee) Discharge Disposition Codes.
*   **Data Conversion Notes:** Must be linked to the `Patient` and the admitting `Practitioner`. Use `hospitalization.admitSource` if the admission originated from the ER.

---

## 6. AdmissionEligibility
Represents the verification of a patient's insurance coverage for an admission.

*   **FHIR R4 Resource:** `CoverageEligibilityRequest` and `CoverageEligibilityResponse`
*   **Field-to-Element Mappings (Request):**
    *   `PatientID` → `CoverageEligibilityRequest.patient`
    *   `InsurerID` → `CoverageEligibilityRequest.insurer`
    *   `ServiceType` → `CoverageEligibilityRequest.item.category`
*   **Field-to-Element Mappings (Response):**
    *   `EligibilityStatus` → `CoverageEligibilityResponse.status`
    *   `FinancialDetails` (Copay/Deductible) → `CoverageEligibilityResponse.insurance.item.benefit`
*   **Required FHIR Extensions:** None strictly required.
*   **Terminology Bindings:**
    *   **Service Type:** X12 270/271 Service Type Codes (e.g., `30` for Health Benefit Plan Coverage).
*   **Data Conversion Notes:** This is a transactional pair. The HMS must generate the Request resource and parse the Response resource returned by the clearinghouse/payer.

---

## 7. EmergencyArrival
Represents a patient arriving at the Emergency Department.

*   **FHIR R4 Resource:** `Encounter` (with `class` = `EMER` for Emergency).
*   **Field-to-Element Mappings:**
    *   `ArrivalTime` → `Encounter.period.start`
    *   `ArrivalMode` (Ambulance, Walk-in) → `Encounter.hospitalization.admitSource`
    *   `PatientID` → `Encounter.subject`
*   **Required FHIR Extensions:**
    *   Custom extension for `TransportAgency` if arriving via EMS (if not mapped to `participant`).
*   **Terminology Bindings:**
    *   **Class:** HL7 ActEncounterCode (`EMER`).
    *   **Admit Source:** SNOMED-CT or NUBC codes.
*   **Data Conversion Notes:** If the emergency visit results in an admission, this Encounter can be linked to the Admission Encounter using `Encounter.partOf`.

---

## 8. TriageAssessment
Represents the initial clinical evaluation and acuity scoring in the ER.

*   **FHIR