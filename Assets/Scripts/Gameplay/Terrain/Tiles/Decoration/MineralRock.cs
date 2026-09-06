// @tags: rock, mineral, drop, ore, decoration, special-chunk
using UnityEngine;

/// <summary>
/// "광물돌"의 정체성 컴포넌트. DiggableRock과 같은 GameObject에 부착한다.
///  - 이 돌이 어떤 광물(ore)인지 보유
///  - 파괴 시 그 광물을 확정 개수 드롭 (IRockDropOverride)
///  - specialChunkSettings.json의 mineralRock 설정으로 HP·드롭 개수를 광물별 주입
/// 발광색은 MineralRockGlow가 따로 보유한다 (SRP).
/// Loader(-150) 다음, DiggableRock.OnEnable(_currentHp=MaxHp) 전에 MaxHp를 세팅하기 위해 -140.
/// </summary>
[DefaultExecutionOrder(-140)]
[RequireComponent(typeof(DiggableRock))]
public class MineralRock : MonoBehaviour, IRockDropOverride
{
    [Header("정체성")]
    [Tooltip("이 광물돌이 드롭하는 광물")]
    [SerializeField] private MineralSO ore;

    private int _minDrop = 2;
    private int _maxDrop = 4;

    private void Awake()
    {
        var section = (SpecialChunkSettingsLoader.Instance != null)
            ? SpecialChunkSettingsLoader.Instance.Settings.mineralRock
            : new SpecialChunkSettingsData.MineralRockSection();

        string id = ore != null ? ore.mineralID.ToString() : "";
        var cfg = section.Resolve(id);

        _minDrop = cfg.minDrop;
        _maxDrop = cfg.maxDrop;

        // DiggableRock.OnEnable()이 이후 _currentHp = MaxHp 로 초기화 → 순서 안전
        var rock = GetComponent<DiggableRock>();
        if (rock != null) rock.MaxHp = cfg.maxHp;
    }

    // IRockDropOverride — DiggableRock.DestroyRock()에서 호출
    public bool TryDropOnDestroy(Vector3 rockCenter)
    {
        if (ore == null) return false; // 광물 미지정 → 기본 드롭으로 폴백

        // 광물돌도 "돌에서 나오는 광물"이므로 같은 업그레이드를 탄다.
        int count = RockDropBonus.Apply(Mathf.Max(0, Random.Range(_minDrop, _maxDrop + 1)));
        for (int i = 0; i < count; i++)
        {
            Vector3 pos = new Vector3(
                rockCenter.x + Random.Range(-0.05f, 0.05f),
                rockCenter.y,
                -1f);
            MineralDropHelper.Drop(ore, pos);
        }
        return true;
    }
}
