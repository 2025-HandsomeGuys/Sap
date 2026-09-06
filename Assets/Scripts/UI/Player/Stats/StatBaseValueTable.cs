using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// StatType별 기준값(Base Value)을 Dictionary로 관리하는 테이블.
/// PlayerStat 내부에서 사용됨.
///
/// 직렬화 리스트(entries)와 런타임 Dictionary(_dict) 두 벌을 들고 있다.
/// 읽기는 _dict만 보므로, entries가 밖에서 바뀌면 _dict를 다시 지어야 한다.
/// ISerializationCallbackReceiver가 그 시점을 잡아준다 — 인스펙터에서 값을 고치면
/// Unity가 객체를 재역직렬화하면서 OnAfterDeserialize를 부르고, 캐시가 무효화된다.
/// 이게 없으면 플레이 중 인스펙터 수정이 조용히 무시된다.
/// </summary>
[System.Serializable]
public class StatBaseValueTable : ISerializationCallbackReceiver
{
    // Inspector 노출용 직렬화 래퍼
    [System.Serializable]
    private struct Entry
    {
        public StatType type;
        public float value;
    }

    [SerializeField] private List<Entry> entries = new List<Entry>();

    // 런타임 Dictionary (직렬화되지 않음)
    private Dictionary<StatType, float> _dict;
    private bool _initialized;

    private void EnsureInit()
    {
        if (_initialized) return;
        _dict = new Dictionary<StatType, float>();
        foreach (var e in entries)
        {
            _dict[e.type] = e.value;
        }
        _initialized = true;
    }

    public float Get(StatType type)
    {
        EnsureInit();
        return _dict.TryGetValue(type, out float val) ? val : 0f;
    }

    public void Set(StatType type, float value)
    {
        EnsureInit();
        _dict[type] = value;

        // 직렬화 목록도 동기화
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].type == type)
            {
                entries[i] = new Entry { type = type, value = value };
                return;
            }
        }
        entries.Add(new Entry { type = type, value = value });
    }

    // ===================================================
    // ISerializationCallbackReceiver
    // ===================================================
    // Set()이 entries를 항상 함께 갱신하므로 직렬화 직전에 따로 할 일이 없다.
    public void OnBeforeSerialize() { }

    /// <summary>
    /// 역직렬화 직후 — 인스펙터 수정, 프리팹 리로드, 씬 로드 모두 여기를 지난다.
    /// Unity 메인 스레드가 아닐 수 있으므로 Unity API를 부르지 않고 플래그만 내린다.
    /// 실제 재구축은 다음 Get/Set의 EnsureInit()에서 지연 수행된다.
    /// </summary>
    public void OnAfterDeserialize()
    {
        _initialized = false;
        _dict = null;
    }
}
