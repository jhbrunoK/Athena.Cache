using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Athena.Cache.Core.Attributes;
using Athena.Cache.Core.Enums;
using Athena.Cache.Core.Interfaces;
using Athena.Cache.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace Athena.Cache.Core.Services;

/// <summary>
/// Reflection을 사용한 백업 캐시 설정 레지스트리
/// Source Generator가 없을 때 사용됩니다.
/// </summary>
public class ReflectionCacheConfigurationRegistry : ICacheConfigurationRegistry
{
    private readonly Dictionary<string, CacheConfiguration> _configurations;

    public ReflectionCacheConfigurationRegistry()
    {
        _configurations = new Dictionary<string, CacheConfiguration>();
        LoadConfigurations();
    }

    public CacheConfiguration? GetConfiguration(string controllerName, string actionName)
    {
        var key = $"{controllerName}.{actionName}";
        return _configurations.TryGetValue(key, out var config) ? config : null;
    }

    public IReadOnlyDictionary<string, CacheConfiguration> GetAllConfigurations()
    {
        return _configurations;
    }

    private void LoadConfigurations()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        
        foreach (var assembly in assemblies)
        {
            try
            {
                var controllerTypes = assembly.GetTypes()
                    .Where(t => t.IsClass && !t.IsAbstract && 
                               (t.IsSubclassOf(typeof(ControllerBase)) || t.Name.EndsWith("Controller")))
                    .ToList();

                foreach (var controllerType in controllerTypes)
                {
                    ProcessController(controllerType);
                }
            }
            catch (ReflectionTypeLoadException)
            {
                // 일부 어셈블리는 로드할 수 없을 수 있음
                continue;
            }
        }
    }

    private void ProcessController(Type controllerType)
    {
        var controllerName = controllerType.Name;
        var methods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.IsPublic && !m.IsStatic && m.DeclaringType == controllerType)
            .ToList();

        foreach (var method in methods)
        {
            var config = CreateCacheConfiguration(controllerType, method);
            if (config != null)
            {
                var key = $"{controllerName}.{method.Name}";
                _configurations[key] = config;
            }
        }
    }

    private CacheConfiguration? CreateCacheConfiguration(Type controllerType, MethodInfo method)
    {
        var cacheAttr = method.GetCustomAttribute<AthenaCacheAttribute>() ?? 
                       controllerType.GetCustomAttribute<AthenaCacheAttribute>();
        
        var invalidationAttrs = method.GetCustomAttributes<CacheInvalidateOnAttribute>()
            .Concat(controllerType.GetCustomAttributes<CacheInvalidateOnAttribute>())
            .ToList();

        var noCacheAttr = method.GetCustomAttribute<NoCacheAttribute>() ?? 
                         controllerType.GetCustomAttribute<NoCacheAttribute>();

        if (noCacheAttr != null || (cacheAttr == null && invalidationAttrs.Count == 0))
        {
            return null;
        }

        return new CacheConfiguration
        {
            Controller = controllerType.Name,
            Action = method.Name,
            Enabled = cacheAttr?.Enabled ?? true,
            ExpirationMinutes = cacheAttr?.ExpirationMinutes ?? -1,
            CustomKeyPrefix = cacheAttr?.CustomKeyPrefix,
            MaxRelatedDepth = cacheAttr?.MaxRelatedDepth ?? -1,
            AdditionalKeyParameters = cacheAttr?.AdditionalKeyParameters ?? new string[0],
            ExcludeParameters = cacheAttr?.ExcludeParameters ?? new string[0],
            InvalidationRules = invalidationAttrs.Select(attr => new TableInvalidationRule
            {
                TableName = attr.TableName,
                InvalidationType = attr.InvalidationType,
                Pattern = attr.Pattern,
                RelatedTables = attr.RelatedTables ?? new string[0],
                MaxDepth = attr.MaxDepth
            }).ToList()
        };
    }
}