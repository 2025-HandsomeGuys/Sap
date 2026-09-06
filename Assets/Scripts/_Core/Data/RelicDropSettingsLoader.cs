// @tags: relic, drop, settings, loader, json, static, streamingassets
using System.IO;
using UnityEngine;

/// <summary>
/// StreamingAssets/relicDropSettings.json 로더.
///
/// 다른 설정 로더(ToolConfigLoader·SpecialChunkSettingsLoader)와 달리 <b>MonoBehaviour가 아니다.</b>
/// 유물 드롭은 <see cref="DiggableRock"/>(무한맵·정적청크·던전 어디서든 파괴됨)과 던전 상자에서
/// 불리는데, 씬마다 로더 오브젝트를 심어 두는 걸 잊으면 그 씬에서만 조용히 드롭이 사라진다.
/// 최초 접근 시 1회 읽는 static 캐시로 두면 씬 배치 의존이 없어진다.
///
/// ⚠ 메인 스레드에서만 호출할 것 — <c>Application.streamingAssetsPath</c> 제약.
/// </summary>
public static class RelicDropSettingsLoader
{
    private const string FILE_NAME = "relicDropSettings.json";

    private static RelicDropSettingsData _settings;

    /// <summary>설정. 파일이 없거나 깨졌으면 기본값(드롭 목록 비어 있음 = 드롭 없음)을 준다.</summary>
    public static RelicDropSettingsData Settings
    {
        get
        {
            if (_settings == null) Load();
            return _settings;
        }
    }

    /// <summary>파일을 다시 읽는다. 에디터에서 JSON을 고친 뒤 플레이 없이 확인할 때 쓴다.</summary>
    public static void Reload()
    {
        _settings = null;
        Load();
    }

    private static void Load()
    {
        string path = Path.Combine(Application.streamingAssetsPath, FILE_NAME);

        if (!File.Exists(path))
        {
            Debug.LogWarning($"[RelicDropSettings] {FILE_NAME} 없음 — 유물 드롭이 비활성화된다.");
            _settings = new RelicDropSettingsData { enabled = false };
            return;
        }

        try
        {
            _settings = JsonUtility.FromJson<RelicDropSettingsData>(File.ReadAllText(path));
            if (_settings == null) throw new System.Exception("FromJson이 null 반환");

            int layerCount = _settings.layers != null ? _settings.layers.Length : 0;
            Debug.Log($"[RelicDropSettings] 로드 완료: 지층 {layerCount}개, 티어 {_settings.HighestTier()}단계, " +
                      $"돌 드롭 확률 {_settings.rock.baseChance:P2}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[RelicDropSettings] 파싱 실패 — 드롭 비활성화. 오류: {e.Message}");
            _settings = new RelicDropSettingsData { enabled = false };
        }
    }
}
