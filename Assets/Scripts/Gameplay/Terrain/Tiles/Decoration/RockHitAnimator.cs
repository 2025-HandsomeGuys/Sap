// @tags: rock, hit, animation, animator, vfx, decoration
using UnityEngine;

/// <summary>
/// 바위가 맞았을 때 Animator 클립(흔들림 등)을 재생한다.
///
/// ─── 왜 단순히 Animator만 붙이면 안 되는가 ───────────────────────────────
/// RockSpawner가 돌의 localPosition·localRotation·localScale을 확정해서 넣는다.
/// 특히 회전은 RockLayoutCalculator가 배치 단계에서 결정한 값이고, 마스크 변환·
/// 노출 판정·세이브까지 이 값에 물려 있다(= 연출용 랜덤이 아니다).
/// Animator는 클립 값을 transform에 **절대값으로** 쓰기 때문에, 클립이 Transform을
/// 건드리는 순간 그 배치값이 매 프레임 지워진다.
///
/// ─── 해법: 매 프레임 base ∘ clip 합성 ────────────────────────────────────
/// Unity 실행 순서는 Update → (Animator 평가) → LateUpdate 다.
///   Update      : transform을 원점(0 / identity / 1)으로 되돌린다.
///   Animator    : 클립이 애니메이트하는 속성만 덮어쓴다. 나머지는 원점으로 남는다.
///   LateUpdate  : 남아 있는 값을 "오프셋"으로 보고 base 위에 합성한다.
///
/// Update에서 원점으로 되돌리는 단계가 핵심이다. 이게 없으면, 클립이 예를 들어
/// localPosition만 애니메이트할 때 rotation/scale은 지난 프레임에 우리가 써 넣은
/// 합성 결과를 그대로 들고 있게 되고, 그걸 다시 오프셋으로 곱해 매 프레임 발산한다.
/// 이 구조 덕분에 클립이 pos/rot/scale 중 무엇을 애니메이트하든 안전하다.
///
/// PlayerSortingController가 애니메이션이 덮어쓰는 sortingOrder를 LateUpdate에서
/// 재적용하는 것과 같은 패턴이다(CLAUDE.md §16).
///
/// ─── 비용 ────────────────────────────────────────────────────────────────
/// 맞고 있지 않은 돌은 Animator와 이 컴포넌트가 **둘 다 비활성**이라 프레임 비용이 0이다.
/// 비활성 MonoBehaviour는 Unity의 메시지 리스트에서 아예 빠지므로, 조기 return과 달리
/// native→managed 디스패치 비용조차 내지 않는다.
/// 활성 Animator는 클립이 단순해도 개당 5~20μs라 로드된 돌 전부에 상시로 켜두면
/// 무시할 수 없다 — 그래서 히트 순간에만 켠다.
/// </summary>
[DisallowMultipleComponent]
public class RockHitAnimator : MonoBehaviour, IRockHitReactor
{
    [Tooltip("재생할 Animator 상태 이름. 비우면 레이어 0의 기본 상태를 0초로 되감아 재생한다.")]
    public string hitStateName = "Hit";

    [Tooltip("흔들림 지속 시간(초). Animator 클립 길이에 맞춘다. " +
             "클립을 실수로 Loop로 만들어도 이 시간에 강제 종료되므로 안전장치 역할도 한다.")]
    [Range(0.02f, 2f)] public float duration = 0.15f;

    [Tooltip("연타 시 흔들림을 처음부터 다시 재생한다. false면 재생 위치는 두고 종료 시각만 연장한다.")]
    public bool restartOnRehit = true;

    private Animator _animator;

    // RockSpawner가 확정한 배치값. 클립은 이 위에 오프셋으로 얹힌다.
    private Vector3    _basePos   = Vector3.zero;
    private Quaternion _baseRot   = Quaternion.identity;
    private Vector3    _baseScale = Vector3.one;

    private float _endTime;

    private void Awake()
    {
        _animator = GetComponent<Animator>();

        // 씬/프리팹에 직접 배치된 돌(RockSpawner를 안 거치는 경로)은 자기 transform이 곧 base다.
        // 스포너 경로는 SpawnRockObject가 SetBase()로 덮어쓴다.
        CaptureBaseFromTransform();

        Stop();
    }

