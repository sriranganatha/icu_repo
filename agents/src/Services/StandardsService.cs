using GNex.Database;
using GNex.Database.Entities.Platform.Standards;
using GNex.Database.Repositories;
using GNex.Services.Dtos.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GNex.Services.Platform;

public sealed class StandardsService : IStandardsService
{
    private readonly IPlatformRepository<CodingStandard> _codingRepo;
    private readonly IPlatformRepository<NamingConvention> _namingRepo;
    private readonly IPlatformRepository<QualityGate> _gateRepo;
    private readonly IPlatformRepository<ReviewChecklist> _checklistRepo;
    private readonly IPlatformRepository<SecurityPolicy> _policyRepo;
    private readonly GNexDbContext _db;
    private readonly ILogger<StandardsService> _logger;

    public StandardsService(
        IPlatformRepository<CodingStandard> codingRepo,
        IPlatformRepository<NamingConvention> namingRepo,
        IPlatformRepository<QualityGate> gateRepo,
        IPlatformRepository<ReviewChecklist> checklistRepo,
        IPlatformRepository<SecurityPolicy> policyRepo,
        GNexDbContext db,
        ILogger<StandardsService> logger)
    {
        _codingRepo = codingRepo;
        _namingRepo = namingRepo;
        _gateRepo = gateRepo;
        _checklistRepo = checklistRepo;
        _policyRepo = policyRepo;
        _db = db;
        _logger = logger;
    }

    // ─── Coding Standards ───────────────────────────────────
    public async Task<CodingStandardDto?> GetCodingStandardAsync(string id, CancellationToken ct = default)
    {
        var e = await _codingRepo.GetByIdAsync(id, ct);
        return e is null ? null : MapCodingStandard(e);
    }

    public async Task<List<CodingStandardDto>> ListCodingStandardsAsync(string? languageId = null, CancellationToken ct = default)
    {
        if (languageId is not null)
        {
            var items = await _codingRepo.QueryAsync(c => c.LanguageId == languageId, ct: ct);
            return items.Select(MapCodingStandard).ToList();
        }
        var all = await _codingRepo.ListAsync(ct: ct);
        return all.Select(MapCodingStandard).ToList();
    }

    public async Task<CodingStandardDto> CreateCodingStandardAsync(CreateCodingStandardRequest request, CancellationToken ct = default)
    {
        var entity = new CodingStandard
        {
            Name = request.Name,
            LanguageId = request.LanguageId,
            RulesJson = request.RulesJson,
            LinterConfig = request.LinterConfig
        };
        await _codingRepo.CreateAsync(entity, ct);
        _logger.LogInformation("Created coding standard {Id} '{Name}'", entity.Id, entity.Name);
        return MapCodingStandard(entity);
    }

    public async Task<CodingStandardDto> UpdateCodingStandardAsync(UpdateCodingStandardRequest request, CancellationToken ct = default)
    {
        var entity = await _codingRepo.GetByIdAsync(request.Id, ct)
            ?? throw new KeyNotFoundException($"Coding standard {request.Id} not found");
        entity.Name = request.Name;
        entity.LanguageId = request.LanguageId;
        entity.RulesJson = request.RulesJson;
        entity.LinterConfig = request.LinterConfig;
        await _codingRepo.UpdateAsync(entity, ct);
        return MapCodingStandard(entity);
    }

    public async Task DeleteCodingStandardAsync(string id, CancellationToken ct = default)
        => await _codingRepo.SoftDeleteAsync(id, ct);

    // ─── Naming Conventions ─────────────────────────────────
    public async Task<List<NamingConventionDto>> ListNamingConventionsAsync(CancellationToken ct = default)
    {
        var items = await _namingRepo.ListAsync(ct: ct);
        return items.Select(n => new NamingConventionDto(
            n.Id, n.Scope, n.Pattern, n.ExamplesJson,
            n.IsActive, n.CreatedAt, n.UpdatedAt)).ToList();
    }

    public async Task<NamingConventionDto> CreateNamingConventionAsync(CreateNamingConventionRequest request, CancellationToken ct = default)
    {
        var entity = new NamingConvention
        {
            Scope = request.Scope,
            Pattern = request.Pattern,
            ExamplesJson = request.ExamplesJson
        };
        await _namingRepo.CreateAsync(entity, ct);
        return new NamingConventionDto(entity.Id, entity.Scope, entity.Pattern,
            entity.ExamplesJson, entity.IsActive, entity.CreatedAt, entity.UpdatedAt);
    }

    public async Task DeleteNamingConventionAsync(string id, CancellationToken ct = default)
        => await _namingRepo.SoftDeleteAsync(id, ct);

    // ─── Quality Gates ──────────────────────────────────────
    public async Task<QualityGateDto?> GetQualityGateAsync(string id, CancellationToken ct = default)
    {
        var e = await _gateRepo.GetByIdAsync(id, ct);
        return e is null ? null : MapQualityGate(e);
    }

    public async Task<List<QualityGateDto>> ListQualityGatesAsync(CancellationToken ct = default)
    {
        var items = await _gateRepo.ListAsync(ct: ct);
        return items.Select(MapQualityGate).ToList();
    }

