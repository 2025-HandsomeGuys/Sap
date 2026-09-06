using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 플레이어 주변 콜라이더를 즉시 진단하는 디버그 도구.
/// F9: 주변 콜라이더 전체 스캔 + 로그
/// F10: 아래쪽 Raycast로 막힌 콜라이더만 강조
/// Gizmos는 Scene View에서 시각화됨.
/// </summary>
public class ColliderDebugger : MonoBehaviour
{
    [Header("Keys")]
    [SerializeField] private KeyCode scanKey = KeyCode.F9;
    [SerializeField] private KeyCode raycastKey = KeyCode.F10;

    [Header("Scan Settings")]
    [SerializeField] private float scanRadius = 5f;
    [SerializeField] private float gizmoDisplayDuration = 15f;

    [Header("Raycast Settings")]
    [SerializeField] private float rayLength = 3f;
    [SerializeField] private int rayCount = 5; // 플레이어 폭에 걸쳐 여러 방향

    private List<SnapshotCollider> _snapshot = new List<SnapshotCollider>();
    private List<SnapshotRay> _raySnapshot = new List<SnapshotRay>();
    private float _snapshotTime = -999f;

    private class SnapshotCollider
    {
        public Collider2D col;
        public Vector3 worldCenter;
        public bool isInvisible; // 렌더러 없고 trigger도 아님
    }

    private class SnapshotRay
    {
        public Vector2 origin;
        public Vector2 direction;
        public RaycastHit2D hit;
        public bool didHit;
    }

    #region Unity Lifecycle

    private void Update()
    {
        if (Input.GetKeyDown(scanKey))
            RunScan();

        if (Input.GetKeyDown(raycastKey))
            RunRaycast();
    }

    #endregion

    #region Scan

    private void RunScan()
    {
        _snapshot.Clear();
        _snapshotTime = Time.time;

        Vector2 pos = transform.position;
        Collider2D[] cols = Physics2D.OverlapCircleAll(pos, scanRadius);

        // 거리순 정렬
        System.Array.Sort(cols, (a, b) =>
            Vector2.Distance(pos, a.bounds.center).CompareTo(Vector2.Distance(pos, b.bounds.center)));

        Debug.Log($"[ColliderDebugger] ===== SCAN @ {pos} radius={scanRadius} =====");
        Debug.Log($"[ColliderDebugger] 발견된 Collider2D 수: {cols.Length}");

        foreach (var col in cols)
        {
            if (col == null) continue;

            bool invisible = !HasRenderer(col.gameObject) && !col.isTrigger;
            float dist = Vector2.Distance(pos, col.bounds.center);
            string chunk = "";
            var tc = col.GetComponentInParent<TerrainChunk>();
            if (tc != null) chunk = $" [Chunk {tc.ChunkX},{tc.ChunkY}]";

            string warn = invisible ? " ⚠️ INVISIBLE NON-TRIGGER" : "";
            Debug.Log($"[ColliderDebugger] dist={dist:F2}  {col.GetType().Name}  GO={GetPath(col.gameObject)}" +
                      $"  layer={LayerMask.LayerToName(col.gameObject.layer)}" +
                      $"  trigger={col.isTrigger}  enabled={col.enabled}{chunk}{warn}");

            _snapshot.Add(new SnapshotCollider
            {
                col = col,
                worldCenter = col.bounds.center,
                isInvisible = invisible
            });
        }

        Debug.Log($"[ColliderDebugger] ==========================================");
    }

    #endregion

    #region Raycast

    private void RunRaycast()
    {
        _raySnapshot.Clear();
        _snapshotTime = Time.time;

        Vector2 pos = transform.position;
        float halfWidth = 0.4f; // 플레이어 폭 절반 (필요 시 조정)
        float step = halfWidth * 2f / Mathf.Max(1, rayCount - 1);

        Vector2[] directions = { Vector2.down, Vector2.up, Vector2.left, Vector2.right };

        Debug.Log($"[ColliderDebugger] ===== RAYCAST @ {pos} =====");

        foreach (var dir in directions)
        {
            // 아래/위는 여러 레이, 좌우는 단일 레이
            int count = (dir == Vector2.down || dir == Vector2.up) ? rayCount : 1;

            for (int i = 0; i < count; i++)
            {
                Vector2 origin = pos;
                if (count > 1)
                {
                    float offset = -halfWidth + step * i;
                    Vector2 perp = new Vector2(-dir.y, dir.x); // 수직 방향
                    origin = pos + perp * offset;
                }

                RaycastHit2D hit = Physics2D.Raycast(origin, dir, rayLength);

                if (hit.collider != null)
                {
                    string chunk = "";
                    var tc = hit.collider.GetComponentInParent<TerrainChunk>();
                    if (tc != null) chunk = $" [Chunk {tc.ChunkX},{tc.ChunkY}]";

                    Debug.Log($"[ColliderDebugger] Ray {dir} from {origin:F2}" +
                              $" → HIT {hit.collider.GetType().Name} GO={GetPath(hit.collider.gameObject)}" +
                              $" dist={hit.distance:F2} point={hit.point}{chunk}");
                }

                _raySnapshot.Add(new SnapshotRay
                {
                    origin = origin,
                    direction = dir,
                    hit = hit,
                    didHit = hit.collider != null
                });
            }
        }

        Debug.Log($"[ColliderDebugger] ==========================================");
    }

    #endregion

    #region Gizmos

    private void OnDrawGizmos()
    {
        if (Time.time - _snapshotTime > gizmoDisplayDuration) return;

        // 감지 반경
        Gizmos.color = new Color(0f, 1f, 1f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, scanRadius);

        // 콜라이더 시각화
        foreach (var snap in _snapshot)
        {
            if (snap.col == null) continue;
            Gizmos.color = snap.isInvisible ? Color.red : Color.yellow;
            Gizmos.DrawWireCube(snap.worldCenter, snap.col.bounds.size);
            Gizmos.DrawWireSphere(snap.worldCenter, 0.15f);

#if UNITY_EDITOR
            UnityEditor.Handles.Label(snap.worldCenter + Vector3.up * 0.3f,
                snap.col.gameObject.name + (snap.isInvisible ? "\n⚠️INVISIBLE" : ""));
#endif
        }

        // 레이캐스트 시각화
        foreach (var ray in _raySnapshot)
        {
            if (ray.didHit)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(ray.origin, ray.hit.point);
                Gizmos.DrawWireSphere(ray.hit.point, 0.1f);
            }
            else
            {
                Gizmos.color = new Color(0f, 1f, 0f, 0.4f);
                Gizmos.DrawLine(ray.origin, (Vector2)ray.origin + ray.direction * rayLength);
            }
        }
    }

    #endregion

    #region Helpers

    private bool HasRenderer(GameObject obj)
    {
        var sr = obj.GetComponent<SpriteRenderer>();
        if (sr != null && sr.enabled) return true;
        var mr = obj.GetComponent<MeshRenderer>();
        if (mr != null && mr.enabled) return true;
        return false;
    }

    private string GetPath(GameObject obj)
    {
        string path = obj.name;
        Transform t = obj.transform.parent;
        while (t != null) { path = t.name + "/" + path; t = t.parent; }
        return path;
    }

    #endregion
}
