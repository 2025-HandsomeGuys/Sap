// @tags: rock, vfx, fragment, decoration
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RockFragment : MonoBehaviour
{
    private const float GhostDuration = 0.12f;

    private SpriteRenderer _sr;

    public void Init(
        Sprite  sprite,
        Vector2 velocity,
        int     finalLayer,
        int     sortingLayerID,
        int     sortingOrder,
        float   lifetime,
        float   fadeDuration,
        float   angularDamping)
    {
        if (_sr != null) return;

        // Ghost 레이어에서 시작 — 지형과 충돌하지 않음
        int ghostLayer = LayerMask.NameToLayer("RockFragmentGhost");
        gameObject.layer = ghostLayer >= 0 ? ghostLayer : finalLayer;

        _sr = gameObject.AddComponent<SpriteRenderer>();
        _sr.sprite         = sprite;
        _sr.sortingLayerID = sortingLayerID;
        _sr.sortingOrder   = sortingOrder;

        var rb = gameObject.AddComponent<Rigidbody2D>();
        rb.gravityScale           = 1f;
        rb.linearDamping          = 0.3f;
        rb.angularDamping         = angularDamping;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.linearVelocity         = velocity;
        rb.angularVelocity        = Random.Range(-360f, 360f);

        var col = gameObject.AddComponent<PolygonCollider2D>();
        int pathCount = sprite.GetPhysicsShapeCount();
        if (pathCount > 0)
        {
            col.pathCount = pathCount;
            var pts = new List<Vector2>();
            for (int i = 0; i < pathCount; i++)
            {
                sprite.GetPhysicsShape(i, pts);
                col.SetPath(i, pts);
            }
        }
        else
        {
            Debug.LogWarning($"[RockFragment] '{sprite.name}'에 Physics Shape이 없습니다. Sprite Editor에서 Generate하세요.");
        }

        StartCoroutine(SwitchLayerAfterDelay(finalLayer, GhostDuration));
        StartCoroutine(LifetimeRoutine(lifetime, fadeDuration));
    }

    // spawn 지점 이탈 후 실제 레이어로 전환 → 지형 충돌 활성화
    private IEnumerator SwitchLayerAfterDelay(int finalLayer, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (this != null) gameObject.layer = finalLayer;
    }

    private IEnumerator LifetimeRoutine(float lifetime, float fadeDuration)
    {
        if (fadeDuration <= 0f)
        {
            yield return new WaitForSeconds(lifetime);
            Destroy(gameObject);
            yield break;
        }

        yield return new WaitForSeconds(Mathf.Max(0f, lifetime - fadeDuration));

        float elapsed = 0f;
        Color c = _sr != null ? _sr.color : Color.white;
        while (elapsed < fadeDuration)
        {
            if (_sr == null) yield break;
            elapsed += Time.deltaTime;
            c.a = 1f - Mathf.Clamp01(elapsed / fadeDuration);
            _sr.color = c;
            yield return null;
        }

        Destroy(gameObject);
    }
}
