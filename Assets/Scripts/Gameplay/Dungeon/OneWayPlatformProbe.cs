// @tags: dungeon, oneway, platform, debug, diagnostic, effector
using UnityEngine;

/// <summary>
/// 원웨이 발판(=)이 아래에서 막는 원인을 좁히기 위한 진단용. 원인 확정되면 제거할 것.
///
/// 붙이는 곳: 씬에 깔린 OneWayPlatform 인스턴스 하나(또는 프리팹에 붙여 전체).
/// 인스턴스에 붙여야 '프리팹은 고쳐졌는데 씬 것은 옛날 값' 같은 경우까지 잡힌다.
///
/// 읽는 법:
///  - Start 로그의 usedByEffector / useOneWay 가 False 면 설정이 런타임에 안 먹은 것.
///  - worldSize.x 가 한 칸 크기보다 작으면 발판 사이에 틈이 있다는 뜻(모서리 걸림 원인).
///  - 아래에서 올라오는데 [충돌] 로그가 찍히면 이펙터가 그 접촉을 못 걸러낸 것이다.
///    이때 normal.y 를 보라. 0 근처(수평)면 모서리에 걸린 것 → surfaceArc 를 낮추거나
///    발판 폭을 한 칸에 맞춰 이음매를 없애야 한다. -1 근처면 밑면 정면 충돌이라
///    이펙터가 통째로 동작하지 않는 것이다.
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class OneWayPlatformProbe : MonoBehaviour
{
    [Tooltip("던전 한 칸의 월드 크기. 발판 폭이 한 칸을 채우는지 비교용 (Map Root 스케일 반영값).")]
    public float cellWorldSize = 0.25f;

    private void Start()
    {
        var box = GetComponent<BoxCollider2D>();
        var eff = GetComponent<PlatformEffector2D>();

        string colliderInfo = box == null
            ? "없음"
            : $"size={box.size} offset={box.offset} usedByEffector={box.usedByEffector} " +
              $"isTrigger={box.isTrigger} enabled={box.enabled} worldSize={box.bounds.size}";

        string effectorInfo = eff == null
            ? "없음 ← 이게 원인"
            : $"enabled={eff.enabled} useOneWay={eff.useOneWay} surfaceArc={eff.surfaceArc} " +
              $"rotationalOffset={eff.rotationalOffset} useColliderMask={eff.useColliderMask} " +
              $"mask={eff.colliderMask}";

        float widthInCells = (box != null && cellWorldSize > 0f) ? box.bounds.size.x / cellWorldSize : -1f;

        Debug.Log(
            $"[OneWayProbe] {name} pos={transform.position} lossyScale={transform.lossyScale}\n" +
            $"  layer={LayerMask.LayerToName(gameObject.layer)}({gameObject.layer})\n" +
            $"  collider: {colliderInfo}\n" +
            $"  effector: {effectorInfo}\n" +
            $"  폭 = {widthInCells:F2}칸  (1.00 이 아니면 발판 사이에 틈 → 모서리 걸림 위험)", this);
    }

    // 이펙터가 제대로 걸러내면 아래에서 올라올 때 이 콜백 자체가 안 불린다.
    // 불린다는 것 = 그 접촉이 살아남았다는 뜻이므로, 법선 방향이 원인을 알려준다.
    private void OnCollisionEnter2D(Collision2D collision)
    {
        bool otherIsAbove = collision.transform.position.y > transform.position.y;

        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint2D contact = collision.GetContact(i);

            // normal 은 '들어온 쪽(플레이어) → 나(발판)' 방향이다.
            // 따라서 y 가 음수면 플레이어가 위에 있다는 뜻(= 정상 착지),
            // 양수면 아래/옆에서 들어온 접촉인데도 살아남은 것이므로 이펙터가 못 걸러낸 것이다.
            string verdict = contact.normal.y < -0.5f ? "윗면 착지(정상)"
                           : contact.normal.y > 0.5f ? "아래에서 정면 충돌 ← 이펙터 미동작"
                           : "수평 근처 = 옆/모서리 걸림 ← surfaceArc 가 너무 넓거나 발판 폭이 좁음";

            Debug.Log(
                $"[OneWayProbe] 충돌 유지됨 — {verdict}\n" +
                $"  상대={collision.otherCollider.name}({LayerMask.LayerToName(collision.otherCollider.gameObject.layer)}) " +
                $"상대가 위에 있음={otherIsAbove}\n" +
                $"  normal={contact.normal} point={contact.point} relativeVelocity={collision.relativeVelocity}", this);
        }
    }
}
