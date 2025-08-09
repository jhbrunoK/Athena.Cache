using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Text;

namespace Athena.Cache.SourceGenerator;

[Generator]
public class AthenaCacheSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 간단한 진단을 위해 모든 클래스를 체크
        var allClassProvider = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => s is ClassDeclarationSyntax,
                transform: static (ctx, _) => GetControllerInfo(ctx))
            .Where(static m => m is not null);

        // 생성된 레지스트리 출력
        context.RegisterSourceOutput(allClassProvider.Collect(), GenerateCacheRegistry);
    }

    private static bool IsControllerClass(SyntaxNode node)
    {
        // Controller로 끝나는 클래스이거나 ControllerBase를 상속받는 클래스 찾기
        if (node is not ClassDeclarationSyntax classDeclaration)
            return false;

        return classDeclaration.Identifier.ValueText.EndsWith("Controller") ||
               HasBaseType(classDeclaration, "ControllerBase") ||
               HasBaseType(classDeclaration, "Controller");
    }

    private static bool HasBaseType(ClassDeclarationSyntax classDeclaration, string baseTypeName)
    {
        return classDeclaration.BaseList?.Types
            .Any(baseType => baseType.Type.ToString().Contains(baseTypeName)) == true;
    }

    private static ControllerInfo? GetControllerInfo(GeneratorSyntaxContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        // 클래스의 Symbol 정보 가져오기
        var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration);
        if (classSymbol == null)
            return null;

        var controllerName = classSymbol.Name;
        var actions = new List<ActionInfo>();

        // 모든 public 메서드를 액션으로 간주
        foreach (var member in classSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (member.DeclaredAccessibility != Accessibility.Public ||
                member.IsStatic ||
                member.MethodKind != MethodKind.Ordinary)
                continue;

            var actionInfo = ExtractActionInfo(member);
            if (actionInfo != null)
                actions.Add(actionInfo);
        }

        return new ControllerInfo(controllerName, actions);
    }

    private static ActionInfo? ExtractActionInfo(IMethodSymbol methodSymbol)
    {
        var actionName = methodSymbol.Name;
        
        // AthenaCacheAttribute 찾기
        var cacheAttribute = FindAttribute(methodSymbol, "AthenaCacheAttribute");
        var invalidationAttributes = FindAttributes(methodSymbol, "CacheInvalidateOnAttribute");

        // 컨트롤러 레벨에서도 찾기
        if (cacheAttribute == null)
            cacheAttribute = FindAttribute(methodSymbol.ContainingType, "AthenaCacheAttribute");

        var controllerInvalidationAttributes = FindAttributes(methodSymbol.ContainingType, "CacheInvalidateOnAttribute");
        invalidationAttributes = invalidationAttributes.Concat(controllerInvalidationAttributes).ToList();

        // NoCacheAttribute 체크
        var hasNoCache = FindAttribute(methodSymbol, "NoCacheAttribute") != null ||
                        FindAttribute(methodSymbol.ContainingType, "NoCacheAttribute") != null;

        if (hasNoCache || (cacheAttribute == null && invalidationAttributes.Count == 0))
            return null;

        return new ActionInfo(
            actionName,
            ExtractCacheSettings(cacheAttribute),
            invalidationAttributes.Select(ExtractInvalidationSettings).ToList());
    }

    private static AttributeData? FindAttribute(ISymbol symbol, string attributeName)
    {
        return symbol.GetAttributes()
            .FirstOrDefault(attr => attr.AttributeClass?.Name == attributeName);
    }

    private static List<AttributeData> FindAttributes(ISymbol symbol, string attributeName)
    {
        return symbol.GetAttributes()
            .Where(attr => attr.AttributeClass?.Name == attributeName)
            .ToList();
    }

    private static CacheSettings ExtractCacheSettings(AttributeData? attribute)
    {
        if (attribute == null)
            return new CacheSettings();

        var settings = new CacheSettings();

        // Named arguments 처리
        foreach (var namedArg in attribute.NamedArguments)
        {
            switch (namedArg.Key)
            {
                case nameof(CacheSettings.Enabled):
                    settings.Enabled = (bool)namedArg.Value.Value!;
                    break;
                case nameof(CacheSettings.ExpirationMinutes):
                    settings.ExpirationMinutes = (int)namedArg.Value.Value!;
                    break;
                case nameof(CacheSettings.CustomKeyPrefix):
                    settings.CustomKeyPrefix = (string?)namedArg.Value.Value;
                    break;
                case nameof(CacheSettings.MaxRelatedDepth):
                    settings.MaxRelatedDepth = (int)namedArg.Value.Value!;
                    break;
                case nameof(CacheSettings.AdditionalKeyParameters):
                    settings.AdditionalKeyParameters = ExtractStringArray(namedArg.Value);
                    break;
                case nameof(CacheSettings.ExcludeParameters):
                    settings.ExcludeParameters = ExtractStringArray(namedArg.Value);
                    break;
            }
        }

        return settings;
    }

    private static InvalidationSettings ExtractInvalidationSettings(AttributeData attribute)
    {
        var settings = new InvalidationSettings();
        
        // Constructor arguments 처리
        if (attribute.ConstructorArguments.Length > 0)
        {
            // 첫 번째 인수: TableName
            settings.TableName = (string)attribute.ConstructorArguments[0].Value!;
            
            // 두 번째 인수: InvalidationType (enum)
            if (attribute.ConstructorArguments.Length > 1)
            {
                if (attribute.ConstructorArguments[1].Value is int enumValue)
                {
                    settings.InvalidationType = enumValue switch
                    {
                        0 => "All",
                        1 => "Pattern", 
                        2 => "Related",
                        _ => "All"
                    };
                }
            }
            
            // 세 번째 인수: Pattern (string) 또는 RelatedTables (string[])
            if (attribute.ConstructorArguments.Length > 2)
            {
                var thirdArg = attribute.ConstructorArguments[2];
                if (thirdArg.Kind == TypedConstantKind.Array)
                {
                    // RelatedTables 배열
                    settings.RelatedTables = ExtractStringArray(thirdArg);
                }
                else if (thirdArg.Value is string pattern)
                {
                    // Pattern 문자열
                    settings.Pattern = pattern;
                }
            }
        }

        // Named arguments (property setters)
        foreach (var namedArg in attribute.NamedArguments)
        {
            switch (namedArg.Key)
            {
                case nameof(InvalidationSettings.InvalidationType):
                    // Enum 값을 문자열로 변환
                    if (namedArg.Value.Value is int enumValue)
                    {
                        settings.InvalidationType = enumValue switch
                        {
                            0 => "All",
                            1 => "Pattern", 
                            2 => "Related",
                            _ => "All"
                        };
                    }
                    break;
                case nameof(InvalidationSettings.Pattern):
                    settings.Pattern = (string?)namedArg.Value.Value;
                    break;
                case nameof(InvalidationSettings.RelatedTables):
                    settings.RelatedTables = ExtractStringArray(namedArg.Value);
                    break;
                case nameof(InvalidationSettings.MaxDepth):
                    settings.MaxDepth = (int)namedArg.Value.Value!;
                    break;
            }
        }

        return settings;
    }

    private static string[] ExtractStringArray(TypedConstant typedConstant)
    {
        if (typedConstant.Kind == TypedConstantKind.Array)
        {
            return typedConstant.Values
                .Select(v => (string)v.Value!)
                .ToArray();
        }
        return [];
    }

    private static void GenerateCacheRegistry(SourceProductionContext context, 
        ImmutableArray<ControllerInfo?> controllers)
    {
        var validControllers = controllers.Where(c => c != null).Cast<ControllerInfo>().ToList();
        
        // 디버깅: 항상 파일 생성 + 강제 진단 메시지
        try
        {
            var sourceCode = GenerateRegistryCode(validControllers);
            context.AddSource("CacheConfigurationRegistry.g.cs", sourceCode);
            
            // 디버깅을 위한 진단 메시지
            var descriptor = new DiagnosticDescriptor(
                "AthenaCache001", 
                "Source Generator executed", 
                $"AthenaCacheSourceGenerator executed successfully. Found {validControllers.Count} controllers.", 
                "Athena.Cache", 
                DiagnosticSeverity.Info, 
                isEnabledByDefault: true);
            context.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None));
        }
        catch (Exception ex)
        {
            // 오류 진단 메시지
            var errorDescriptor = new DiagnosticDescriptor(
                "AthenaCache002", 
                "Source Generator error", 
                $"AthenaCacheSourceGenerator failed: {ex.Message}", 
                "Athena.Cache", 
                DiagnosticSeverity.Error, 
                isEnabledByDefault: true);
            context.ReportDiagnostic(Diagnostic.Create(errorDescriptor, Location.None));
        }
    }

    private static string GenerateRegistryCode(List<ControllerInfo> controllers)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("// Debug: Found " + controllers.Count + " controllers");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine();
        sb.AppendLine("namespace Athena.Cache.Core.Generated;");
        sb.AppendLine();
        
        // 필요한 클래스들을 직접 정의
        GenerateRequiredClasses(sb);
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Compile-time generated cache configuration registry");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public class CacheConfigurationRegistry");
        sb.AppendLine("{");
        
        // Dictionary 생성
        sb.AppendLine("    private static readonly Dictionary<string, CacheConfiguration> _configurations = new()");
        sb.AppendLine("    {");
        
        if (controllers.Count == 0)
        {
            sb.AppendLine("        // No controllers with cache attributes found");
        }
        else
        {
            foreach (var controller in controllers)
            {
                sb.AppendLine($"        // Controller: {controller.Name}");
                foreach (var action in controller.Actions)
                {
                    var key = $"{controller.Name}.{action.Name}";
                    sb.AppendLine($"        [\"{key}\"] = new CacheConfiguration");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            Controller = \"{controller.Name}\",");
                    sb.AppendLine($"            Action = \"{action.Name}\",");
                    sb.AppendLine($"            Enabled = {action.CacheSettings.Enabled.ToString().ToLower()},");
                    sb.AppendLine($"            ExpirationMinutes = {action.CacheSettings.ExpirationMinutes},");
                    sb.AppendLine($"            MaxRelatedDepth = {action.CacheSettings.MaxRelatedDepth},");
                    
                    if (action.CacheSettings.CustomKeyPrefix != null)
                        sb.AppendLine($"            CustomKeyPrefix = \"{action.CacheSettings.CustomKeyPrefix}\",");
                    
                    GenerateStringArray(sb, "AdditionalKeyParameters", action.CacheSettings.AdditionalKeyParameters);
                    GenerateStringArray(sb, "ExcludeParameters", action.CacheSettings.ExcludeParameters);
                    
                    // Invalidation rules
                    if (action.InvalidationSettings.Count > 0)
                    {
                        sb.AppendLine("            InvalidationRules = new List<TableInvalidationRule>");
                        sb.AppendLine("            {");
                        
                        foreach (var invalidation in action.InvalidationSettings)
                        {
                            sb.AppendLine("                new TableInvalidationRule");
                            sb.AppendLine("                {");
                            sb.AppendLine($"                    TableName = \"{invalidation.TableName}\",");
                            sb.AppendLine($"                    InvalidationType = InvalidationType.{invalidation.InvalidationType},");
                            
                            if (invalidation.Pattern != null)
                                sb.AppendLine($"                    Pattern = \"{invalidation.Pattern}\",");
                            
                            GenerateStringArray(sb, "RelatedTables", invalidation.RelatedTables, "                    ");
                            sb.AppendLine($"                    MaxDepth = {invalidation.MaxDepth}");
                            sb.AppendLine("                },");
                        }
                        
                        sb.AppendLine("            }");
                    }
                    else
                    {
                        sb.AppendLine("            InvalidationRules = new List<TableInvalidationRule>()");
                    }
                    
                    sb.AppendLine("        },");
                }
            }
        }
        
        sb.AppendLine("    };");
        sb.AppendLine();
        
        // Get method
        sb.AppendLine("    public CacheConfiguration? GetConfiguration(string controllerName, string actionName)");
        sb.AppendLine("    {");
        sb.AppendLine("        var key = $\"{controllerName}.{actionName}\";");
        sb.AppendLine("        return _configurations.TryGetValue(key, out var config) ? config : null;");
        sb.AppendLine("    }");
        sb.AppendLine();
        
        // GetAll method
        sb.AppendLine("    public IReadOnlyDictionary<string, CacheConfiguration> GetAllConfigurations()");
        sb.AppendLine("    {");
        sb.AppendLine("        return _configurations;");
        sb.AppendLine("    }");
        
        sb.AppendLine("}");
        
        return sb.ToString();
    }

    private static void GenerateStringArray(StringBuilder sb, string propertyName, 
        string[] values, string indent = "            ")
    {
        if (values.Length == 0)
        {
            sb.AppendLine($"{indent}{propertyName} = new string[0],");
        }
        else
        {
            sb.AppendLine($"{indent}{propertyName} = new string[] {{ {string.Join(", ", values.Select(v => $"\"{v}\""))} }},");
        }
    }

    private static void GenerateRequiredClasses(StringBuilder sb)
    {
        // InvalidationType enum
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// 캐시 무효화 타입");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public enum InvalidationType");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>연관된 모든 캐시 삭제</summary>");
        sb.AppendLine("    All,");
        sb.AppendLine("    /// <summary>패턴에 맞는 캐시만 삭제</summary>");
        sb.AppendLine("    Pattern,");
        sb.AppendLine("    /// <summary>연관 테이블까지 캐시 삭제</summary>");
        sb.AppendLine("    Related");
        sb.AppendLine("}");
        sb.AppendLine();

        // TableInvalidationRule class
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// 테이블 무효화 규칙");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public class TableInvalidationRule");
        sb.AppendLine("{");
        sb.AppendLine("    public string TableName { get; set; } = string.Empty;");
        sb.AppendLine("    public InvalidationType InvalidationType { get; set; } = InvalidationType.All;");
        sb.AppendLine("    public string? Pattern { get; set; }");
        sb.AppendLine("    public string[] RelatedTables { get; set; } = [];");
        sb.AppendLine("    public int MaxDepth { get; set; } = -1;");
        sb.AppendLine("}");
        sb.AppendLine();

        // CacheConfiguration class
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// 캐시 설정 정보");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public class CacheConfiguration");
        sb.AppendLine("{");
        sb.AppendLine("    public string Controller { get; set; } = string.Empty;");
        sb.AppendLine("    public string Action { get; set; } = string.Empty;");
        sb.AppendLine("    public bool Enabled { get; set; } = true;");
        sb.AppendLine("    public int ExpirationMinutes { get; set; } = -1;");
        sb.AppendLine("    public string? CustomKeyPrefix { get; set; }");
        sb.AppendLine("    public int MaxRelatedDepth { get; set; } = -1;");
        sb.AppendLine("    public string[] AdditionalKeyParameters { get; set; } = [];");
        sb.AppendLine("    public string[] ExcludeParameters { get; set; } = [];");
        sb.AppendLine("    public List<TableInvalidationRule> InvalidationRules { get; set; } = [];");
        sb.AppendLine("}");
        sb.AppendLine();
    }
}

// Data classes for collecting information
public class ControllerInfo(string name, List<ActionInfo> actions)
{
    public string Name { get; } = name;
    public List<ActionInfo> Actions { get; } = actions;
}

public class ActionInfo(string name, CacheSettings cacheSettings, List<InvalidationSettings> invalidationSettings)
{
    public string Name { get; } = name;
    public CacheSettings CacheSettings { get; } = cacheSettings;
    public List<InvalidationSettings> InvalidationSettings { get; } = invalidationSettings;
}

public class CacheSettings
{
    public bool Enabled { get; set; } = true;
    public int ExpirationMinutes { get; set; } = -1;
    public string? CustomKeyPrefix { get; set; }
    public int MaxRelatedDepth { get; set; } = -1;
    public string[] AdditionalKeyParameters { get; set; } = [];
    public string[] ExcludeParameters { get; set; } = [];
}

public class InvalidationSettings
{
    public string TableName { get; set; } = "";
    public string InvalidationType { get; set; } = "All";
    public string? Pattern { get; set; }
    public string[] RelatedTables { get; set; } = [];
    public int MaxDepth { get; set; } = -1;
}