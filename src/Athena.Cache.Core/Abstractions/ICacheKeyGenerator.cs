namespace Athena.Cache.Core.Abstractions;

/// <summary>
/// 캐시 키 생성기 인터페이스
/// </summary>
public interface ICacheKeyGenerator
{
    /// <summary>
    /// API 요청 파라미터를 기반으로 캐시 키 생성 (동기)
    /// </summary>
    string GenerateKey(string controller, string action, IDictionary<string, object?>? parameters = null);

    /// <summary>
    /// API 요청 파라미터를 기반으로 캐시 키 생성 (비동기 최적화)
    /// </summary>
    ValueTask<string> GenerateKeyAsync(string controller, string action, IDictionary<string, object?>? parameters = null);

    /// <summary>
    /// 테이블 추적용 키 생성
    /// </summary>
    string GenerateTableTrackingKey(string tableName);

    /// <summary>
    /// 파라미터 해시 생성
    /// </summary>
    string GenerateParameterHash(IDictionary<string, object?>? parameters);
}