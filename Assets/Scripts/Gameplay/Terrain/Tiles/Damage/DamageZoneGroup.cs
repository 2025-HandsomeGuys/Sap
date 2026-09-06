// @tags: damage, zone, special-chunk, chunk, container
using UnityEngine;

/// <summary>
/// 데미지 존 그룹 - 여러 DamageZone을 묶어서 관리하는 컨테이너 (선택 사항)
/// TerrainChunk 하위에 배치하여 계층 구조를 정리할 때 사용합니다.
/// </summary>
public class DamageZoneGroup : MonoBehaviour
{
#if UNITY_EDITOR
    [Header("Editor Validation")]
    [Tooltip("자식 오브젝트들의 Collider 설정을 자동으로 검증합니다.")]
    public bool validateChildrenOnStart = true;

    void Start()
    {
        if (validateChildrenOnStart)
        {
            ValidateChildren();
        }
    }

    /// <summary>
    /// 에디터 전용: 자식 오브젝트들의 Collider가 Trigger로 설정되어 있는지 검증
    /// </summary>
    [ContextMenu("Validate All Children Colliders")]
    void ValidateChildren()
    {
        DamageZone[] zones = GetComponentsInChildren<DamageZone>(true);
        
        int validCount = 0;
        int invalidCount = 0;

        foreach (DamageZone zone in zones)
        {
            Collider2D col = zone.GetComponent<Collider2D>();
            
            if (col == null)
            {
                Debug.LogError($"[DamageZoneGroup] '{zone.gameObject.name}'에 Collider2D가 없습니다!", zone);
                invalidCount++;
            }
            else if (!col.isTrigger)
            {
                Debug.LogWarning($"[DamageZoneGroup] '{zone.gameObject.name}'의 Collider2D가 Trigger로 설정되지 않았습니다!", zone);
                invalidCount++;
            }
            else
            {
                validCount++;
            }
        }

        if (invalidCount == 0)
        {
            Debug.Log($"[DamageZoneGroup] 검증 완료! 모든 DamageZone ({validCount}개)이 올바르게 설정되었습니다.");
        }
        else
        {
            Debug.LogWarning($"[DamageZoneGroup] 검증 완료. 유효: {validCount}개, 문제: {invalidCount}개");
        }
    }

    void OnDrawGizmos()
    {
        // 계층 구조 시각화 (에디터)
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(transform.position, Vector3.one * 0.5f);
    }
#endif
}
