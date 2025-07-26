using System.Collections.Generic;
using Athena.Cache.Core.Models;

namespace Athena.Cache.Core.Interfaces;

/// <summary>
/// 캐시 설정 레지스트리 인터페이스
/// </summary>
public interface ICacheConfigurationRegistry
{
    /// <summary>
    /// 컨트롤러와 액션에 대한 캐시 설정을 가져옵니다.
    /// </summary>
    CacheConfiguration? GetConfiguration(string controllerName, string actionName);
    
    /// <summary>
    /// 모든 캐시 설정을 가져옵니다.
    /// </summary>
    IReadOnlyDictionary<string, CacheConfiguration> GetAllConfigurations();
}