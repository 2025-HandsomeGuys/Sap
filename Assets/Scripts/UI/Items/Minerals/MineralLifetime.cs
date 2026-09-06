// @tags: mineral, lifetime, despawn, timer, vfx
using System.Collections;
using UnityEngine;

/// <summary>
/// 월드에 떠 있는 광물에 붙는 자동 소멸 타이머.
/// OnEnable 시 부모가 TerrainChunk이면 타이머를 시작하지 않는다 (지형 내장 상태).
/// 청크 언로드로 분리되거나 DiggableRock 드롭 시 AddComponent로 붙인다.
///
/// [중요] 수명은 "지형 밖으로 드러나 있는 동안"에만 흐른다.
/// 타이머를 켜는 시점은 MineralItemController.WakeUp()(지지율 &lt; 0.8)인데,
/// 그 조건은 광물이 여전히 지형에 갇혀 화면에 안 보이는 상태에서도 성립한다.
/// PickupableItem은 IsExposed를 요구하므로, 카운트를 멈추지 않으면
/// "못 먹는 채로 60초를 다 쓰고 사라지는" 광물이 생긴다 — 픽업 가능 조건과 수명 카운트 조건은 같아야 한다.
/// </summary>
public class MineralLifetime : MonoBehaviour
{
    public const float LIFETIME_SECONDS = 60f;

    /// <summary>
    /// 진단 로그 스위치. 켜면 타이머 시작·노출 상태 전환·소멸 시점을 콘솔에 남기고,
    /// 소멸 순간에는 그 좌표를 덮고 있는 SpriteRenderer(스프라이트 지형·특수청크 아트 등)까지 조사한다.
    /// "안 나온 것처럼 보이는데 사라진다"의 원인이 픽셀 판정인지 시각 가림인지 가르기 위한 것.
    /// ⚠ 소멸 시 씬 전체 SpriteRenderer를 순회하므로 진단이 끝나면 반드시 false로 되돌릴 것.
    /// </summary>
    public static bool VerboseLog = true;

    // 노출 여부 폴링 주기. 매 프레임 지형 픽셀을 조회하면 월드 광물 수에 비례해 비용이 붙는다.
    private const float TICK_SECONDS = 0.25f;
    private static readonly WaitForSeconds s_tick = new WaitForSeconds(TICK_SECONDS);

    private MineralItemController _controller;
    private SpriteRenderer _renderer;

    private void Awake()
    {
        _controller = GetComponent<MineralItemController>();
        _renderer = GetComponentInChildren<SpriteRenderer>();
    }

    private void OnEnable()
    {
        StopAllCoroutines();
        if (GetComponentInParent<TerrainChunk>() != null) return;
        StartCoroutine(DespawnCoroutine());
    }

    private IEnumerator DespawnCoroutine()
    {
        float remaining = LIFETIME_SECONDS;

        // 진단 통계: 시간이 실제로 깎인 틱 / 파묻혀서 멈춘 틱
        int countedTicks = 0;
        int frozenTicks = 0;
        bool lastExposed = true;
        bool firstTick = true;

        if (VerboseLog)
            Debug.Log($"[MineralLifetime] START {name} — {Describe()}\n  {DescribeMap()}", this);

        while (remaining > 0f)
        {
            yield return s_tick;

            // Scene 뷰에 위치를 X자로 표시(초록=IsExposed, 빨강=파묻힘). Gizmos 켜면 보인다.
            if (VerboseLog && _controller != null) _controller.DrawDebugMarker(TICK_SECONDS);

            // 아직 지형에 파묻혀 있으면 시간을 깎지 않는다 (파내는 순간부터 다시 흐른다).
            // 컨트롤러가 없는 드롭 아이템은 판정 대상이 아니므로 그대로 카운트한다.
            bool exposed = _controller == null || _controller.IsExposed;

            if (VerboseLog && (firstTick || exposed != lastExposed))
            {
                Debug.Log($"[MineralLifetime] {(exposed ? "COUNTING" : "FROZEN")} {name} " +
                          $"remaining={remaining:F1}s — {Describe()}\n  {DescribeMap()}", this);
                lastExposed = exposed;
                firstTick = false;
            }

            if (!exposed)
            {
                frozenTicks++;
                continue;
            }

            countedTicks++;
            remaining -= TICK_SECONDS;
        }

        if (VerboseLog)
        {
            Debug.LogWarning($"[MineralLifetime] DESPAWN {name} — counted={countedTicks * TICK_SECONDS:F1}s " +
                             $"frozen={frozenTicks * TICK_SECONDS:F1}s\n  {Describe()}\n  {DescribeMap()}\n  {DescribeOccluders()}", this);
        }

        TerrainParticleManager.Instance?.SpawnImpact(transform.position, new Color32(255, 220, 80, 255), 8, 5f, 5f);
        MineralGenerator.ReturnToPool(gameObject);
    }

