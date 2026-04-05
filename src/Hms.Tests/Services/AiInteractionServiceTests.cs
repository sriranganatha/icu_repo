using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;
using Hms.AiService.Contracts;
using Hms.AiService.Data.Entities;
using Hms.AiService.Data.Repositories;
using Hms.AiService.Kafka;
using Hms.AiService.Services;
using Xunit;

namespace Hms.Tests.Services;

/// <summary>
/// Unit tests for AiInteractionService.
/// Feature coverage: EP-P1, Module-P, AI
/// </summary>
public class AiInteractionServiceTests
{
    private readonly Mock<IAiInteractionRepository> _repoMock = new();
    private readonly Mock<AiServiceEventProducer> _eventsMock;
    private readonly AiInteractionService _sut;

    public AiInteractionServiceTests()
    {
        _eventsMock = new Mock<AiServiceEventProducer>(
            MockBehavior.Loose, null!, null!);
        _sut = new AiInteractionService(
            _repoMock.Object,
            _eventsMock.Object,
            Mock.Of<ILogger<AiInteractionService>>());
    }

    private static AiInteraction CreateTestEntity() => new()
    {

    };

    [Fact]
    public async Task GetById_ReturnsNull_WhenNotFound()
    {
        _repoMock.Setup(r => r.GetByIdAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AiInteraction?)null);

        var result = await _sut.GetByIdAsync("missing");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetById_ReturnsDtoWithAllFields_WhenFound()
    {
        var entity = CreateTestEntity();
        _repoMock.Setup(r => r.GetByIdAsync(entity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        var result = await _sut.GetByIdAsync(entity.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(entity.Id);
        result.TenantId.Should().Be(entity.TenantId);
    }

    [Fact]
    public async Task List_ReturnsPagedResults()
    {
        var entities = new List<AiInteraction> { CreateTestEntity(), CreateTestEntity() };
        _repoMock.Setup(r => r.ListAsync(0, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entities);

        var result = await _sut.ListAsync(0, 10);

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(dto => dto.TenantId.Should().NotBeNullOrEmpty());
    }

    [Fact]
    public async Task Create_SavesEntityViaRepository()
    {
        _repoMock.Setup(r => r.CreateAsync(It.IsAny<AiInteraction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AiInteraction e, CancellationToken _) => e);

        var request = new CreateAiInteractionRequest
        {
            TenantId = "tenant-1",
        };

        var result = await _sut.CreateAsync(request);

        result.Should().NotBeNull();
        result.Id.Should().NotBeNullOrEmpty();
        result.TenantId.Should().Be("tenant-1");
        _repoMock.Verify(r => r.CreateAsync(It.IsAny<AiInteraction>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_PublishesCreatedEvent()
    {
        _repoMock.Setup(r => r.CreateAsync(It.IsAny<AiInteraction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AiInteraction e, CancellationToken _) => e);

        var request = new CreateAiInteractionRequest
        {
            TenantId = "tenant-1",
        };

        await _sut.CreateAsync(request);

        _eventsMock.Verify(e => e.PublishAsync(
            It.Is<AiInteractionCreatedEvent>(evt => evt.TenantId == "tenant-1"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_ThrowsWhenNotFound()
    {
        _repoMock.Setup(r => r.GetByIdAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AiInteraction?)null);

        var act = () => _sut.UpdateAsync(new UpdateAiInteractionRequest { Id = "missing" });

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Update_PublishesUpdatedEvent()
    {
        var entity = CreateTestEntity();
        _repoMock.Setup(r => r.GetByIdAsync(entity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        await _sut.UpdateAsync(new UpdateAiInteractionRequest { Id = entity.Id });

        _repoMock.Verify(r => r.UpdateAsync(It.IsAny<AiInteraction>(), It.IsAny<CancellationToken>()), Times.Once);
        _eventsMock.Verify(e => e.PublishAsync(
            It.Is<AiInteractionUpdatedEvent>(evt => evt.EntityId == entity.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Entity_HasTenantIdProperty()
    {
        typeof(AiInteraction).GetProperty("TenantId").Should().NotBeNull(
            "all entities must have TenantId for multi-tenant isolation [NFR-SEC-01]");
    }

    [Fact]
    public void Entity_HasAuditColumns()
    {
        var type = typeof(AiInteraction);
        type.GetProperty("CreatedAt").Should().NotBeNull("HIPAA audit trail requires CreatedAt [NFR-AUD-01]");
        type.GetProperty("CreatedBy").Should().NotBeNull("HIPAA audit trail requires CreatedBy [NFR-AUD-01]");
    }
}