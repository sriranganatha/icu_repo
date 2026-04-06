# User-Submitted Requirements (2026-04-05 18:23:44 UTC)

## Add retry-safe patient update API

Ensure update endpoint is idempotent and auditable with clear validation errors.

### Acceptance Criteria
- Given a valid update request, when the same request is retried, then the system applies it once and returns consistent response
- Given invalid demographics, when update is submitted, then validation errors are returned with actionable messages

**Tags:** api, service

