// @tags: trap, special-chunk, collapse, digging, pixel, player, vfx
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 싱크홀 기믹 오케스트레이터: 플레이어가 올라서면 collapseDelay 초 후 바닥 픽셀이 소멸하는 함정.
/// TerrainChunk 기반 특수 청크(SinkholeChunk 프리팹)에 부착하여 사용.
/// isStaticSpecialChunk = true 인 청크에만 동작.
///
/// [SOLID 리팩토링]
///   SRP: 픽셀 수정 → PixelFloorCollapser, 오디오 → AudioCollapseEffect, VFX → VFXCollapseEffect 로 분리.
///   OCP: ICollapseEffect 목록에 컴포넌트를 추가하는 것만으로 새 효과를 확장 가능.
///   DIP: 구체 클래스(TerrainChunk, AudioSource 등) 대신 IFloorCollapser / ICollapseEffect 인터페이스에 의존.
///
/// BoxCollider2D(isTrigger=true)는 청크 전체 영역을 커버한다.
///   - OnTriggerEnter2D: 플레이어가 바닥 상단에서 진입 시 붕괴 시작.
/// </summary>
[RequireComponent(typeof(TerrainChunk))]
public class CollapseFloor : MonoBehaviour
{
    // ============================================================
    //  설정
    // ============================================================
    [Header("Collapse Settings")]
    [Tooltip("플레이어 감지 후 붕괴까지 걸리는 시간(초)")]
    public float collapseDelay = 0.2f;

    [Tooltip("청크 상단에서 소멸시킬 바닥 두께(픽셀 단위)")]
    public int floorThicknessPx = 200;

    [Header("Effects")]
    [Tooltip("붕괴 효과 컴포넌트 목록 (ICollapseEffect를 구현한 MonoBehaviour). " +
             "AudioCollapseEffect, VFXCollapseEffect 등을 여기에 등록.")]
    public List<MonoBehaviour> effectComponents = new List<MonoBehaviour>();

    // ============================================================
    //  런타임 상태
    // ============================================================
    private IFloorCollapser   _collapser;
    private ICollapseEffect[] _effects;
    private bool _isTriggered  = false;
    private bool _hasCollapsed = false;

    // ============================================================
    //  유니티 생명주기
    // ============================================================
    void Awake()
    {
        // JSON 설정 적용
        if (SpecialChunkSettingsLoader.Instance != null)
        {
            var s = SpecialChunkSettingsLoader.Instance.Settings.traps.collapseFloor;
            collapseDelay    = s.collapseDelay;
            floorThicknessPx = s.floorThicknessPx;
        }

        _collapser = GetComponent<IFloorCollapser>();
        if (_collapser == null)
            Debug.LogError("[CollapseFloor] IFloorCollapser 컴포넌트(PixelFloorCollapser 등)가 없습니다!");

        _effects = effectComponents
            .Where(m => m != null)
            .OfType<ICollapseEffect>()
            .ToArray();
    }

    /// <summary>
    /// Player 태그 오브젝트가 트리거 영역에 진입하면 붕괴 시퀀스 시작.
    /// 플레이어가 바닥 상단 절반에서 진입했을 때만 발동 (하단 진입 무시).
    /// BoxCollider2D (isTrigger=true, 청크 전체 영역)가 이 오브젝트에 있어야 함.
    /// </summary>
    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        if (_isTriggered || _hasCollapsed) return;

        // 플레이어 Y 위치가 청크 중심보다 위에 있을 때만 발동 (바닥 상단 진입)
        if (other.transform.position.y < transform.position.y) return;

        _isTriggered = true;
        StartCoroutine(CollapseRoutine());
    }

    // ============================================================
    //  붕괴 시퀀스
    // ============================================================
    IEnumerator CollapseRoutine()
    {
        foreach (var effect in _effects)
            effect.PlayWarning();

        yield return new WaitForSeconds(collapseDelay);

        _collapser?.CollapseFloor(floorThicknessPx);
        _hasCollapsed = true;

        foreach (var effect in _effects)
            effect.PlayCollapse();
    }
}
