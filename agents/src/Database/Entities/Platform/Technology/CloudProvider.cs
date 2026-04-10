using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Platform.Technology;

public class CloudProvider : PlatformEntityBase
{
    [Required] public string Name { get; set; } = null!;
    [Required] public string RegionsJson { get; set; } = "[]"; // e.g. ["us-east-1","eu-west-1"]
    [Required] public string ServicesJson { get; set; } = "[]"; // e.g. ["EC2","S3","Lambda"]
}
