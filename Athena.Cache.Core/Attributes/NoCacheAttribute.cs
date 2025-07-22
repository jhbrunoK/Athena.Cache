namespace Athena.Cache.Core.Attributes;

/// <summary>
/// 캐시 비활성화 Attribute (특정 액션만 제외하고 싶을 때)
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = true)]
public class NoCacheAttribute : Attribute
{
}