    /// <summary>
    /// RockSpawner가 위치·회전·스케일을 확정한 직후 호출한다.
    /// 풀에서 재사용된 오브젝트에 남아 있던 이전 흔들림도 여기서 정리된다.
    /// </summary>
    public void SetBase(Vector3 localPos, Quaternion localRot, Vector3 localScale)
    {
        _basePos   = localPos;
        _baseRot   = localRot;
        _baseScale = localScale;
        Stop();
    }

    private void CaptureBaseFromTransform()
    {
        _basePos   = transform.localPosition;
        _baseRot   = transform.localRotation;
        _baseScale = transform.localScale;
    }

    // ─── IRockHitReactor ──────────────────────────────────────────────────
    public void OnRockHit(Vector2 worldPos, float damage)
    {
        // 프리팹에 Animator나 컨트롤러가 없으면 조용히 무시 — 모든 돌이 히트 연출을
        // 가질 필요는 없고, 없다고 경고를 쏟으면 로그가 도배된다.
        if (_animator == null || _animator.runtimeAnimatorController == null) return;

        bool wasIdle = !enabled;

        _animator.enabled = true;
        enabled           = true;

        if (restartOnRehit || wasIdle)
        {
            if (string.IsNullOrEmpty(hitStateName))
                _animator.Play(0, 0, 0f);
            else
                _animator.Play(hitStateName, 0, 0f);
        }

        _endTime = Time.time + duration;
    }

    /// <summary>
    /// 파괴 직전 통지. 흔들림을 즉시 끝내고 transform을 배치 확정값으로 되돌린다.
    ///
    /// Update()가 Animator 평가를 위해 transform을 원점으로 비워 둔 프레임에
    /// 치명타가 들어오면, DestroyRock()이 그 원점 상태를 읽어 조각·효과음·광물을
    /// 청크 원점에 쏟아낸다(LateUpdate 합성은 그 뒤에 오므로 늦다).
    /// 어차피 사라질 돌이라 흔들림을 끊는 데 따르는 시각적 손해는 없다.
    /// </summary>
    public void OnRockBreaking() => Stop();

    // ─── base ∘ clip 합성 ─────────────────────────────────────────────────

    /// <summary>
    /// Animator가 클립 값을 쓰기 전에 원점으로 되돌린다.
    /// 클립이 애니메이트하지 않는 속성이 지난 프레임의 합성 결과를 들고 있어
    /// 발산하는 것을 막는 단계다. (클래스 주석 참고)
    /// </summary>
    private void Update()
    {
        Transform t = transform;
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        t.localScale    = Vector3.one;
    }

    private void LateUpdate()
    {
        Transform t = transform;
        Compose(_basePos, _baseRot, _baseScale,
                t.localPosition, t.localRotation, t.localScale,
                out Vector3 pos, out Quaternion rot, out Vector3 scale);

        t.localPosition = pos;
        t.localRotation = rot;
        t.localScale    = scale;

        if (Time.time >= _endTime)
            Stop();
    }

    /// <summary>
    /// 배치값(base)과 클립 오프셋(clip)을 합성한다. 순수 함수 — EditMode 테스트 대상.
    /// clip이 항등(0 / identity / 1)이면 결과는 base와 정확히 같아야 한다.
    /// </summary>
    public static void Compose(
        Vector3 basePos, Quaternion baseRot, Vector3 baseScale,
        Vector3 clipPos, Quaternion clipRot, Vector3 clipScale,
        out Vector3 pos, out Quaternion rot, out Vector3 scale)
    {
        pos   = basePos + clipPos;
        rot   = baseRot * clipRot;
        scale = Vector3.Scale(baseScale, clipScale);
    }

    /// <summary>흔들림을 즉시 끝내고 배치값을 그대로 복원한다. 이후 프레임 비용은 0.</summary>
    private void Stop()
    {
        transform.localPosition = _basePos;
        transform.localRotation = _baseRot;
        transform.localScale    = _baseScale;

        if (_animator != null) _animator.enabled = false;
        enabled  = false;
        _endTime = 0f;
    }
}
