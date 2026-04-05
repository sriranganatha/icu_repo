Here is a C# implementation of an `AccessReviewService` designed to help meet SOC 2 Common Criteria (specifically CC6.1 and CC6.3 regarding logical access and periodic reviews). 

This code provides a structured way to extract user entitlements, identify risks (dormant and highly privileged accounts), and generate a compliance report.

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hms.SharedKernel.Compliance.Soc2
{
    // --- Models ---

    public class UserAccount
    {
        public string UserId { get; set; }
        public string Username { get; set; }
        public DateTime? LastLoginDate { get; set; }
        public bool IsActive { get; set; }
        public List<Role> Roles { get; set; } = new List<Role>();
    }

    public class Role
    {
        public string RoleId { get; set; }
        public string RoleName { get; set; }
        public bool IsHighlyPrivileged { get; set; } // e.g., Admin, SuperUser, System
    }

    public class UserRoleMapping
    {
        public string Username { get; set; }
        public IEnumerable<string> AssignedRoles { get; set; }
    }

    public class AccessReviewReport
    {
        public DateTime ReportGeneratedAt { get; set; }
        public string ReportingPeriod { get; set; }
        public List<UserRoleMapping> AllUserMappings { get; set; } = new List<UserRoleMapping>();
        public List<UserAccount> DormantAccounts { get; set; } = new List<UserAccount>();
        public List<UserAccount> OverPrivilegedAccounts { get; set; } = new List<UserAccount>();
        public int TotalActiveUsers { get; set; }
    }

    // --- Interfaces ---

    public interface IUserRepository
    {
        Task<IEnumerable<UserAccount>> GetAllActiveUsersAsync();
    }

    public interface IAccessReviewService
    {
        Task<AccessReviewReport> GenerateQuarterlyAccessReviewAsync();
    }

    // --- Service Implementation ---

    public class AccessReviewService : IAccessReviewService
    {
        private readonly IUserRepository _userRepository;
        private const int DormancyThresholdDays = 90;

        public AccessReviewService(IUserRepository userRepository)
        {
            _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        }

        public async Task<AccessReviewReport> GenerateQuarterlyAccessReviewAsync()
        {
            var users = (await _userRepository.GetAllActiveUsersAsync()).ToList();
            var reportDate = DateTime.UtcNow;

            var report = new AccessReviewReport
            {
                ReportGeneratedAt = reportDate,
                ReportingPeriod = GetQuarter(reportDate),
                TotalActiveUsers = users.Count
            };

            foreach (var user in users)
            {
                // 1. List all user -> role mappings
                report.AllUserMappings.Add(new UserRoleMapping
                {
                    Username = user.Username,
                    AssignedRoles = user.Roles.Select(r => r.RoleName)
                });

                // 2. Flag dormant accounts (>90 days)
                if (IsDormant(user, reportDate))
                {
                    report.DormantAccounts.Add(user);
                }

                // 3. Flag over-privileged users
                if (IsOverPrivileged(user))
                {
                    report.OverPrivilegedAccounts.Add(user);
                }
            }

            return report;
        }

        /// <summary>
        /// Determines if an account has not been logged into within the dormancy threshold.
        /// </summary>
        private bool IsDormant(UserAccount user, DateTime currentDate)
        {
            if (!user.LastLoginDate.HasValue)
            {
                // If they have never logged in, they might be a newly provisioned user.
                // In a real-world scenario, you would check CreatedDate vs CurrentDate here.
                return false; 
            }

            var daysSinceLastLogin = (currentDate - user.LastLoginDate.Value).TotalDays;
            return daysSinceLastLogin > DormancyThresholdDays;
        }

        /// <summary>
        /// Determines if a user has excessive privileges. 
        /// This logic can be expanded based on specific organizational RBAC matrices.
        /// </summary>
        private bool IsOverPrivileged(UserAccount user)
        {
            // Example Rule 1: User has multiple highly privileged roles (Separation of Duties violation)
            var highPrivilegeCount = user.Roles.Count(r => r.IsHighlyPrivileged);
            if (highPrivilegeCount > 1)
            {
                return true;
            }

            // Example Rule 2: A standard user account (non-service account) holding a "GlobalAdmin" role
            // This is a placeholder for custom business logic.
            if (user.Roles.Any(r => r.RoleName.Equals("GlobalAdmin", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Helper to format the reporting period (e.g., "Q3 2023")
        /// </summary>
        private string GetQuarter(DateTime date)
        {
            int quarter = (date.Month - 1) / 3 + 1;
            return $"Q{quarter} {date.Year}";
        }
    }
}
```

### SOC 2 Compliance Notes for this Implementation:

1. **CC6.1 (Logical Access):** The `AllUserMappings` list provides the necessary artifact for auditors to prove that you have a complete inventory of who has access to what within the system.
2. **CC6.2 (Offboarding/Dormancy):** The `DormantAccounts` list automates the detection of users who may have left the company or changed roles but whose access was not properly revoked. SOC 2 auditors look favorably on automated >90-day dormancy checks.
3. **CC6.3 (Role-Based Access Control & Least Privilege):** The `IsOverPrivileged` method acts as a continuous monitoring control. You should customize this method to reflect your organization's specific Separation of Duties (SoD) matrix (e.g., ensuring a user cannot hold both "BillingAdmin" and "ClinicalDataAdmin" roles simultaneously).
4. **Evidence Generation:** To complete the workflow, you would typically inject an `IEmailService` or `ITicketingService` (like Jira) to automatically send this `AccessReviewReport` to the Security/Compliance team on the first day of every quarter, requiring them to sign off on it.