using Xunit;

namespace Hms.Tests.Services;

public class AuditEventServiceTests
{
    [Fact]
    public async Task GetById_ReturnsNull_WhenNotFound()
    {
        // Arrange — TODO: wire mock repository
        // Act
        // Assert
        await Task.CompletedTask;
        Assert.True(true, "Stub — implement when service layer is wired");
    }

    [Fact]
    public async Task Create_ReturnsDtoWithId()
    {
        await Task.CompletedTask;
        Assert.True(true, "Stub — implement when service layer is wired");
    }

    [Fact]
    public void TenantId_IsRequired_OnAllEntities()
    {
        // Verify entity has TenantId property
        Assert.True(true, "Stub — reflect on entity to confirm TenantId");
    }
}