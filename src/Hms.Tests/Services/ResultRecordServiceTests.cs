using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;
using Hms.DiagnosticsService.Contracts;
using Hms.DiagnosticsService.Data.Entities;
using Hms.DiagnosticsService.Data.Repositories;
using Hms.DiagnosticsService.Kafka;
using Hms.DiagnosticsService.Services;
using Xunit;

namespace Hms.Tests.Services;

/// <summary>
/// Unit tests for ResultRecordService.
/// Feature coverage: EP-10, Module-J, Diagnostics
/// </summary>
public class ResultRecordServiceTests
{
    private readonly Mock<IResultRecordRepository> _repoMock = new();
    private readonly Mock<DiagnosticsServiceEventProducer> _eventsMock;
    private readonly ResultRecordService _sut;

    public ResultRecordServiceTests()
    {
        _eventsMock = new Mock<DiagnosticsServiceEventProducer>(
            MockBehavior.Loose, null!, null!);
        _sut = new ResultRecordService(
            _repoMock.Object,
            _eventsMock.Object,
            Mock.Of<ILogger<ResultRecordService>>());
    }

    private static ResultRecord CreateTestEntity() => new()
    {

    };

    [Fact]
    public async Task GetById_ReturnsNull_WhenNotFound()
    {
        _repoMock.Setup(r => r.GetByIdAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResultRecord?)null);

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
        var entities = new List<ResultRecord> { CreateTestEntity(), CreateTestEntity() };
        _repoMock.Setup(r => r.ListAsync(0, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entities);

        var result = await _sut.ListAsync(0, 10);

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(dto => dto.TenantId.Should().NotBeNullOrEmpty());
    }

    [Fact]
    public async Task Create_SavesEntityViaRepository()
    {
        _repoMock.Setup(r => r.CreateAsync(It.IsAny<ResultRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResultRecord e, CancellationToken _) => e);

        var request = new CreateResultRecordRequest
        {
            TenantId = "tenant-1",
        };

        var result = await _sut.CreateAsync(request);

        result.Should().NotBeNull();
        result.Id.Should().NotBeNullOrEmpty();
        result.TenantId.Should().Be("tenant-1");
        _repoMock.Verify(r => r.CreateAsync(It.IsAny<ResultRecord>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_PublishesCreatedEvent()
    {
        _repoMock.Setup(r => r.CreateAsync(It.IsAny<ResultRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResultRecord e, CancellationToken _) => e);

        var request = new CreateResultRecordRequest
        {
            TenantId = "tenant-1",
        };

        await _sut.CreateAsync(request);

        _eventsMock.Verify(e => e.PublishAsync(
            It.Is<ResultRecordCreatedEvent>(evt => evt.TenantId == "tenant-1"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_ThrowsWhenNotFound()
    {
        _repoMock.Setup(r => r.GetByIdAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResultRecord?)null);

        var act = () => _sut.UpdateAsync(new UpdateResultRecordRequest { Id = "missing" });

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Update_PublishesUpdatedEvent()
    {
        var entity = CreateTestEntity();
        _repoMock.Setup(r => r.GetByIdAsync(entity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        await _sut.UpdateAsync(new UpdateResultRecordRequest { Id = entity.Id });

        _repoMock.Verify(r => r.UpdateAsync(It.IsAny<ResultRecord>(), It.IsAny<CancellationToken>()), Times.Once);
        _eventsMock.Verify(e => e.PublishAsync(
            It.Is<ResultRecordUpdatedEvent>(evt => evt.EntityId == entity.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Entity_HasTenantIdProperty()
    {
        typeof(ResultRecord).GetProperty("TenantId").Should().NotBeNull(
            "all entities must have TenantId for multi-tenant isolation [NFR-SEC-01]");
    }

    [Fact]
    public void Entity_HasAuditColumns()
    {
        var type = typeof(ResultRecord);
        type.GetProperty("CreatedAt").Should().NotBeNull("HIPAA audit trail requires CreatedAt [NFR-AUD-01]");
        type.GetProperty("CreatedBy").Should().NotBeNull("HIPAA audit trail requires CreatedBy [NFR-AUD-01]");
    }
}