    public async Task<QualityGateDto> CreateQualityGateAsync(CreateQualityGateRequest request, CancellationToken ct = default)
    {
        var entity = new QualityGate
        {
            Name = request.Name,
            GateType = request.GateType,
            ThresholdConfigJson = request.ThresholdConfigJson
        };
        await _gateRepo.CreateAsync(entity, ct);
        _logger.LogInformation("Created quality gate {Id} '{Name}'", entity.Id, entity.Name);
        return MapQualityGate(entity);
    }

    public async Task<QualityGateDto> UpdateQualityGateAsync(UpdateQualityGateRequest request, CancellationToken ct = default)
    {
        var entity = await _gateRepo.GetByIdAsync(request.Id, ct)
            ?? throw new KeyNotFoundException($"Quality gate {request.Id} not found");
        entity.Name = request.Name;
        entity.GateType = request.GateType;
        entity.ThresholdConfigJson = request.ThresholdConfigJson;
        await _gateRepo.UpdateAsync(entity, ct);
        return MapQualityGate(entity);
    }

    public async Task DeleteQualityGateAsync(string id, CancellationToken ct = default)
        => await _gateRepo.SoftDeleteAsync(id, ct);

    // ─── Review Checklists ──────────────────────────────────
    public async Task<List<ReviewChecklistDto>> ListReviewChecklistsAsync(string? scope = null, CancellationToken ct = default)
    {
        List<ReviewChecklist> items;
        if (scope is not null)
            items = await _checklistRepo.QueryAsync(c => c.Scope == scope, ct: ct);
        else
            items = await _checklistRepo.ListAsync(ct: ct);
        return items.Select(c => new ReviewChecklistDto(
            c.Id, c.Name, c.Scope, c.ChecklistItemsJson,
            c.IsActive, c.CreatedAt, c.UpdatedAt)).ToList();
    }

    public async Task<ReviewChecklistDto> CreateReviewChecklistAsync(CreateReviewChecklistRequest request, CancellationToken ct = default)
    {
        var entity = new ReviewChecklist
        {
            Name = request.Name,
            Scope = request.Scope,
            ChecklistItemsJson = request.ChecklistItemsJson
        };
        await _checklistRepo.CreateAsync(entity, ct);
        return new ReviewChecklistDto(entity.Id, entity.Name, entity.Scope,
            entity.ChecklistItemsJson, entity.IsActive, entity.CreatedAt, entity.UpdatedAt);
    }

    public async Task DeleteReviewChecklistAsync(string id, CancellationToken ct = default)
        => await _checklistRepo.SoftDeleteAsync(id, ct);

    // ─── Security Policies ──────────────────────────────────
    public async Task<SecurityPolicyDto?> GetSecurityPolicyAsync(string id, CancellationToken ct = default)
    {
        var e = await _policyRepo.GetByIdAsync(id, ct);
        return e is null ? null : MapSecurityPolicy(e);
    }

    public async Task<List<SecurityPolicyDto>> ListSecurityPoliciesAsync(string? category = null, CancellationToken ct = default)
    {
        List<SecurityPolicy> items;
        if (category is not null)
            items = await _policyRepo.QueryAsync(p => p.Category == category, ct: ct);
        else
            items = await _policyRepo.ListAsync(ct: ct);
        return items.Select(MapSecurityPolicy).ToList();
    }

    public async Task<SecurityPolicyDto> CreateSecurityPolicyAsync(CreateSecurityPolicyRequest request, CancellationToken ct = default)
    {
        var entity = new SecurityPolicy
        {
            Name = request.Name,
            Category = request.Category,
            RulesJson = request.RulesJson,
            Severity = request.Severity
        };
        await _policyRepo.CreateAsync(entity, ct);
        _logger.LogInformation("Created security policy {Id} '{Name}'", entity.Id, entity.Name);
        return MapSecurityPolicy(entity);
    }

    public async Task<SecurityPolicyDto> UpdateSecurityPolicyAsync(UpdateSecurityPolicyRequest request, CancellationToken ct = default)
    {
        var entity = await _policyRepo.GetByIdAsync(request.Id, ct)
            ?? throw new KeyNotFoundException($"Security policy {request.Id} not found");
        entity.Name = request.Name;
        entity.Category = request.Category;
        entity.RulesJson = request.RulesJson;
        entity.Severity = request.Severity;
        await _policyRepo.UpdateAsync(entity, ct);
        return MapSecurityPolicy(entity);
    }

    public async Task DeleteSecurityPolicyAsync(string id, CancellationToken ct = default)
        => await _policyRepo.SoftDeleteAsync(id, ct);

    // ─── Mapping ────────────────────────────────────────────
    private static CodingStandardDto MapCodingStandard(CodingStandard c) => new(
        c.Id, c.Name, c.LanguageId, c.RulesJson, c.LinterConfig,
        c.IsActive, c.CreatedAt, c.UpdatedAt);

    private static QualityGateDto MapQualityGate(QualityGate g) => new(
        g.Id, g.Name, g.GateType, g.ThresholdConfigJson,
        g.IsActive, g.CreatedAt, g.UpdatedAt);

    private static SecurityPolicyDto MapSecurityPolicy(SecurityPolicy p) => new(
        p.Id, p.Name, p.Category, p.RulesJson, p.Severity,
        p.IsActive, p.CreatedAt, p.UpdatedAt);
}
