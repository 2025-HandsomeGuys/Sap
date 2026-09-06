// @tags: dungeon, rock, save, persistence, diggable
using UnityEngine;

/// <summary>
/// 던전 안에 직접 배치된 DiggableRock의 채굴 상태를 인스턴스별로 영속화한다.
/// DiggableRock을 수정하지 않고 같은 GameObject에 함께 부착한다.
///  - Start: 이 인스턴스에서 이미 채굴된 rock이면 자기 자신을 파괴(재입장 시 숨김).
///  - OnDestroy: 형제 DiggableRock의 HP가 0 이하(=채굴로 파괴됨)일 때만 부서짐으로 기록.
///    (씬 언로드/앱 종료로 파괴될 때는 HP>0 → 기록하지 않음)
/// rockId는 한 던전 프리팹 안에서 유일해야 한다(수동 부여).
/// </summary>
[RequireComponent(typeof(DiggableRock))]
public class DungeonRock : MonoBehaviour
{
    [Tooltip("이 던전 프리팹 안에서 유일한 rock 식별자")]
    [SerializeField] private int rockId;

    private DiggableRock _rock;

    private void Awake() => _rock = GetComponent<DiggableRock>();

    private void Start()
    {
        if (DungeonStateStore.IsRockBroken(DungeonStateStore.CurrentInstance, rockId))
            Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (_rock != null && _rock.CurrentHp <= 0f)
            DungeonStateStore.MarkRockBroken(rockId);
    }
}
