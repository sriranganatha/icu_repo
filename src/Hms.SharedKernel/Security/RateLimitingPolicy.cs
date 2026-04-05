using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.RateLimiting;

namespace Hms.SharedKernel.Security;

/// <summary>
/// Configures rate limiting policies per tenant to prevent abuse and DoS.
/// Clinical APIs: 100 req/min. Search/List: 30 req/min. Auth: 10 req/min.
/// </summary>
public static class RateLimitingPolicy
{
    public const string Clinical = "clinical";
    public const string Search = "search";
    public const string Auth = "auth";

    public static IServiceCollection AddHmsRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.AddFixedWindowLimiter(Clinical, opt =>
            {
                opt.PermitLimit = 100;
                opt.Window = TimeSpan.FromMinutes(1);
                opt.QueueLimit = 10;
                opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });

            options.AddFixedWindowLimiter(Search, opt =>
            {
                opt.PermitLimit = 30;
                opt.Window = TimeSpan.FromMinutes(1);
                opt.QueueLimit = 5;
            });

            options.AddFixedWindowLimiter(Auth, opt =>
            {
                opt.PermitLimit = 10;
                opt.Window = TimeSpan.FromMinutes(1);
                opt.QueueLimit = 2;
            });

            options.RejectionStatusCode = 429;
        });

        return services;
    }
}