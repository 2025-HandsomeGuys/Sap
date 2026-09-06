// @tags: event, spawn, trap, digging, hazard, icicle
using UnityEngine;

/// <summary>
/// 특정 색상의 타일(Ice) 파괴 시 위 방향 레이캐스트로 천장을 탐지하여
/// FallingHazard 프리팹을 스폰하는 컴포넌트.
/// TileEventDispatcher에 자동 등록/해제된다.
///
/// [SOLID]
///   SRP: 색상 판별 + 천장 레이캐스트 + 낙하물 스폰만 담당.
///   OCP: 색상/레이어/프리팹은 Inspector 설정만으로 확장.
///   DIP: ITileDestroyListener / TileEventDispatcher 경유 — TerrainModifier 직접 의존 없음.
/// </summary>
public class IcicleSpawner : MonoBehaviour, ITileDestroyListener
{
    [Header("반응 설정")]
    [Tooltip("반응할 타일의 대표 색상 (Inspector에서 실제 Ice 타일 색상으로 맞출 것)")]
    public Color32 targetTileColor = new Color32(180, 220, 255, 255);

    [Tooltip("색상 매칭 허용 오차 (0-255, 권장 30-50)")]
    public int colorTolerance = 40;

    [Tooltip("낙하 위험 프리팹 (FallingHazard 등)")]
    public GameObject fallingHazardPrefab;

    [Tooltip("위 방향 레이캐스트 최대 거리 (유닛)")]
    public float raycastHeight = 20f;

    [Tooltip("천장 감지에 사용할 레이어 마스크 (테레인 콜라이더 레이어)")]
    public LayerMask ceilingLayer;

    private void OnEnable()
    {
        if (TileEventDispatcher.Instance != null)
            TileEventDispatcher.Instance.Register(this);
    }

    private void OnDisable()
    {
        if (TileEventDispatcher.Instance != null)
            TileEventDispatcher.Instance.Unregister(this);
    }

    public void OnTileDestroyed(Vector2 worldPos, Color32 destroyedColor)
    {
        if (!IsColorMatch(destroyedColor, targetTileColor, colorTolerance)) return;

        // 위 방향 레이캐스트로 천장 탐지
        RaycastHit2D hit = Physics2D.Raycast(worldPos, Vector2.up, raycastHeight, ceilingLayer);
        Debug.Log($"[IcicleSpawner] Ice 타일 파괴 @ {worldPos} — 천장 감지: {(hit.collider != null ? hit.point.ToString() : "없음")}");

        if (hit.collider != null && fallingHazardPrefab != null)
        {
            Instantiate(fallingHazardPrefab, hit.point, Quaternion.identity);
            Debug.Log($"[IcicleSpawner] 고드름 스폰 @ {hit.point}");
        }
        else if (fallingHazardPrefab == null)
        {
            Debug.LogWarning("[IcicleSpawner] fallingHazardPrefab이 null — 스폰 생략");
        }
    }

    private static bool IsColorMatch(Color32 a, Color32 b, int tolerance)
    {
        return Mathf.Abs(a.r - b.r) <= tolerance &&
               Mathf.Abs(a.g - b.g) <= tolerance &&
               Mathf.Abs(a.b - b.b) <= tolerance;
    }
}
