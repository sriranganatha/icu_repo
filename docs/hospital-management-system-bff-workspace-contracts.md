# Hospital Management System Backend-for-Frontend Workspace Contracts

## 1. Purpose

This document defines the major workspace-level query contracts exposed by backend-for-frontend services for staff and patient channels. It exists because user experiences such as emergency boards, inpatient worklists, and physician round views should not assemble data directly from many domain services in the client.

## 2. Workspace Contract Principles

- workspace APIs are query-oriented and read-model backed
- tenant and facility context is explicit
- client payloads are optimized for low-click operational workflows
- sensitive sections are masked or omitted based on authorization
- response contracts are stable even when underlying domain services evolve

## 3. Staff Workspace Contracts

### 3.1 Emergency Board Workspace

Endpoint:

- GET /bff/v1/emergency-board

Response should include:

- waiting patients
- triaged patients
- in-room patients
- observation patients
- boarded patients
- pending diagnostics indicators
- pathway timers
- staffing summary

### 3.2 Inpatient Unit Workspace

Endpoint:

- GET /bff/v1/inpatient-units/{unitId}/workspace

Response should include:

- bed occupancy summary
- active patients with acuity markers
- medication due indicators
- discharge readiness indicators
- pending consult and result alerts
- staffing alerts

### 3.3 Physician Rounds Workspace

Endpoint:

- GET /bff/v1/rounds/workspace

Response should include:

- assigned patient list
- overnight events summary
- critical results summary
- pending documentation tasks
- AI chart summary references where enabled

### 3.4 Case Management Workspace

Endpoint:

- GET /bff/v1/case-management/workspace

Response should include:

- continued-stay review queue
- discharge barriers
- expected discharge dates
- post-acute referral tasks
- payer authorization status

### 3.5 Billing Operations Workspace

Endpoint:

- GET /bff/v1/revenue-cycle/workspace

Response should include:

- unbilled encounters
- claim backlog
- denial work queue
- payment reconciliation exceptions
- payer response metrics

## 4. Patient Workspace Contracts

### 4.1 Patient Portal Home

Endpoint:

- GET /bff/v1/patient/home

Response should include:

- next appointments
- recent results approved for release
- outstanding payments
- active reminders
- telehealth join links where applicable

### 4.2 Patient Result Detail View

Endpoint:

- GET /bff/v1/patient/results/{resultId}

Response rules:

- only released results
- no hidden internal annotations
- educational guidance controlled by policy

## 5. Multi-Tenancy Rules for BFFs

- no cross-tenant aggregation in user-facing workspaces
- tenant-specific feature flags and masking rules apply at composition time
- dedicated and shared tenants should receive the same contract shape where possible
- BFF caching must be tenant-safe and role-safe

## 6. Required Backing Read Models

- emergency board projection
- inpatient unit census projection
- physician rounds projection
- case management queue projection
- billing operations queue projection
- patient home projection

## 7. Reliability Requirements

- partial degradation should be explicit in response metadata
- stale data indicators should be returned when projections lag
- critical sections such as pathway timers and medication due lists should expose freshness metadata