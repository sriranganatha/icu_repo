using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GNex.Database;

/// <summary>Design-time factory for EF Core CLI tools (migrations, etc.).</summary>
public class GNexDbContextFactory : IDesignTimeDbContextFactory<GNexDbContext>
{
    public GNexDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<GNexDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5419;Database=gnex_master_db;Username=gnex_master;Password=GNex@Master2024");
        return new GNexDbContext(optionsBuilder.Options, new DesignTimeTenantProvider());
    }

    private sealed class DesignTimeTenantProvider : ITenantProvider
    {
        public string TenantId => "design-time";
    }
}
