using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Athena.Cache.Redis
{
    /// <summary>
    /// Redis 캐시 설정 옵션
    /// </summary>
    public class RedisCacheOptions
    {
        /// <summary>Redis 연결 문자열</summary>
        public string ConnectionString { get; set; } = "localhost:6379";

        /// <summary>사용할 데이터베이스 번호 (기본: 0)</summary>
        public int DatabaseId { get; set; } = 0;

        /// <summary>키 접두사 (네임스페이스 추가 분리)</summary>
        public string? KeyPrefix { get; set; }

        /// <summary>배치 처리 크기</summary>
        public int BatchSize { get; set; } = 1000;

        /// <summary>연결 재시도 횟수</summary>
        public int RetryCount { get; set; } = 3;

        /// <summary>연결 타임아웃 (초)</summary>
        public int ConnectTimeoutSeconds { get; set; } = 5;

        /// <summary>JSON 직렬화 옵션</summary>
        public JsonSerializerOptions JsonOptions { get; set; } = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }
}
