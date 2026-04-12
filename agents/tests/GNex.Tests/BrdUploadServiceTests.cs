using FluentAssertions;
using GNex.Database;
using GNex.Database.Entities.Platform.Projects;
using GNex.Database.Entities.Platform.Technology;
using GNex.Services.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace GNex.Tests;

public class BrdUploadServiceTests : IDisposable
{
    private readonly GNexDbContext _db;
    private readonly BrdUploadService _svc;
    private readonly string _projectId;
    private const string TenantId = "test-tenant";

    public BrdUploadServiceTests()
    {
        var options = new DbContextOptionsBuilder<GNexDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new GNexDbContext(options, new TestTenantProvider(TenantId));
        _svc = new BrdUploadService(_db, new Mock<ILogger<BrdUploadService>>().Object);

        // Seed a project
        _projectId = Guid.NewGuid().ToString("N");
        _db.Projects.Add(new Project
        {
            Id = _projectId,
            Name = "Test Project",
            Slug = "test-project",
            TenantId = TenantId
        });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── Helpers ──────────────────────────────────────────────

    private void SeedTemplate(string? id = null, bool isDefault = true, int sectionCount = 3)
    {
        var sections = Enumerable.Range(1, sectionCount)
            .Select(i => new { Type = $"section_{i}", Title = $"Section {i}", Order = i, Prompt = $"Describe section {i}" })
            .ToList();

        _db.BrdTemplates.Add(new BrdTemplate
        {
            Id = id ?? Guid.NewGuid().ToString("N"),
            Name = "Standard BRD",
            ProjectType = "web_app",
            SectionsJson = System.Text.Json.JsonSerializer.Serialize(sections),
            IsDefault = isDefault,
            TenantId = TenantId
        });
        _db.SaveChanges();
    }

    // ── Single File Upload ──────────────────────────────────

    [Fact]
    public async Task SingleUpload_NoTemplate_ReturnsRawOnly()
    {
        var result = await _svc.UploadAndGenerateDraftAsync(_projectId, "req.txt", "some content");

        result.Status.Should().Be("raw_only");
        result.SectionsCreated.Should().Be(0);
        result.ProjectId.Should().Be(_projectId);
        result.RawRequirementId.Should().NotBeNullOrEmpty();

        var raw = await _db.RawRequirements.SingleAsync();
        raw.InputText.Should().Be("some content");
        raw.InputType.Should().Be("file");
        raw.SubmittedBy.Should().Be("upload:req.txt");
    }

    [Fact]
    public async Task SingleUpload_WithDefaultTemplate_CreatesSections()
    {
        SeedTemplate(sectionCount: 4);

        var result = await _svc.UploadAndGenerateDraftAsync(_projectId, "req.txt", "my requirements");

        result.Status.Should().Be("draft");
        result.SectionsCreated.Should().Be(4);

        var sections = await _db.BrdSectionRecords.Where(s => s.BrdId == _projectId).ToListAsync();
        sections.Should().HaveCount(4);
        sections.Should().AllSatisfy(s =>
        {
            s.Content.Should().Contain("[DRAFT]");
            s.Content.Should().Contain("req.txt");
            s.DiagramsJson.Should().Be("[]");
        });
        sections.Select(s => s.Order).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task SingleUpload_WithSpecificTemplate_UsesIt()
    {
        var templateId = Guid.NewGuid().ToString("N");
        SeedTemplate(id: templateId, isDefault: false, sectionCount: 2);

        var result = await _svc.UploadAndGenerateDraftAsync(
            _projectId, "spec.md", "spec content", templateId: templateId);

        result.Status.Should().Be("draft");
        result.SectionsCreated.Should().Be(2);
    }

    [Fact]
    public async Task SingleUpload_SecondFile_DoesNotDuplicateSections()
    {
        SeedTemplate(sectionCount: 3);

        var first = await _svc.UploadAndGenerateDraftAsync(_projectId, "file1.txt", "content 1");
        first.Status.Should().Be("draft");
        first.SectionsCreated.Should().Be(3);

        var second = await _svc.UploadAndGenerateDraftAsync(_projectId, "file2.txt", "content 2");
        second.Status.Should().Be("appended");
        second.SectionsCreated.Should().Be(0);

        var sections = await _db.BrdSectionRecords.Where(s => s.BrdId == _projectId).ToListAsync();
        sections.Should().HaveCount(3, "sections should NOT be duplicated on second upload");

        var rawCount = await _db.RawRequirements.CountAsync();
        rawCount.Should().Be(2, "both raw requirements should be stored");
    }

    [Fact]
    public async Task SingleUpload_StoresRawRequirementWithCorrectMetadata()
    {
        SeedTemplate();

        var before = DateTimeOffset.UtcNow;
        var result = await _svc.UploadAndGenerateDraftAsync(_projectId, "data.csv", "col1,col2\n1,2");

        var raw = await _db.RawRequirements.FindAsync(result.RawRequirementId);
        raw.Should().NotBeNull();
        raw!.ProjectId.Should().Be(_projectId);
        raw.InputType.Should().Be("file");
        raw.SubmittedBy.Should().Be("upload:data.csv");
        raw.SubmittedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public async Task SingleUpload_InvalidTemplateId_FallsBackToRawOnly()
    {
        SeedTemplate(); // seeds a default template

        var result = await _svc.UploadAndGenerateDraftAsync(
            _projectId, "req.txt", "content", templateId: "nonexistent-id");

        result.Status.Should().Be("raw_only");
        result.SectionsCreated.Should().Be(0);

        var rawCount = await _db.RawRequirements.CountAsync();
        rawCount.Should().Be(1, "raw requirement should still be stored");
    }

    // ── Batch Upload ────────────────────────────────────────

    [Fact]
    public async Task BatchUpload_NoTemplate_StoresAllRawRequirements()
    {
        var files = new List<(string, string)>
        {
            ("file1.txt", "content 1"),
            ("file2.txt", "content 2"),
            ("file3.txt", "content 3")
        };

        var result = await _svc.UploadBatchAndGenerateDraftAsync(_projectId, files);

        result.Status.Should().Be("raw_only");
        result.FilesProcessed.Should().Be(3);
        result.TotalSectionsCreated.Should().Be(0);
        result.FileResults.Should().HaveCount(3);

        var rawCount = await _db.RawRequirements.CountAsync();
        rawCount.Should().Be(3);
    }

    [Fact]
    public async Task BatchUpload_WithTemplate_CreatesExactlyOneSectionSet()
    {
        SeedTemplate(sectionCount: 5);

        var files = new List<(string, string)>
        {
            ("req1.md", "requirements part 1"),
            ("req2.md", "requirements part 2"),
            ("req3.md", "requirements part 3")
        };

        var result = await _svc.UploadBatchAndGenerateDraftAsync(_projectId, files);

        result.Status.Should().Be("draft");
        result.FilesProcessed.Should().Be(3);
        result.TotalSectionsCreated.Should().Be(5, "exactly one set of template sections");

        var sections = await _db.BrdSectionRecords.Where(s => s.BrdId == _projectId).ToListAsync();
        sections.Should().HaveCount(5, "must NOT multiply by file count");
        sections.Select(s => s.SectionType).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task BatchUpload_SectionContentReferencesAllFiles()
    {
        SeedTemplate(sectionCount: 2);

        var files = new List<(string, string)>
        {
            ("alpha.txt", "alpha content"),
            ("beta.md", "beta content")
        };

        var result = await _svc.UploadBatchAndGenerateDraftAsync(_projectId, files);

        var sections = await _db.BrdSectionRecords.Where(s => s.BrdId == _projectId).ToListAsync();
        sections.Should().AllSatisfy(s =>
        {
            s.Content.Should().Contain("alpha.txt");
            s.Content.Should().Contain("beta.md");
            s.Content.Should().Contain("2 file(s)");
        });
    }

    [Fact]
    public async Task BatchUpload_ReplacesExistingSections()
    {
        SeedTemplate(sectionCount: 3);

        // First batch
        var files1 = new List<(string, string)> { ("v1.txt", "version 1") };
        await _svc.UploadBatchAndGenerateDraftAsync(_projectId, files1);

        var sectionsBefore = await _db.BrdSectionRecords.Where(s => s.BrdId == _projectId).ToListAsync();
        sectionsBefore.Should().HaveCount(3);
        var oldIds = sectionsBefore.Select(s => s.Id).ToHashSet();

        // Second batch — should replace, not append
        var files2 = new List<(string, string)>
        {
            ("v2a.txt", "version 2a"),
            ("v2b.txt", "version 2b")
        };
        await _svc.UploadBatchAndGenerateDraftAsync(_projectId, files2);

        var sectionsAfter = await _db.BrdSectionRecords.Where(s => s.BrdId == _projectId).ToListAsync();
        sectionsAfter.Should().HaveCount(3, "same template section count, not 6");
        sectionsAfter.Select(s => s.Id).Should().NotIntersectWith(oldIds, "old sections should be removed");
        sectionsAfter.Should().AllSatisfy(s =>
        {
            s.Content.Should().Contain("v2a.txt");
            s.Content.Should().Contain("v2b.txt");
            s.Content.Should().NotContain("v1.txt");
        });
    }

    [Fact]
    public async Task BatchUpload_StoresAllRawRequirements()
    {
        SeedTemplate(sectionCount: 2);

        var files = new List<(string, string)>
        {
            ("a.txt", "aaa"),
            ("b.txt", "bbb"),
            ("c.txt", "ccc"),
            ("d.txt", "ddd")
        };

        await _svc.UploadBatchAndGenerateDraftAsync(_projectId, files);

        var raws = await _db.RawRequirements.ToListAsync();
        raws.Should().HaveCount(4);
        raws.Select(r => r.SubmittedBy).Should().Contain("upload:a.txt");
        raws.Select(r => r.SubmittedBy).Should().Contain("upload:d.txt");
    }

    [Fact]
    public async Task BatchUpload_FileResultsContainCorrectMetadata()
    {
        SeedTemplate(sectionCount: 2);

        var files = new List<(string, string)>
        {
            ("spec.md", "spec content"),
            ("req.txt", "req content")
        };

        var result = await _svc.UploadBatchAndGenerateDraftAsync(_projectId, files);

        result.FileResults.Should().HaveCount(2);
        result.FileResults.Should().AllSatisfy(fr =>
        {
            fr.RawRequirementId.Should().NotBeNullOrEmpty();
            fr.Status.Should().Be("draft");
        });
        result.FileResults.Select(fr => fr.FileName).Should()
            .BeEquivalentTo(["spec.md", "req.txt"]);
    }

    [Fact]
    public async Task BatchUpload_EmptyList_ReturnsZeroCounts()
    {
        SeedTemplate(sectionCount: 3);

        var result = await _svc.UploadBatchAndGenerateDraftAsync(
            _projectId, new List<(string, string)>());

        result.FilesProcessed.Should().Be(0);
        result.TotalSectionsCreated.Should().Be(0);
        result.FileResults.Should().BeEmpty();
    }

    [Fact]
    public async Task BatchUpload_WithSpecificTemplate_UsesIt()
    {
        var templateId = Guid.NewGuid().ToString("N");
        SeedTemplate(id: templateId, isDefault: false, sectionCount: 7);

        var files = new List<(string, string)> { ("x.txt", "x content") };

        var result = await _svc.UploadBatchAndGenerateDraftAsync(
            _projectId, files, templateId: templateId);

        result.TotalSectionsCreated.Should().Be(7);
    }

    // ── GetBrdSectionsAsync ─────────────────────────────────

    [Fact]
    public async Task GetSections_ReturnsOrderedActiveSections()
    {
        SeedTemplate(sectionCount: 4);
        await _svc.UploadAndGenerateDraftAsync(_projectId, "f.txt", "content");

        var sections = await _svc.GetBrdSectionsAsync(_projectId);

        sections.Should().HaveCount(4);
        sections.Select(s => s.Order).Should().BeInAscendingOrder();
        sections.Should().AllSatisfy(s =>
        {
            s.Id.Should().NotBeNullOrEmpty();
            s.SectionType.Should().StartWith("section_");
            s.Content.Should().Contain("[DRAFT]");
        });
    }

    [Fact]
    public async Task GetSections_ExcludesInactiveSections()
    {
        SeedTemplate(sectionCount: 2);
        await _svc.UploadAndGenerateDraftAsync(_projectId, "f.txt", "content");

        // Soft-delete one section
        var first = await _db.BrdSectionRecords.FirstAsync();
        first.IsActive = false;
        await _db.SaveChangesAsync();

        var sections = await _svc.GetBrdSectionsAsync(_projectId);
        sections.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetSections_EmptyProject_ReturnsEmptyList()
    {
        var sections = await _svc.GetBrdSectionsAsync(_projectId);
        sections.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSections_DifferentProject_ReturnsOnlyMatchingSections()
    {
        SeedTemplate(sectionCount: 3);

        var otherProjectId = Guid.NewGuid().ToString("N");
        _db.Projects.Add(new Project
        {
            Id = otherProjectId,
            Name = "Other",
            Slug = "other",
            TenantId = TenantId
        });
        await _db.SaveChangesAsync();

        await _svc.UploadAndGenerateDraftAsync(_projectId, "f1.txt", "content1");
        await _svc.UploadAndGenerateDraftAsync(otherProjectId, "f2.txt", "content2");

        var sections1 = await _svc.GetBrdSectionsAsync(_projectId);
        var sections2 = await _svc.GetBrdSectionsAsync(otherProjectId);

        sections1.Should().HaveCount(3);
        sections2.Should().HaveCount(3);
        sections1.Select(s => s.Id).Should().NotIntersectWith(sections2.Select(s => s.Id));
    }

    // ── Tenant Provider ─────────────────────────────────────

    private sealed class TestTenantProvider(string tenantId) : ITenantProvider
    {
        public string TenantId { get; } = tenantId;
    }
}
