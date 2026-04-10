using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hms.Database;

/// <summary>Design-time factory for EF Core CLI tools (migrations, etc.).</summary>
public class HmsDbContextFactory : IDesignTimeDbContextFactory<HmsDbContext>
{
    public HmsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<HmsDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5419;Database=icu_master_db;Username=postgres;Password=postgres");
        return new HmsDbContext(optionsBuilder.Options, new DesignTimeTenantProvider());
    }

    private sealed class DesignTimeTenantProvider : ITenantProvider
    {
        public string TenantId => "design-time";
    }
}
