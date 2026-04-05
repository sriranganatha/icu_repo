# Monitor Report
**Timestamp**: 2026-04-05 08:47:25Z

## Docker Container Status
- ICU-postgres: **running** ✓
- ICU-postgres: **Up 6 hours** ✓

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
- **ICU-postgres**: FATAL/PANIC errors detected in PostgreSQL logs

## Resource Usage
- NAME           CPU %     MEM USAGE / LIMIT   NET I/O
- ICU-postgres   0.00%     41.3MiB / 15.5GiB   2.11MB / 609kB


## Database Connectivity
- PostgreSQL at localhost:5418/icu_db: **CONNECTED**

## Summary
- Docker containers checked: 2
- Services healthy: 0/9
- Log issues detected: 1
- Total findings: 10
- Duration: 38.1s
- **Overall: 10 ISSUES DETECTED**
