using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Platform.Technology;

public class DatabaseTechnology : PlatformEntityBase
{
    [Required] public string Name { get; set; } = null!;
    [Required] public string DbType { get; set; } = null!; // relational | nosql | graph | vector | timeseries
    public int DefaultPort { get; set; }
    public string? ConnectionTemplate { get; set; } // e.g. "Host={host};Port={port};Database={db};..."
}
