using Athena.Cache.Core.Middleware;
using Microsoft.Extensions.ObjectPool;

namespace Athena.Cache.Core.ObjectPools;

/// <summary>
/// CachedResponse 객체 풀링을 위한 정책 클래스
/// 메모리 할당을 최소화하고 객체 재사용을 통해 GC 압박을 줄임
/// </summary>
public class CachedResponsePooledObjectPolicy : PooledObjectPolicy<CachedResponse>
{
    public override CachedResponse Create()
    {
        return new CachedResponse();
    }

    public override bool Return(CachedResponse obj)
    {
        if (obj == null) return false;
        
        // 객체 상태 초기화 (재사용을 위해)
        obj.StatusCode = 0;
        obj.ContentType = string.Empty;
        obj.Content = string.Empty;
        obj.Headers?.Clear();
        obj.CachedAt = default;
        obj.ExpiresAt = default;
        
        return true;
    }
}

/// <summary>
/// CachedResponse 전용 객체 풀 관리자
/// </summary>
public class CachedResponsePool
{
    private readonly ObjectPool<CachedResponse> _pool;
    
    public CachedResponsePool(IServiceProvider serviceProvider)
    {
        var poolProvider = serviceProvider.GetService<ObjectPoolProvider>() 
            ?? new DefaultObjectPoolProvider();
            
        _pool = poolProvider.Create(new CachedResponsePooledObjectPolicy());
    }
    
    /// <summary>
    /// 풀에서 CachedResponse 객체 가져오기
    /// </summary>
    public CachedResponse Get() => _pool.Get();
    
    /// <summary>
    /// 사용 완료된 객체를 풀에 반환
    /// </summary>
    public void Return(CachedResponse response) => _pool.Return(response);
}