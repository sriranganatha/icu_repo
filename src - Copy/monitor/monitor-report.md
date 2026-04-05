# Monitor Report
**Timestamp**: 2026-04-05 15:39:45Z

## Docker Container Status
- ICU-postgres: **running** ✓
- ICU-postgres: **Up 13 hours** ✓

## Service Health Checks
- PatientService (:5101): **UNHEALTHY** (HTTP 0)
- EncounterService (:5102): **UNHEALTHY** (HTTP 0)
- InpatientService (:5103): **UNHEALTHY** (HTTP 0)
- EmergencyService (:5104): **UNHEALTHY** (HTTP 0)
- DiagnosticsService (:5105): **UNHEALTHY** (HTTP 0)
- RevenueService (:5106): **UNHEALTHY** (HTTP 0)
- AuditService (:5107): **UNHEALTHY** (HTTP 0)
- AiService (:5108): **UNHEALTHY** (HTTP 0)
- ApiGateway (:5100): **UNHEALTHY** (HTTP 0)

## Log Analysis
- No critical error patterns detected in logs.

## Resource Usage
- NAME           CPU %     MEM USAGE / LIMIT    NET I/O
- ICU-postgres   0.03%     44.76MiB / 15.5GiB   2.73MB / 799kB


## Database Connectivity
- PostgreSQL at localhost:5418/icu_db: **CONNECTED**

## Summary
- Docker containers checked: 2
- Services healthy: 0/9
- Log issues detected: 0
- Total findings: 9
- Duration: 38.0s
- **Overall: 9 ISSUES DETECTED**
