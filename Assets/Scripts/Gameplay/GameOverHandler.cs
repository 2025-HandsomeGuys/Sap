// @tags: game-over, death, cinematic, stamina, mineral, inventory, scene, event
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 지하 사망 처리 전담 컴포넌트.
/// <see cref="PlayerStat.OnStaminaDepleted"/>를 받아 게임오버 연출을 띄우고 지상으로 돌려보낸다.
///
///   1. <see cref="GameOverSequenceUI"/> 재생 — HUD 페이드아웃 → 주변 암전 → 죽는 모션 → 완전 암전
///   2. (연출이 흐르는 동안) 미네랄 버스트 VFX + 짐 정리 —
///      <see cref="CarryLossPenalty"/>로 **가장 비싼 광물 1~2개만** 창고에 남기고 나머지는 소실,
///      소비아이템은 전부 소실, 장비·골드는 유지 (긴급 탈출과 같은 규칙)
///   3. 살아남은 광물만 창고에 덧쓰고(<c>SaveManager.PersistWarehouseOnly</c>) 로딩씬 → 지상 씬
///   4. 지상 도착 후 UIStateManager가 <see cref="EmergencyEscapeReport"/>.HasPending을 보고
///      정산창(<c>EmergencyEscapeOverlayUI</c>)을 띄운다 — 긴급 탈출과 같은 창, 문구만 사망용으로 갈린다
///
/// 전체 저장을 하지 않는 이유: 지하 진행은 원래 저장되지 않는다(강제 종료와 동일 처리).
/// 파일에는 지하 진입 전 지상 데이터가 그대로 남아 있어 그것이 복구된다.
/// 창고만 따로 덧쓰는 이유는 <c>SaveManager.PersistWarehouseOnly</c> 주석 참고.
///
/// 씬 세팅은 필요 없다 — <see cref="AutoAttachScenes"/>에 등록된 씬에서는 플레이어(PlayerStat)에
/// 자동으로 붙는다. 직접 붙여 인스펙터로 조정해도 되며, 그 경우 자동 부착은 건너뛴다.
/// </summary>
public class GameOverHandler : MonoBehaviour
{
    // ===================================================
    // 자동 부착 (씬 세팅 불필요)
    // ===================================================
    /// <summary>이 씬들에서는 컴포넌트를 배치하지 않아도 플레이어에 자동으로 붙는다.</summary>
    public static string[] AutoAttachScenes = { "DemoUnderground" };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TryAutoAttach(SceneManager.GetActiveScene().name);
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TryAutoAttach(scene.name);

    private static void TryAutoAttach(string sceneName)
    {
        if (System.Array.IndexOf(AutoAttachScenes, sceneName) < 0) return;

        var stat = FindFirstObjectByType<PlayerStat>(FindObjectsInactive.Include);
        if (stat == null) return;
        if (stat.GetComponent<GameOverHandler>() != null) return; // 씬에 이미 붙여 뒀다면 그대로 둔다

        stat.gameObject.AddComponent<GameOverHandler>();
    }

    // ===================================================
    // 인스펙터
    // ===================================================
    [Header("플레이어 참조 (비워두면 자동 탐색)")]
    [SerializeField] private PlayerStat playerStat;
    [SerializeField] private MineralInventory mineralInventory;

    [Header("미네랄 버스트 VFX (선택)")]
    [Tooltip("플레이어 위치에서 재생할 파티클 시스템. 비워두면 생략한다.")]
    [SerializeField] private ParticleSystem mineralBurstVFX;
    [Tooltip("VFX 재생 후 가방을 비울 때까지 대기 시간 (초). HUD가 사라진 뒤에 비워지도록 잡는다.")]
    [SerializeField] private float burstDuration = 1.0f;

    [Header("씬 전환")]
    [SerializeField] private string targetScene = "DemoUpground";

    private bool _isGameOver;

    // ===================================================
    // 수명 주기
    // ===================================================
    private void Awake()
    {
        if (playerStat == null) playerStat = GetComponentInParent<PlayerStat>();
        if (playerStat == null) playerStat = GetComponent<PlayerStat>();

        if (mineralInventory == null && playerStat != null)
            mineralInventory = playerStat.GetComponentInChildren<MineralInventory>(true);
        if (mineralInventory == null && InventoryUI.Instance != null)
            mineralInventory = InventoryUI.Instance.mineralInventory;
        if (mineralInventory == null)
            mineralInventory = FindFirstObjectByType<MineralInventory>(FindObjectsInactive.Include);
    }

    private void OnEnable()
    {
        if (playerStat != null)
            playerStat.OnStaminaDepleted += TriggerGameOver;
    }

    private void OnDisable()
    {
        if (playerStat != null)
            playerStat.OnStaminaDepleted -= TriggerGameOver;
    }

