using System.ComponentModel.DataAnnotations;

namespace Hms.Database.Entities.Platform.Technology;

public class ApiProtocol : PlatformEntityBase
{
    [Required] public string Name { get; set; } = null!; // REST | GraphQL | gRPC | WebSocket | SOAP
    public string? SpecFormat { get; set; } // OpenAPI | GraphQL SDL | protobuf | AsyncAPI
}
