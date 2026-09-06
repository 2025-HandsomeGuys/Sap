// @tags: zone, wind, data-container, json, pattern
using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class WindPhaseData
{
    [Tooltip("바람이 부는 방향 (정규화 불필요)")]
    public Vector2 direction;
    
    [Tooltip("예고 구간(미풍) 세기")]
    public float breezeStrength;
    
    [Tooltip("예고 구간(미풍) 지속 시간(초)")]
    public float breezeDuration;
    
    [Tooltip("본격적인 바람(강풍) 세기")]
    public float activeStrength;
    
    [Tooltip("본격적인 바람(강풍) 지속 시간(초)")]
    public float activeDuration;
    
    [Tooltip("바람이 멈춘 후 휴식 시간(초)")]
    public float restDuration;
}

[System.Serializable]
public class WindPatternData
{
    [Tooltip("패턴 고유 식별자 (예: Blizzard_Lvl1)")]
    public string patternId;
    
    [Tooltip("순차적으로 실행될 바람 페이즈 목록")]
    public List<WindPhaseData> phases;
}

[System.Serializable]
public class WindPatternDatabase
{
    public List<WindPatternData> patterns;
}
