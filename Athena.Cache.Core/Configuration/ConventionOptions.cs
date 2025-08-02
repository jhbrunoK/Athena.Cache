namespace Athena.Cache.Core.Configuration;

/// <summary>
/// Convention 기반 설정
/// </summary>
public class ConventionOptions
{
    /// <summary>Convention 기반 매핑 활성화</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>컨트롤러명에서 테이블명 추출 시 복수형 변환</summary>
    public bool UsePluralizer { get; set; } = true;

    /// <summary>Humanizer 라이브러리 사용</summary>
    public bool UseHumanizer { get; set; } = false;

    /// <summary>커스텀 복수형 변환 함수 (단일 테이블)</summary>
    public Func<string, string>? CustomPluralizer { get; set; }

    /// <summary>커스텀 다중 테이블 추론 함수</summary>
    public Func<string, string[]>? CustomMultiTableInferrer { get; set; }

    /// <summary>컨트롤러별 테이블 매핑 사전 (컨트롤러명 → 테이블명 배열)</summary>
    public Dictionary<string, string[]> ControllerTableMappings { get; set; } = new();

    /// <summary>Convention 기반 추론에서 제외할 컨트롤러명 목록</summary>
    public HashSet<string> ExcludedControllers { get; set; } = new();
}