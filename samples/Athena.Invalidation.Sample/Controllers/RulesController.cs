using Athena.Invalidation.Core.Abstractions;
using Athena.Invalidation.Sample.Models;
using Athena.Invalidation.Sample.Services;
using Microsoft.AspNetCore.Mvc;

namespace Athena.Invalidation.Sample.Controllers;

/// <summary>
/// 동적 무효화 규칙 관리 기능을 시연하는 컨트롤러
/// 규칙 기반의 자동 무효화와 조건부 무효화를 제공합니다.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class RulesController : ControllerBase
{
    private readonly IInvalidationEngine _invalidationEngine;
    private readonly ILogger<RulesController> _logger;

    // 간단한 메모리 저장소 (실제로는 데이터베이스나 Redis 사용)
    private static readonly List<InvalidationRule> _rules = new();

    public RulesController(
        IInvalidationEngine invalidationEngine,
        ILogger<RulesController> logger)
    {
        _invalidationEngine = invalidationEngine;
        _logger = logger;

        // 기본 규칙들 초기화
        InitializeDefaultRules();
    }

    /// <summary>
    /// 새로운 무효화 규칙 생성
    /// </summary>
    [HttpPost("create")]
    public IActionResult CreateRule([FromBody] CreateInvalidationRuleRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new { error = "규칙 이름은 필수입니다" });
            }

            if (_rules.Any(r => r.Name.Equals(request.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return Conflict(new { error = $"규칙 '{request.Name}'이 이미 존재합니다" });
            }

            var rule = new InvalidationRule
            {
                Id = _rules.Count + 1,
                Name = request.Name,
                Description = request.Description,
                TriggerTable = request.TriggerTable,
                TriggerEvent = request.TriggerEvent,
                Condition = request.Condition,
                InvalidationTargets = request.InvalidationTargets ?? new List<InvalidationTarget>(),
                IsActive = request.IsActive,
                Priority = request.Priority,
                CreatedAt = DateTime.UtcNow
            };

            _rules.Add(rule);

            _logger.LogInformation("무효화 규칙 생성: {RuleName}", request.Name);

            return Ok(new
            {
                message = "무효화 규칙이 성공적으로 생성되었습니다",
                rule = new
                {
                    id = rule.Id,
                    name = rule.Name,
                    description = rule.Description,
                    triggerTable = rule.TriggerTable,
                    triggerEvent = rule.TriggerEvent,
                    isActive = rule.IsActive,
                    priority = rule.Priority,
                    targetCount = rule.InvalidationTargets.Count
                },
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "무효화 규칙 생성 실패: {RuleName}", request.Name);
            return StatusCode(500, new { error = "무효화 규칙 생성에 실패했습니다", details = ex.Message });
        }
    }

    /// <summary>
    /// 모든 무효화 규칙 조회
    /// </summary>
    [HttpGet("list")]
    public IActionResult GetAllRules()
    {
        try
        {
            var rules = _rules.OrderBy(r => r.Priority).ThenBy(r => r.Name).ToList();

            return Ok(new
            {
                totalRules = rules.Count,
                activeRules = rules.Count(r => r.IsActive),
                inactiveRules = rules.Count(r => !r.IsActive),
                rules = rules.Select(r => new
                {
                    id = r.Id,
                    name = r.Name,
                    description = r.Description,
                    triggerTable = r.TriggerTable,
                    triggerEvent = r.TriggerEvent,
                    isActive = r.IsActive,
                    priority = r.Priority,
                    targetCount = r.InvalidationTargets.Count,
                    createdAt = r.CreatedAt,
                    lastExecuted = r.LastExecutedAt
                }),
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "무효화 규칙 목록 조회 실패");
            return StatusCode(500, new { error = "무효화 규칙 목록 조회에 실패했습니다", details = ex.Message });
        }
    }

    /// <summary>
    /// 규칙 기반 자동 무효화 실행
    /// </summary>
    [HttpPost("execute-by-event")]
    public async Task<IActionResult> ExecuteRulesByEvent([FromBody] ExecuteRulesByEventRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.TableName))
            {
                return BadRequest(new { error = "테이블 이름은 필수입니다" });
            }

            var applicableRules = _rules
                .Where(r => r.IsActive && 
                           r.TriggerTable.Equals(request.TableName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.Priority)
                .ToList();

            if (!applicableRules.Any())
            {
                return Ok(new
                {
                    message = "적용 가능한 규칙이 없습니다",
                    tableName = request.TableName,
                    eventType = request.EventType
                });
            }

            var results = new List<object>();
            var totalInvalidations = 0;

            _logger.LogInformation("이벤트 기반 규칙 실행: {TableName}.{EventType}, 적용 규칙: {RuleCount}개", 
                request.TableName, request.EventType, applicableRules.Count);

            foreach (var rule in applicableRules)
            {
                var ruleResults = new List<object>();

                // 조건 평가 (간단한 버전)
                var conditionMet = string.IsNullOrEmpty(rule.Condition) || rule.Condition == "always";

                if (conditionMet)
                {
                    // 무효화 대상 실행
                    foreach (var target in rule.InvalidationTargets.OrderBy(t => t.Priority))
                    {
                        switch (target.Type.ToLowerInvariant())
                        {
                            case "key":
                                await _invalidationEngine.InvalidateByKeyAsync(target.Target);
                                break;
                            case "pattern":
                                await _invalidationEngine.InvalidateByPatternAsync(target.Target);
                                break;
                            case "table":
                                await _invalidationEngine.InvalidateByTableAsync(target.Target);
                                break;
                        }

                        ruleResults.Add(new
                        {
                            type = target.Type,
                            target = target.Target,
                            executed = true
                        });
                        totalInvalidations++;
                    }

                    // 통계 업데이트
                    rule.ExecutionCount++;
                    rule.LastExecutedAt = DateTime.UtcNow;
                }

                results.Add(new
                {
                    ruleId = rule.Id,
                    ruleName = rule.Name,
                    conditionMet = conditionMet,
                    invalidationsExecuted = ruleResults.Count,
                    details = ruleResults
                });
            }

            return Ok(new
            {
                scenario = "이벤트 기반 규칙 실행",
                eventInfo = new
                {
                    tableName = request.TableName,
                    eventType = request.EventType
                },
                execution = new
                {
                    applicableRules = applicableRules.Count,
                    executedRules = results.Count,
                    totalInvalidations = totalInvalidations
                },
                ruleExecutions = results,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "이벤트 기반 규칙 실행 실패: {TableName}.{EventType}", request.TableName, request.EventType);
            return StatusCode(500, new { error = "이벤트 기반 규칙 실행에 실패했습니다", details = ex.Message });
        }
    }

    private void InitializeDefaultRules()
    {
        if (_rules.Any()) return; // 이미 초기화됨

        // 기본 규칙들
        _rules.AddRange(new[]
        {
            new InvalidationRule
            {
                Id = 1,
                Name = "상품 가격 변경 - 기본",
                Description = "상품 가격이 변경되면 관련 캐시를 무효화합니다",
                TriggerTable = "products",
                TriggerEvent = "PriceChanged",
                Condition = "always",
                InvalidationTargets = new List<InvalidationTarget>
                {
                    new() { Type = "key", Target = "product:{productId}", Priority = 1 },
                    new() { Type = "pattern", Target = "products:category:*", Priority = 2 },
                    new() { Type = "key", Target = "products:featured", Priority = 3 }
                },
                IsActive = true,
                Priority = 1,
                CreatedAt = DateTime.UtcNow
            },
            new InvalidationRule
            {
                Id = 2,
                Name = "카테고리 변경 - 계층적",
                Description = "카테고리가 변경되면 계층 구조 관련 캐시를 무효화합니다",
                TriggerTable = "categories",
                TriggerEvent = "Updated",
                Condition = "always",
                InvalidationTargets = new List<InvalidationTarget>
                {
                    new() { Type = "table", Target = "categories", Priority = 1 },
                    new() { Type = "pattern", Target = "products:category:*", Priority = 2 }
                },
                IsActive = true,
                Priority = 1,
                CreatedAt = DateTime.UtcNow
            }
        });
    }
}

// 요청/응답 모델들
public class CreateInvalidationRuleRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string TriggerTable { get; set; } = string.Empty;
    public string TriggerEvent { get; set; } = string.Empty;
    public string? Condition { get; set; }
    public List<InvalidationTarget>? InvalidationTargets { get; set; }
    public bool IsActive { get; set; } = true;
    public int Priority { get; set; } = 1;
}

public class ExecuteRulesByEventRequest
{
    public string TableName { get; set; } = string.Empty;
    public string? EventType { get; set; }
    public Dictionary<string, object>? EventData { get; set; }
}

// 도메인 모델들
public class InvalidationRule
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string TriggerTable { get; set; } = string.Empty;
    public string TriggerEvent { get; set; } = string.Empty;
    public string? Condition { get; set; }
    public List<InvalidationTarget> InvalidationTargets { get; set; } = new();
    public bool IsActive { get; set; } = true;
    public int Priority { get; set; } = 1;
    public int ExecutionCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastExecutedAt { get; set; }
}

public class InvalidationTarget
{
    public string Type { get; set; } = string.Empty; // "key", "pattern", "table"
    public string Target { get; set; } = string.Empty;
    public int Priority { get; set; } = 1;
}