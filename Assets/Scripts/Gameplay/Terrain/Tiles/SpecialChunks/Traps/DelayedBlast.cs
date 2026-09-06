// @tags: trap, special-chunk, explosion, mineral, damage, loot
using UnityEngine;
using System.Collections;

/// <summary>
/// Special Chunk: Methane Gas Ore
/// 광물 프리팹(CondensedGas)에 부착하는 지연 폭발 컴포넌트.
/// PickupableItem.Interact()에서 인벤토리 추가 성공 시 Activate()를 호출한다.
/// Activate() 후 blastDelay 초 뒤에 주변 드롭 아이템을 파괴하는 폭발이 일어난다.
/// </summary>
public class DelayedBlast : MonoBehaviour
{
    [Header("폭발 설정")]
    [Tooltip("픽업 후 폭발까지의 지연 시간 (초)")]
    public float blastDelay = 2f;

    [Tooltip("폭발로 지형이 파괴될 반경 (유닛)")]
    public float blastRadius = 0.3f;

    [Tooltip("폭발로 플레이어나 아이템이 영향을 받는 물리적 타격 반경 (유닛)")]
    public float damageRadius = 1.5f;

    [Tooltip("폭발로 파괴할 대상(아이템, 드롭물 등)의 레이어")]
    public LayerMask destroyItemLayer;
    
    [Tooltip("폭발로 스태미나 피해를 입을 대상(플레이어 등)의 레이어")]
    public LayerMask damageTargetLayer;

    [Tooltip("폭발 시 플레이어가 입을 스태미나 피해량")]
    public float staminaDamage = 20f;

    [Header("VFX")]
    [Tooltip("상태 전환 시 재생할 가스 파티클 프리팹 (옵션)")]
    public GameObject gasParticlePrefab;

    [Tooltip("폭발 순간 재생할 이펙트 프리팹 (옵션)")]
    public GameObject explosionVFXPrefab;

    private bool _isActivated = false;

    private void Awake()
    {
        // JSON 설정 적용
        if (SpecialChunkSettingsLoader.Instance != null)
        {
            var s = SpecialChunkSettingsLoader.Instance.Settings.traps.delayedBlast;
            blastDelay    = s.blastDelay;
            blastRadius   = s.blastRadius;
            damageRadius  = s.damageRadius;
            staminaDamage = s.staminaDamage;
        }
    }

    /// <summary>
    /// 외부(예: MineralItemController)에서 아이템이 물리적으로 떨어질 때 호출
    /// </summary>
    public void Activate()
    {
        if (_isActivated) return;
        _isActivated = true;
        StartCoroutine(BlastRoutine());
    }

    private IEnumerator BlastRoutine()
    {
        // 1. 활성화 시 가스 이펙트 생성
        if (gasParticlePrefab != null)
        {
            Instantiate(gasParticlePrefab, transform.position, Quaternion.identity, transform);
        }

        // 2. 대기 시간
        yield return new WaitForSeconds(blastDelay);

        Vector2 blastPos = transform.position;

        // 3. 폭발 이펙트 생성 (부모 없이 독립적으로 재생 후 자동 파괴되도록 구성 권장)
        if (explosionVFXPrefab != null)
        {
            Instantiate(explosionVFXPrefab, blastPos, Quaternion.identity);
        }

        // 4. 지형 파괴 연동 (동적으로 ITerrainManager 찾기)
        ITerrainManager mapManager = Object.FindFirstObjectByType<StaticChunkTerrainManager>() as ITerrainManager 
                                  ?? Object.FindFirstObjectByType<InfinityMapManager>() as ITerrainManager;
        
        if (mapManager != null)
        {
            // 도구 인덱스나 각도에 영향 받지 않는 순수 원형 폭발 파괴 연동
            mapManager.ExplodeTerrain(blastPos, blastRadius);
            Debug.Log($"[DelayedBlast] 지형 파괴: 위치 {blastPos}, 반경 {blastRadius}");
        }

        // 5. 주변 파괴 및 대미지 처리 (SRP 원칙에 따라 분리)
        ProcessExplosionDamageAndDestruction(blastPos);

        // 6. 폭발 후 자기 자신 파괴
        Destroy(gameObject);
    }

    private void ProcessExplosionDamageAndDestruction(Vector2 blastPos)
    {
        // ① 아이템(드롭물 등) 파괴 처리
        Collider2D[] items = Physics2D.OverlapCircleAll(blastPos, damageRadius, destroyItemLayer);
        foreach (var hit in items)
        {
            if (hit == null || hit.gameObject == gameObject) continue;

            // 추가 안전장치: 레이어나 태그에 실수로 맵(Terrain/Chunk)이 포함된 경우 거르기
            int layer = hit.gameObject.layer;
            string layerName = LayerMask.LayerToName(layer);
            if (layerName.Contains("Terrain") || layerName.Contains("Chunk") || layerName.Contains("Ground"))
                continue;

            // 확실하게 드롭 아이템(PickupableItem)인 경우만 파괴하여 청크 증발 원천 차단
            if (hit.GetComponent<PickupableItem>() != null)
            {
                Destroy(hit.gameObject);
                Debug.Log($"[DelayedBlast] 파괴됨: {hit.name}");
            }
        }

        // ② 플레이어(생명체 등) 스태미나 대미지 처리
        Collider2D[] targets = Physics2D.OverlapCircleAll(blastPos, damageRadius, damageTargetLayer);
        foreach (var hit in targets)
        {
            ApplyExplosionDamage(hit);
        }
    }

    private void ApplyExplosionDamage(Collider2D hit)
    {
        // DIP: IHazardTarget 인터페이스로 PlayerStat 구체 타입 의존 제거
        IHazardTarget target = hit.GetComponentInParent<IHazardTarget>();
        if (target == null) target = hit.GetComponentInChildren<IHazardTarget>();

        if (target != null)
        {
            target.ApplyHazardDamage(staminaDamage);
            Debug.Log($"[DelayedBlast] 피해 적용 -{staminaDamage}");
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
        Gizmos.DrawSphere(transform.position, blastRadius);
        Gizmos.color = new Color(1f, 0f, 0f, 0.2f);
        Gizmos.DrawSphere(transform.position, damageRadius);
    }
}
