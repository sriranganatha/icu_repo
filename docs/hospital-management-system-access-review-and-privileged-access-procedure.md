# Hospital Management System Access Review and Privileged Access Procedure

## 1. Purpose

This document defines joiner, mover, leaver, periodic access review, privileged access approval, and emergency-access review procedures for the Hospital Management System.

## 2. Scope

This procedure applies to:

- Workforce users.
- Support personnel.
- Compliance and audit personnel.
- Technical administrators and integration engineers.
- AI governance reviewers.

## 3. Joiner Process

1. Manager or authorized sponsor requests access.
2. Role assignment is based on approved role catalog and facility context.
3. Privileged or sensitive access requires extra approval.
4. Access provisioning is logged and attributable.

## 4. Mover Process

1. Role or department changes trigger entitlement review.
2. Old role privileges are removed unless explicitly re-approved.
3. Coverage and delegation assignments are updated.

## 5. Leaver Process

1. Access is disabled promptly on termination or contractor end date.
2. Active sessions, API credentials, and privileged tokens are revoked.
3. Shared secrets or device credentials under the user’s control are rotated if needed.

## 6. Periodic Access Review

- Standard users: review on a defined periodic cadence.
- Privileged users: review more frequently.
- Sensitive-data permissions: review with privacy or compliance oversight.
- Review outputs include retain, modify, or remove decisions.

## 7. Privileged Access Rules

- Privileged access is limited to named roles.
- Just-in-time or short-lived elevation should be preferred where possible.
- Production access requires explicit approval and logging.
- Administrative actions must be auditable and attributable.

## 8. Break-Glass Review

- Every break-glass event enters review queue.
- Review verifies emergency need, scope, duration, and appropriateness.
- Exceptions require corrective action or disciplinary escalation according to policy.

## 9. Evidence and Reporting

- Provisioning records.
- Deprovisioning records.
- Review attestation records.
- Privileged access approvals.
- Break-glass event reviews.