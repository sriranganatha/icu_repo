# Hospital Management System Vendor Risk and Business Associate Governance

## 1. Purpose

This document defines the onboarding, risk review, contractual governance, and ongoing oversight process for vendors, subprocessors, integration partners, and AI providers that may handle PHI, regulated operational data, or critical hospital workflows.

## 2. Vendor Categories

- Cloud infrastructure and hosting vendors.
- Identity and authentication vendors.
- Payment and financial processing vendors.
- Clinical integration vendors such as labs, imaging, device platforms, and RPM providers.
- Messaging and notification vendors.
- AI and model-serving vendors.
- Support and managed service providers.

## 3. Risk Tiers

### Tier 1: High Risk

- Stores, processes, or can access PHI.
- Supports critical care or patient-safety workflow.
- Handles identity, secrets, AI inference, or regulated data export.

### Tier 2: Medium Risk

- Processes sensitive operational or financial data.
- Affects important but not immediately critical workflow.

### Tier 3: Lower Risk

- Limited operational dependency and no PHI handling.

## 4. Onboarding Requirements

Before onboarding a Tier 1 or Tier 2 vendor:

- Complete security and privacy questionnaire.
- Review architecture and data flow.
- Review regional hosting and data residency capabilities.
- Confirm encryption, access control, logging, and incident notification posture.
- Confirm subcontractor and data-subprocessor transparency.
- Execute required contractual terms, including BAA where applicable.

## 5. Business Associate Governance

- Any vendor handling PHI in a manner requiring business associate treatment must have approved contractual terms before production use.
- The agreement must address permitted use, disclosures, safeguards, subcontractors, incident notification, retention, deletion, and audit cooperation.
- The vendor inventory must record agreement status, renewal date, services used, and data categories handled.

## 6. AI Vendor Requirements

- AI providers must disclose PHI handling boundaries, training use restrictions, logging behavior, retention behavior, and regional processing options.
- Production use with PHI requires approved contract terms and governance review.
- Retrieval, prompt, and output logging behavior must be understood and consistent with internal policy.

## 7. Ongoing Oversight

- Review Tier 1 vendors on a regular cadence.
- Reassess after major service changes, new data flows, or incidents.
- Track certifications, attestations, penetration testing summaries, and policy changes where appropriate.
- Maintain incident-notification contacts and escalation expectations.

## 8. Offboarding and Termination

- Remove integrations and credentials.
- Revoke access and shared secrets.
- Confirm data return, destruction, or archival obligations.
- Retain evidence of offboarding completion.

## 9. Vendor Register Minimum Fields

- Vendor name.
- Service description.
- Risk tier.
- Data classes handled.
- PHI involvement.
- Regions used.
- Contract and BAA status.
- Owner.
- Review cadence.
- Last review date.
- Incident contact.