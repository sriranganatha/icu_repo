# Hospital Management System Priority Service Contracts

## 1. Purpose

This document provides implementation-ready contract guidance for the first priority bounded contexts:

- Master Patient Identity
- Encounter and Charting
- Inpatient and ADT
- Emergency
- Revenue Cycle

It complements the platform-wide API specification by defining concrete contract expectations, request and response shapes, authorization scope, audit behavior, and tenancy rules.

## 2. Contract Conventions

- All endpoints are versioned under /api/v1.
- All protected requests require authenticated identity.
- All regulated requests are tenant-aware.
- Commands return explicit workflow state and audit correlation identifiers.
- Sensitive responses should omit unnecessary nested clinical detail.

## 3. Master Patient Identity Service

### 3.1 Create Patient

Endpoint:

- POST /api/v1/patients

Authorization:

- registrar.create-patient or equivalent scoped permission.

Request shape:

```json
{
  "facilityId": "fac_001",
  "demographics": {
    "legalName": {
      "given": "Asha",
      "family": "Patel"
    },
    "dateOfBirth": "1987-02-14",
    "sexAtBirth": "female",
    "primaryLanguage": "en"
  },
  "identifiers": [
    {
      "type": "national_id",
      "value": "masked-or-tokenized-ref"
    }
  ],
  "contacts": [
    {
      "type": "mobile",
      "value": "+1-555-0100"
    }
  ]
}
```

Response shape:

```json
{
  "patientId": "pat_123",
  "tenantId": "ten_001",
  "facilityId": "fac_001",
  "enterprisePersonKey": "epk_001",
  "status": "active",
  "auditTraceId": "aud_001"
}
```

Audit requirements:

- record create event
- record actor identity
- record source channel

Tenancy requirements:

- identifiers unique within tenant scope unless explicitly modeled otherwise
- search-before-create workflow recommended to reduce duplicates

### 3.2 Merge Patients

Endpoint:

- POST /api/v1/patient-identity-merges

Required controls:

- elevated approval
- dual review in high-risk tenants if configured
- full audit with source and target patient references

## 4. Encounter and Charting Service

### 4.1 Create Encounter

Endpoint:

- POST /api/v1/encounters

Request shape:

```json
{
  "patientId": "pat_123",
  "facilityId": "fac_001",
  "encounterType": "opd",
  "sourcePathway": "scheduled_appointment",
  "attendingProviderRef": "usr_100"
}
```

Response shape:

```json
{
  "encounterId": "enc_123",
  "status": "active",
  "startAt": "2026-04-04T10:12:00Z",
  "auditTraceId": "aud_010"
}
```

### 4.2 Append Clinical Note

Endpoint:

- POST /api/v1/encounters/{encounterId}/notes

Request shape:

```json
{
  "noteType": "soap",
  "classification": "clinical_standard",
  "content": {
    "subjective": "Patient reports fatigue.",
    "objective": "Vitals stable.",
    "assessment": "Further workup pending.",
    "plan": "Order CBC and CMP."
  },
  "aiAssistRef": null
}
```

Response requirements:

- return note id and version
- return chart state if workflow requires it
- include AI disclosure reference if AI contributed

## 5. Inpatient and ADT Service

### 5.1 Evaluate Admission Eligibility

Endpoint:

- POST /api/v1/admission-eligibility-evaluations

Request shape:

```json
{
  "patientId": "pat_123",
  "facilityId": "fac_001",
  "encounterId": "enc_200",
  "candidateClass": "inpatient",
  "clinicalBasis": {
    "acuityLevel": "high",
    "vitalsSummary": "tachycardia",
    "biomarkerRefs": ["res_100", "res_101"],
    "physicianAssessment": "requires overnight monitoring"
  },
  "payerContext": {
    "payerRef": "pay_001",
    "authorizationStatus": "pending"
  }
}
```

Response shape:

```json
{
  "evaluationId": "aev_001",
  "decision": "approved",
  "recommendedClass": "observation",
  "overrideRequired": false,
  "auditTraceId": "aud_020"
}
```

Audit requirements:

- rationale captured
- biomarker and payer evidence references retained
- override and approver tracked if changed

### 5.2 Create Admission

Endpoint:

- POST /api/v1/admissions

Required controls:

- validated patient and encounter tenant scope
- bed-assignment policy checks
- emit admission.created event on success

## 6. Emergency Service

### 6.1 Register Emergency Arrival

Endpoint:

- POST /api/v1/emergency-arrivals

Request shape:

```json
{
  "facilityId": "fac_001",
  "arrivalMode": "ambulance",
  "temporaryIdentity": {
    "unknownPatient": true,
    "displayAlias": "Unknown Male 01"
  },
  "chiefComplaint": "chest pain",
  "handoffSource": "ems"
}
```

Behavior requirements:

- support temporary identity
- avoid blocking on full registration
- emit emergency.arrival-registered.v1

### 6.2 Record Triage Assessment

Endpoint:

- POST /api/v1/emergency-triage-assessments

Response should include:

- triage id
- acuity level
- next queue state
- pathway recommendation if configured

## 7. Revenue Cycle Service

### 7.1 Post Charge

Endpoint:

- POST /api/v1/charges

Request shape:

```json
{
  "patientId": "pat_123",
  "encounterRef": "enc_123",
  "chargeType": "room_charge",
  "quantity": 1,
  "unitAmount": 1500,
  "currency": "USD",
  "serviceDate": "2026-04-04"
}
```

Controls:

- role segregation
- tenant-scoped contract pricing resolution
- idempotency required for integration-originated charges

### 7.2 Submit Claim

Endpoint:

- POST /api/v1/claims

Response should include:

- claim id
- payer reference
- initial status
- submission timestamp
- reconciliation correlation id

## 8. Contract-Level Audit Requirements

Every priority service endpoint that changes state should produce:

- audit trace id in response
- actor and channel attribution
- tenant and facility attribution
- before or after state reference as appropriate
- correlation id for downstream events

## 9. Contract-Level Multi-Tenancy Requirements

- all path and body references must be validated within tenant scope
- no cross-tenant dereference allowed through opaque ids
- control-plane administrative APIs must be separated from tenant-serving data-plane APIs
- dedicated-tenant deployments still return tenant-aware metadata for observability and auditing

## 10. Next Contract Deliverables

- OpenAPI YAML per priority service
- JSON schema catalog for shared payload components
- event schema catalog for published domain events