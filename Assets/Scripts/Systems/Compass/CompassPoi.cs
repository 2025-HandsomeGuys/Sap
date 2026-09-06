// @tags: compass, poi, target-marker, prefab, special-chunk
using UnityEngine;

/// <summary>
/// 특수청크 프리팹 안에 미리 배치해, 나침반이 가리킬 정확한 지점을 지정하는 마커.
/// 빈 자식 오브젝트에 이 컴포넌트를 붙이고 원하는 위치에 두면,
/// 그 특수청크가 나침반의 현재 타겟일 때 청크 중심 대신 이 오브젝트의 위치를 가리킨다.
///
/// 등록/해제는 SpecialChunkManager가 앵커 스폰/언로드 시점에 처리한다(스스로 등록하지 않음).
/// 앵커 하위(GetComponentInChildren)에서 첫 번째 마커만 사용된다.
///
/// 주의(CLAUDE.md #8): 프리팹 자식 배치는 반드시 Prefab Edit 모드에서 한다.
/// 씬 인스턴스에 드래그 후 Apply하면 local position이 틀어진다.
/// </summary>
public class CompassPoi : MonoBehaviour
{
    // 마커 역할만 하는 태그 컴포넌트. 로직은 SpecialChunkManager/ActiveAnchorLocator가 담당.
}