    private string DescribeMap()
        => _controller != null ? _controller.DescribeExposureMap() : "(컨트롤러 없음 — 노출 맵 없음)";

    private string Describe()
    {
        string mine = _controller != null ? _controller.DescribeExposure() : $"pos={transform.position} (컨트롤러 없음 = 드롭 아이템)";
        string vis = _renderer != null ? $" rendererEnabled={_renderer.enabled} inFrustum={_renderer.isVisible} order={_renderer.sortingLayerName}:{_renderer.sortingOrder}" : "";
        return mine + vis;
    }

    /// <summary>
    /// 소멸 좌표를 덮고 있는(= 광물보다 앞에 그려지는) SpriteRenderer를 나열한다.
    /// 스프라이트 지형(SpriteTerrainBorder가 붙은 특수청크 아트 등)이 픽셀 지형과 무관하게
    /// 광물을 가리고 있었는지 확인하는 용도. 읽기 가능한 텍스처면 해당 지점의 알파까지 찍는다.
    /// </summary>
    private string DescribeOccluders()
    {
        Vector3 p = transform.position;
        int myLayerValue = _renderer != null ? SortingLayer.GetLayerValueFromID(_renderer.sortingLayerID) : int.MinValue;
        int myOrder = _renderer != null ? _renderer.sortingOrder : int.MinValue;

        var all = UnityEngine.Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None);
        var sb = new System.Text.StringBuilder("occluders: ");
        int found = 0;

        foreach (var sr in all)
        {
            if (sr == null || sr == _renderer || !sr.enabled) continue;
            if (sr.transform.IsChildOf(transform)) continue;
            if (!sr.bounds.Contains(new Vector3(p.x, p.y, sr.bounds.center.z))) continue;

            int layerValue = SortingLayer.GetLayerValueFromID(sr.sortingLayerID);
            bool inFront = layerValue > myLayerValue || (layerValue == myLayerValue && sr.sortingOrder > myOrder);
            if (!inFront) continue;

            sb.Append($"\n    [{(found + 1)}] {sr.name} ({sr.gameObject.scene.name}) " +
                      $"order={sr.sortingLayerName}:{sr.sortingOrder} alphaAtPoint={AlphaAt(sr, p)}");
            if (sr.GetComponent<SpriteTerrainBorder>() != null) sb.Append(" ← SpriteTerrainBorder");
            found++;
            if (found >= 8) { sb.Append("\n    ... (이하 생략)"); break; }
        }

        if (found == 0) sb.Append("없음 (앞에 그려지는 스프라이트가 이 좌표를 덮고 있지 않음)");
        return sb.ToString();
    }

    /// 스프라이트의 해당 월드 좌표 알파. 텍스처가 Read/Write 아니면 "n/a".
    private static string AlphaAt(SpriteRenderer sr, Vector3 worldPos)
    {
        Sprite s = sr.sprite;
        if (s == null || s.texture == null || !s.texture.isReadable) return "n/a";

        Vector3 local = sr.transform.InverseTransformPoint(worldPos);
        float ppu = s.pixelsPerUnit;
        int px = Mathf.FloorToInt(local.x * ppu + s.pivot.x + s.textureRect.x);
        int py = Mathf.FloorToInt(local.y * ppu + s.pivot.y + s.textureRect.y);
        if (px < 0 || py < 0 || px >= s.texture.width || py >= s.texture.height) return "out";

        return s.texture.GetPixel(px, py).a.ToString("F2");
    }
}
