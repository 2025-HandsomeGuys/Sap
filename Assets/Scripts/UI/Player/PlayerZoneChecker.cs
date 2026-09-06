using System.Collections;
using UnityEngine;

public class PlayerZoneChecker : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("How often (in seconds) to check the player's current zone and apply effects.")]
    public float checkInterval = 1.0f;

    [Header("References")]
    [Tooltip("StaminaManager 레퍼런스 (비워두면 씬에서 자동 탐색)")]
    [SerializeField] private StaminaManager staminaManagerRef;

    private StaminaManager _staminaManager;
    private int _lastCheckedGridY = int.MinValue;

    // 현재 층의 체류 상태이상. tileData.json의 zoneStatusType / zoneStatusPerSecond에서 온다.
    // 파기 비용(maxStaminaReduction)을 여기서 쓰면 안 된다 — 1층 동상·마그마층 동상 버그의 원인이었다.
    private ZoneStatusType _zoneStatus = ZoneStatusType.None;
    private float _statusPerSecond = 0f;

    void Start()
    {
        // 1. 인스펙터 참조 우선
        _staminaManager = staminaManagerRef != null ? staminaManagerRef : GetComponent<StaminaManager>();
        
        // 2. 자신의 게임오브젝트에 없으면 씬에서 탐색 (매니저가 분리된 경우)
        if (_staminaManager == null)
        {
            _staminaManager = FindFirstObjectByType<StaminaManager>();
        }

        if (_staminaManager == null)
        {
            // 지상 씬(DemoUpground)에는 StaminaManager 오브젝트가 없다. 층 상태이상은 지하 전용이므로
            // 그냥 꺼지는 게 정상 — 여기서 LogError를 내면 지상 갈 때마다 콘솔이 빨개진다.
            // 지하(InfinityMapManager가 있는 씬)인데도 없으면 그건 진짜 세팅 누락이라 에러로 남긴다.
            if (InfinityMapManager.Instance != null)
                Debug.LogError("[PlayerZoneChecker] StaminaManager not found in scene! Zone effects will not be applied.", this);

            enabled = false;
            return;
        }

        // Wait for TileDataManager to be ready
        if (TileDataManager.Instance == null)
        {
             Debug.LogWarning("[PlayerZoneChecker] TileDataManager Instance is null at Start. It might initialize later.");
        }

        StartCoroutine(ZoneCheckCoroutine());
    }

    private IEnumerator ZoneCheckCoroutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(checkInterval);

            if (_staminaManager == null) continue;

            // Convert player's world position to grid cell position
            // Fixed NRE by removing missing WorldManager dependency and calculating manually
            // 10f is the ChunkHeightWorld derived from TerrainChunk settings (1000px / 100ppu)
            int playerGridY = Mathf.RoundToInt(transform.position.y / 10f);

            // Check if we moved to a new depth/zone
            if (playerGridY != _lastCheckedGridY)
            {
                UpdateZoneEffects(playerGridY);
                _lastCheckedGridY = playerGridY;
            }

            // 층별 상태이상 누적 — StaminaManager를 통해 MaxStamina를 깎는다
            if (_zoneStatus != ZoneStatusType.None && _statusPerSecond > 0f)
            {
                float amount = _statusPerSecond * checkInterval;
                switch (_zoneStatus)
                {
                    case ZoneStatusType.Frostbite: _staminaManager.AddFrostbite(amount); break;
                    case ZoneStatusType.Burn:      _staminaManager.AddBurn(amount);      break;
                    case ZoneStatusType.Radiation: _staminaManager.AddRadiation(amount); break;
                }
            }
        }
    }

    private void UpdateZoneEffects(int depth)
    {
        if (TileDataManager.Instance == null) return;

        TileType currentTileType = TileDataManager.Instance.GetTileTypeAtDepth(depth);
        TileDataJson tileData = TileDataManager.Instance.GetData(currentTileType);

        if (tileData != null)
        {
            if (!ZoneStatusSelector.TryParse(tileData.zoneStatusType, out _zoneStatus))
            {
                Debug.LogWarning($"[PlayerZoneChecker] tileData.json의 zoneStatusType '{tileData.zoneStatusType}'" +
                                 $" 를 해석하지 못했습니다 ({currentTileType} 층). 상태이상 없이 진행합니다.", this);
            }

            _statusPerSecond = tileData.zoneStatusPerSecond;
            // Debug.Log($"[PlayerZoneChecker] Entered Layer: {currentTileType} ({_zoneStatus} {_statusPerSecond}/s)");
        }
        else
        {
            _zoneStatus = ZoneStatusType.None;
            _statusPerSecond = 0f;
        }
    }
}
