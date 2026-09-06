// @tags: special-chunk, digging, entity, snowman, damage
using UnityEngine;
using System;

/// <summary>
/// IDiggable을 구현한 눈사람 엔티티.
/// </summary>
public class SnowmanEntity : MonoBehaviour, IDiggable
{
    [Header("눈사람 설정")]
    [Tooltip("파괴되기까지의 체력 (타격 횟수)")]
    public float maxHp = 5f;
    
    [Tooltip("파괴 시 드롭할 아이템 프리팹")]
    public GameObject lootItemPrefab;

    public event Action<string> OnDialogue;

    private float _currentHp;
    private bool _dialogue75, _dialogue50, _dialogue25;
    
    // 타격 중복 방지용 쿨타임
    private float _lastHitTime;

    private void Awake()
    {
        if (SpecialChunkSettingsLoader.Instance != null)
            maxHp = SpecialChunkSettingsLoader.Instance.Settings.entities.snowman.maxHp;
        _currentHp = maxHp;
    }

    public void Dig(Vector2 worldPos, float radius, int toolIndex)
    {
        // 쿨타임 설정 (중복 히트 방지, 0.2초)
        if (Time.time - _lastHitTime < 0.2f) return;
        _lastHitTime = Time.time;
        
        _currentHp -= 1f; // 도구 상관없이 1타격당 1 대미지
        CheckDialogue();
        
        if (_currentHp <= 0) Die();
    }

    private void CheckDialogue()
    {
        float ratio = _currentHp / maxHp;
        if (!_dialogue75 && ratio <= 0.75f)
        {
            _dialogue75 = true;
            OnDialogue?.Invoke("눈사람: 아야! 그만 좀 파줄래?");
            Debug.Log("[SnowmanEntity] 대화: 75% HP");
        }
        else if (!_dialogue50 && ratio <= 0.5f)
        {
            _dialogue50 = true;
            OnDialogue?.Invoke("눈사람: 이러다 나 진짜 없어진다고!");
            Debug.Log("[SnowmanEntity] 대화: 50% HP");
        }
        else if (!_dialogue25 && ratio <= 0.25f)
        {
            _dialogue25 = true;
            OnDialogue?.Invoke("눈사람: ...제발.");
            Debug.Log("[SnowmanEntity] 대화: 25% HP");
        }
    }

    private void Die()
    {
        if (lootItemPrefab != null)
            Instantiate(lootItemPrefab, transform.position, Quaternion.identity);
            
        Debug.Log("[SnowmanEntity] 파괴 → 아이템 드롭");
        Destroy(gameObject);
    }
}
