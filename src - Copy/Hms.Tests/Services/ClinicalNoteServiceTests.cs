using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;
using Hms.EncounterService.Contracts;
using Hms.EncounterService.Data.Entities;
using Hms.EncounterService.Data.Repositories;
using Hms.EncounterService.Kafka;
using Hms.EncounterService.Services;
using Xunit;

namespace Hms.Tests.Services;

/// <summary>
/// Unit tests for ClinicalNoteService.
/// Feature coverage: EP-02, Module-D, ClinicalDocs
/// </summary>
public class ClinicalNoteServiceTests
{
    private readonly Mock<IClinicalNoteRepository> _repoMock = new();
    private readonly Mock<EncounterServiceEventProducer> _eventsMock;
    private readonly ClinicalNoteService _sut;

    public ClinicalNoteServiceTests()
    {
        _eventsMock = new Mock<EncounterServiceEventProducer>(
            MockBehavior.Loose, null!, null!);
        _sut = new ClinicalNoteService(
            _repoMock.Object,
            _eventsMock.Object,
            Mock.Of<ILogger<ClinicalNoteService>>());
    }

    private static ClinicalNote CreateTestEntity() => new()
    {

    };

    [Fact]
    public async Task GetById_ReturnsNull_WhenNotFound()
    {
        _repoMock.Setup(r => r.GetByIdAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClinicalNote?)null);

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
        var entities = new List<ClinicalNote> { CreateTestEntity(), CreateTestEntity() };
        _repoMock.Setup(r => r.ListAsync(0, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entities);

        var result = await _sut.ListAsync(0, 10);

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(dto => dto.TenantId.Should().NotBeNullOrEmpty());
    }

    [Fact]
    public async Task Create_SavesEntityViaRepository()
    {
        _repoMock.Setup(r => r.CreateAsync(It.IsAny<ClinicalNote>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClinicalNote e, CancellationToken _) => e);

        var request = new CreateClinicalNoteRequest
        {
            TenantId = "tenant-1",
        };

        var result = await _sut.CreateAsync(request);

        result.Should().NotBeNull();
        result.Id.Should().NotBeNullOrEmpty();
        result.TenantId.Should().Be("tenant-1");
        _repoMock.Verify(r => r.CreateAsync(It.IsAny<ClinicalNote>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_PublishesCreatedEvent()
    {
        _repoMock.Setup(r => r.CreateAsync(It.IsAny<ClinicalNote>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClinicalNote e, CancellationToken _) => e);

        var request = new CreateClinicalNoteRequest
        {
            TenantId = "tenant-1",
        };

        await _sut.CreateAsync(request);

        _eventsMock.Verify(e => e.PublishAsync(
            It.Is<ClinicalNoteCreatedEvent>(evt => evt.TenantId == "tenant-1"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_ThrowsWhenNotFound()
    {
        _repoMock.Setup(r => r.GetByIdAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClinicalNote?)null);

        var act = () => _sut.UpdateAsync(new UpdateClinicalNoteRequest { Id = "missing" });

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Update_PublishesUpdatedEvent()
    {
        var entity = CreateTestEntity();
        _repoMock.Setup(r => r.GetByIdAsync(entity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        await _sut.UpdateAsync(new UpdateClinicalNoteRequest { Id = entity.Id });

        _repoMock.Verify(r => r.UpdateAsync(It.IsAny<ClinicalNote>(), It.IsAny<CancellationToken>()), Times.Once);
        _eventsMock.Verify(e => e.PublishAsync(
            It.Is<ClinicalNoteUpdatedEvent>(evt => evt.EntityId == entity.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Entity_HasTenantIdProperty()
    {
        typeof(ClinicalNote).GetProperty("TenantId").Should().NotBeNull(
            "all entities must have TenantId for multi-tenant isolation [NFR-SEC-01]");
    }

    [Fact]
    public void Entity_HasAuditColumns()
    {
        var type = typeof(ClinicalNote);
        type.GetProperty("CreatedAt").Should().NotBeNull("HIPAA audit trail requires CreatedAt [NFR-AUD-01]");
        type.GetProperty("CreatedBy").Should().NotBeNull("HIPAA audit trail requires CreatedBy [NFR-AUD-01]");
    }
}