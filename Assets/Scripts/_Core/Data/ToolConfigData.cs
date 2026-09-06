// @tags: tool, config, data-container, dto, json, unlock
/// <summary>
/// toolConfig.json 의 데이터 모델.
/// 인덱스는 ToolController.toolSprites 배열 순서와 일치해야 한다.
/// 빈 문자열이면 해금 조건 없음 (항상 선택 가능).
/// </summary>
[System.Serializable]
public class ToolConfigData
{
    public string[] unlockNodeIds = new string[0];

    /// <summary>표시용 이름(한글). 비어 있으면 "도구 N"으로 표기된다. 디버그 콘솔 출력용.</summary>
    public string[] names = new string[0];

    /// <summary>
    /// 디버그 콘솔 입력용 영문 키(예: drill). 콘솔은 IME 없이 치는 경우가 많아
    /// <see cref="names"/>(한글)만으로는 지목이 불편하다.
    /// </summary>
    public string[] debugAliases = new string[0];

    /// <summary>표시용 이름. 범위를 벗어나거나 비어 있으면 인덱스 표기로 대체.</summary>
    public string NameOf(int index)
    {
        if (names != null && index >= 0 && index < names.Length && !string.IsNullOrEmpty(names[index]))
            return names[index];
        return $"도구 {index}";
    }

    public string AliasOf(int index)
    {
        if (debugAliases != null && index >= 0 && index < debugAliases.Length)
            return debugAliases[index];
        return null;
    }

    public string NodeIdOf(int index)
    {
        if (unlockNodeIds != null && index >= 0 && index < unlockNodeIds.Length)
            return unlockNodeIds[index];
        return null;
    }

    /// <summary>세 배열 중 가장 긴 길이 — 콘솔이 도구 목록을 훑을 때의 상한.</summary>
    public int ToolCount
    {
        get
        {
            int n = unlockNodeIds?.Length ?? 0;
            if (names != null && names.Length > n) n = names.Length;
            if (debugAliases != null && debugAliases.Length > n) n = debugAliases.Length;
            return n;
        }
    }
}
