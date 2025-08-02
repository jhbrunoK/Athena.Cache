namespace Athena.Cache.Core.Attributes;

/// <summary>
/// Convention 기반 테이블명 추론을 비활성화하는 Attribute
/// 이 어트리뷰트가 적용된 컨트롤러/액션은 명시적으로 선언된 CacheInvalidateOn만 사용하고,
/// Convention 기반 자동 추론은 수행하지 않습니다.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public class NoConventionInvalidationAttribute : Attribute
{
}