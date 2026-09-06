// @tags: event, explosion, mineral, spawn, digging, trap
using UnityEngine;
using System.Collections;

/// <summary>
/// 특정 색상의 타일이 파괴될 때 폭발 위험 오브젝트를 지연 스폰하는 컴포넌트.
/// TileEventDispatcher에 자동 등록/해제된다.
///
/// [SOLID]
///   SRP: 색상 판별 + 지연 스폰만 담당.
///   OCP: 색상/지연/프리팹은 Inspector 설정만으로 확장.
///   DIP: TerrainModifier에 직접 의존하지 않고 ITileDestroyListener / TileEventDispatcher 경유.
/// </summary>
public class ExplosiveMineralReactor : MonoBehaviour, ITileDestroyListener
{
    [Header("반응 설정")]
    [Tooltip("반응할 타일의 대표 색상 (Inspector에서 실제 타일 색상으로 맞출 것)")]
    public Color32 targetTileColor = new Color32(200, 50, 0, 255);

    [Tooltip("색상 매칭 허용 오차 (0-255, 권장 30-50)")]
    public int colorTolerance = 40;

    [Tooltip("폭발 위험 프리팹 (ProjectileHazard 등)")]
    public GameObject projectileHazardPrefab;

    [Tooltip("타일 파괴 후 폭발까지 지연 시간 (초)")]
    public float blastDelay = 2f;

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
        Debug.Log($"[ExplosiveMineralReactor] 대상 타일 파괴 감지 @ {worldPos} — {blastDelay}초 후 폭발");
        StartCoroutine(DelayedBlast(worldPos));
    }

    private IEnumerator DelayedBlast(Vector2 pos)
    {
        yield return new WaitForSeconds(blastDelay);

        if (projectileHazardPrefab != null)
        {
            Instantiate(projectileHazardPrefab, pos, Quaternion.identity);
            Debug.Log($"[ExplosiveMineralReactor] 폭발 스폰 @ {pos}");
        }
        else
        {
            Debug.LogWarning("[ExplosiveMineralReactor] projectileHazardPrefab이 null — 스폰 생략");
        }
    }

    private static bool IsColorMatch(Color32 a, Color32 b, int tolerance)
    {
        return Mathf.Abs(a.r - b.r) <= tolerance &&
               Mathf.Abs(a.g - b.g) <= tolerance &&
               Mathf.Abs(a.b - b.b) <= tolerance;
    }
}
