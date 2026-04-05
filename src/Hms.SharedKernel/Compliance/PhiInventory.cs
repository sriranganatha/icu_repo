```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Hms.SharedKernel.Compliance
{
    /// <summary>
    /// Centralized inventory of all Protected Health Information (PHI) fields across the HMS platform.
    /// Used for HIPAA compliance auditing, data masking, logging redaction, and access control.
    /// </summary>
    public static class PhiInventory
    {
        /// <summary>
        /// A read-only mapping of Entity names to their respective PHI fields.
        /// Fields included here fall under the 18 HIPAA identifiers or contain clinical data 
        /// tied to a patient (e.g., free text notes, dates of service, account numbers).
        /// </summary>
        public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> EntityPhiMap = 
            new ReadOnlyDictionary<string, IReadOnlyList<string>>(
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    // Patient Service
                    { 
                        "PatientService.PatientProfile", 
                        new List<string> { 
                            "FirstName", "LastName", "MiddleName", "DateOfBirth", "SocialSecurityNumber", 
                            "AddressLine1", "AddressLine2", "City", "State", "ZipCode", 
                            "HomePhone", "MobilePhone", "EmailAddress", "PhotographUrl", "EmergencyContactName" 
                        }.AsReadOnly() 
                    },
                    { 
                        "PatientService.PatientIdentifier", 
                        new List<string> { 
                            "MedicalRecordNumber", "IdentifierValue", "IdentifierType", "IssuedByFacility" 
                        }.AsReadOnly() 
                    },

                    // Encounter Service
                    { 
                        "EncounterService.Encounter", 
                        new List<string> { 
                            "PatientId", "EncounterDate", "DischargeDate", "ChiefComplaint", "ReasonForVisit" 
                        }.AsReadOnly() 
                    },
                    { 
                        "EncounterService.ClinicalNote", 
                        new List<string> { 
                            "PatientId", "NoteText", "DictationAudioUrl", "AuthorId", "ServiceDate" 
                        }.AsReadOnly() 
                    },

                    // Inpatient Service
                    { 
                        "InpatientService.Admission", 
                        new List<string> { 
                            "PatientId", "AdmissionDate", "DischargeDate", "RoomNumber", "BedNumber", "AttendingPhysicianId" 
                        }.AsReadOnly() 
                    },
                    { 
                        "InpatientService.AdmissionEligibility", 
                        new List<string> { 
                            "PatientId", "SubscriberId", "GroupNumber", "GuarantorName", "GuarantorSsn", "HealthPlanBeneficiaryNumber" 
                        }.AsReadOnly() 
                    },

                    // Emergency Service
                    { 
                        "EmergencyService.EmergencyArrival", 
                        new List<string> { 
                            "PatientId", "ArrivalTime", "IncidentLocation", "TransportMethod", "EmsRunSheetNumber", "NextOfKinName", "NextOfKinPhone" 
                        }.AsReadOnly() 
                    },
                    { 
                        "EmergencyService.TriageAssessment", 
                        new List<string> { 
                            "PatientId", "TriageNotes", "VitalSigns", "AcuityLevel", "AssessmentTime" 
                        }.AsReadOnly() 
                    },

                    // Diagnostics Service
                    { 
                        "DiagnosticsService.ResultRecord", 
                        new List<string> { 
                            "PatientId", "AccessionNumber", "SpecimenCollectionDate", "ResultValue", "ClinicalInterpretation", "DeviceIdentifier" 
                        }.AsReadOnly() 
                    },

                    // Revenue Service
                    { 
                        "RevenueService.Claim", 
                        new List<string> { 
                            "PatientId", "ClaimId", "SubscriberId", "ServiceDates", "DiagnosisCodes", "ProcedureCodes", "BilledAmount", "PatientAccountNumber" 
                        }.AsReadOnly() 
                    },

                    // Audit Service
                    { 
                        "AuditService.AuditEvent", 
                        new List<string> { 
                            "PatientId", "AccessedResource", "IpAddress", "DeviceSerialNumber", "UserIdentifier", "EventTimestamp" 
                        }.AsReadOnly() 
                    },

                    // AI Service
                    { 
                        "AiService.AiInteraction", 
                        new List<string> { 
                            "PatientId", "PromptText", "ResponseText", "SessionId", "ContextWindowData", "UserIpAddress" 
                        }.AsReadOnly() 
                    }
                });

        /// <summary>
        /// Retrieves the list of PHI fields for a given entity.
        /// </summary>
        /// <param name="entityName">The fully qualified entity name (e.g., "PatientService.PatientProfile")</param>
        /// <returns>A list of PHI fields, or an empty list if the entity is not found.</returns>
        public static IReadOnlyList<string> GetPhiFieldsForEntity(string entityName)
        {
            if (string.IsNullOrWhiteSpace(entityName))
                return Array.Empty<string>();

            return EntityPhiMap.TryGetValue(entityName, out var fields) 
                ? fields 
                : Array.Empty<string>();
        }

        /// <summary>
        /// Checks if a specific field on a specific entity is classified as PHI.
        /// </summary>
        /// <param name="entityName">The fully qualified entity name.</param>
        /// <param name="fieldName">The name of the field/property.</param>
        /// <returns>True if the field is PHI, otherwise false.</returns>
        public static bool IsPhiField(string entityName, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(entityName) || string.IsNullOrWhiteSpace(fieldName))
                return false;

            if (EntityPhiMap.TryGetValue(entityName, out var fields))
            {
                return fields.Contains(fieldName, StringComparer.OrdinalIgnoreCase);
            }

            return false;
        }

        /// <summary>
        /// Gets all entities that contain at least one PHI field.
        /// </summary>
        public static IEnumerable<string> GetAllEntitiesWithPhi()
        {
            return EntityPhiMap.Keys;
        }
    }
}
```