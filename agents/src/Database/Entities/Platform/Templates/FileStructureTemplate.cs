using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Platform.Technology;

public class FileStructureTemplate : PlatformEntityBase
{
    [Required] public string Name { get; set; } = null!;
    public string? FrameworkId { get; set; }
    [Required] public string TreeJson { get; set; } = "{}"; // nested folder/file structure

    public Framework? Framework { get; set; }
}
