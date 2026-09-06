// @tags: loading, scene, data-container, global, state
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class LoadingData
{
    /// <summary>
    /// 현재 로딩 중 여부. 로딩 중에는 입력 등을 차단하는 데 사용
    /// </summary>
    public static bool IsLoading = false;

    /// <summary>
    /// 다음에 불러올 씬의 이름
    /// </summary>
    public static string NextSceneName = string.Empty;

    /// <summary>
    /// 이전에 있던 씬의 이름 (Additive 로딩 시 Unload용)
    /// </summary>
    public static string PreviousSceneName = string.Empty;

    /// <summary>
    /// 씬이 그려지고 나서 내부 초기화 로직(맵 생성 등)이 완료되었는지 여부
    /// </summary>
    public static bool IsReady = true;

    /// <summary>
    /// 긴급 탈출이 진행 중인지 여부 (지상 씬 진입 시 페널티 적용을 위함)
    /// </summary>
    public static bool IsEmergencyEscapePending = false;
}