    // ===================================================
    // 게임오버
    // ===================================================
    public void TriggerGameOver()
    {
        // 긴급 탈출 연출이 이미 도는 중이면(탈출 확정 직후 스태미나가 0이 되는 등) 그쪽 흐름을 존중한다.
        if (_isGameOver || GameOverSequenceUI.IsPlaying) return;
        _isGameOver = true;

        if (SoundManager.Instance != null)
        {
            // 스태미나 0으로 죽는 경우 심장 루프가 울리는 중이다
            SoundManager.Instance.StopLoop(HeartbeatSfx.LoopHandle, 0.1f);
            SoundManager.Instance.PlaySFX(SfxKeys.GameOver);
        }

        // 잠수 추적을 끊으면서 dive_end("death")를 남긴다.
        //
        // ⚠ 2026-08-31까지 이 호출이 없어서 **사망한 잠수가 로그에 아예 안 남았다**
        //   (실측 dive_start 212 vs dive_end 113). 그 탓에 밸런스 분석이 전부
        //   '살아 돌아온 잠수'만 보고 있었다 — 실패가 잦은 구간일수록 수치가
        //   실제보다 좋게 나오는 생존 편향이다.
        //   StopTracking이 아니라 AbortTracking인 이유: 사망은 일반 정산 팝업이 뜨면
        //   안 되고(광물을 잃었다), 누적값도 리셋되어야 한다. 긴급 탈출과 같은 처리다.
        //   대신 뜨는 것이 EmergencyEscapeOverlayUI(사망 문구)다.
        //   배경: Assets/Docs/economy/upgrade-balance-charter.md §6-B
        if (SettlementManager.Instance != null)
            SettlementManager.Instance.AbortTracking("death");

        Transform player = playerStat != null ? playerStat.transform : transform;
        string scene = targetScene;

        // 죽어서 올라가도 내려갈 때 쓴 입구 앞에 나온다.
        // 이 경로는 저장을 하지 않으므로 파일의 기록은 그대로 남지만, 다음 잠수에서 덮어써지고
        // 소비는 static Pending 쪽에서만 일어나므로 중복 적용되지 않는다.
        SurfaceReturnRouter.HandOff();

        // 연출이 끝나면 로딩씬 → 지상. 전체 저장은 하지 않는다(지하 진행은 강제 종료와 동일 처리).
        // 살아남은 광물 1~2개만 LoseCarriedItems가 창고 필드에 덧쓴다.
        GameOverSequenceUI.Play(GameOverReason.Death, player, () =>
        {
            Debug.Log("[GameOverHandler] 지하 사망 — 전체 저장 없이 지상으로 이동 (데이터 복구, 창고만 덧씀)");
            SceneLoader.LoadScene(scene);
        });

        StartCoroutine(LoseCarriedItems());
    }

    /// <summary>
    /// 짐을 정리한다 — 긴급 탈출과 같은 규칙으로 <b>가장 비싼 광물 1~2개만</b> 창고에 남기고,
    /// 나머지 광물과 소비아이템은 전부 잃는다. 장비와 골드는 그대로 유지한다.
    ///
    /// 창고 병합 후 <c>PersistWarehouseOnly</c>로 창고 필드만 파일에 덧쓴다(전체 저장 금지 —
    /// 지하 진행은 저장하지 않는다는 규칙을 깨면 지하에서의 스탯·위치가 굳어 버린다).
    /// </summary>
    private IEnumerator LoseCarriedItems()
    {
        if (mineralBurstVFX != null)
            mineralBurstVFX.Play();

        // HUD가 페이드아웃된 뒤에 수치가 0이 되도록 잠깐 기다린다(카운터가 눈앞에서 깎이는 걸 감춘다).
        for (float t = 0f; t < burstDuration; t += Time.unscaledDeltaTime)
            yield return null;

        // 1) 페널티 — 살아남을 광물만 인벤토리에 남고, 잃은/챙긴 내역이 정산창용으로 기록된다.
        CarryLossPenalty.Apply(mineralInventory, CarryLossReason.Death);

        // 2) 살아남은 광물을 창고로 옮긴다. 소비아이템은 함께 보내지 않는다(사망 시 전부 소실).
        var warehouse = WarehouseManager.Instance;
        if (warehouse != null && mineralInventory != null)
            warehouse.DepositAllFromInventory(null, mineralInventory, null);

        // 3) 창고 필드만 파일에 덧쓴다(지하 진행 전체는 여전히 저장하지 않는다).
        //    지하 씬엔 WarehouseManager가 없을 수 있어 2)의 병합이 통째로 건너뛰어진다 —
        //    그 경우를 위해 살아남은 광물을 넘겨, 파일의 창고 목록에 직접 합치게 한다.
        var save = (GameManager.Instance != null) ? GameManager.Instance.saveManager : null;
        if (save == null) save = FindFirstObjectByType<SaveManager>(FindObjectsInactive.Include);
        if (save != null) save.PersistWarehouseOnly(mineralInventory);

        // 4) 가방을 비운다(광물은 창고로 갔고, 소비아이템은 잃는다).
        if (mineralInventory != null)
            mineralInventory.FromData(new MineralInventoryData(), MineralDatabase.Instance);

        ItemInventory itemInv = null;
        if (InventoryUI.Instance != null)
            itemInv = InventoryUI.Instance.itemInventory;
        if (itemInv == null)
            itemInv = FindFirstObjectByType<ItemInventory>(FindObjectsInactive.Include);
        if (itemInv != null)
            itemInv.FromData(new ItemInventoryData(), ItemDatabase.Instance);
    }
}
