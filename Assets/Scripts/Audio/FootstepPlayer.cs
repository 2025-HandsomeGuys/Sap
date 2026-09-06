using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 플레이어 발소리.
/// </summary>
public class FootstepPlayer : MonoBehaviour
{
    [Header("타이머 폴백 판정")]
    [Tooltip("이 속도 미만이면 걷는 것으로 보지 않는다. (루프 사운드 판정에도 사용됨)")]
    [SerializeField] private float minSpeed = 0.6f;

    [Header("음량")]
    [Tooltip("발소리는 계속 반복돼서 원본 크기 그대로면 쉽게 시끄러워진다.")]
    [SerializeField, Range(0f, 1f)] private float volume = 0.45f;

    [Tooltip("재생 시 음정을 흔드는 폭. 0이면 지터 없음 — 같은 클립 반복의 기계적인 느낌을 줄인다.")]
    [SerializeField, Range(0f, 0.2f)] private float pitchJitter = 0.06f;

    [Header("지상 이동 루프음 (연속 사운드)")]
    [Tooltip("지상 씬에서 이동할 때 끊김없이 계속 재생될 사운드 (예: 풀숲 헤치는 소리)")]
    [SerializeField] private AudioClip surfaceMoveLoopClip;
    [SerializeField, Range(0f, 1f)] private float surfaceMoveLoopVolume = 0.3f;

    // 👇👇👇 추가된 루프 제어 변수들 👇👇👇
    [Tooltip("루프음이 시작되기 전 요구되는 연속 이동 시간(초). 짧게 탭할 때 소리가 나는 것을 방지합니다.")]
    [SerializeField] private float loopStartDelay = 0.1f;
    [Tooltip("루프음이 멈추기 전 주어지는 유예 시간(초). 좌우 방향 전환 시 속도가 0이 되어도 소리가 끊기지 않게 합니다.")]
    [SerializeField] private float loopStopDelay = 0.15f;

    private float _loopStartTimer = 0f;
    private float _loopStopTimer = 0f;
    // 👆👆👆 ======================== 👆👆👆

    [Header("특수 구역 (집 안) 콜라이더")]
    [Tooltip("집 구역을 덮고 있는 콜라이더(BoxCollider2D 등, IsTrigger 체크)를 씬에서 끌어다 넣으세요.")]
    [SerializeField] private Collider2D homeAreaCollider;

    [Header("표면 판정")]
    [Tooltip("SurfaceSceneRegistry 목록에 더해 지상(풀밭)으로 볼 씬. 보통 비워둔다.")]
    [SerializeField] private string[] surfaceScenes;

    [Header("디버그")]
    [Tooltip("스텝마다 '지상/지하 · 층 · 선택된 키'를 콘솔에 찍는다. 어느 발소리가 나는지 확인할 때만 켠다.")]
    [SerializeField] private bool debugLog = false;

    private PlayerController _controller;
    private Rigidbody2D _rb;
    private float _timer;

    private AudioSource _loopAudioSource;

    private bool _drivenByAnimationEvent;
    private bool _rightFoot;

    private const float ChunkWorldSize = 10f;

    private void Awake()
    {
        _controller = GetComponentInParent<PlayerController>();
        _rb = GetComponentInParent<Rigidbody2D>();

        if (_controller == null || _rb == null)
        {
            Debug.LogError($"[FootstepPlayer] {name}: PlayerController/Rigidbody2D를 찾지 못함.", this);
            enabled = false;
            return;
        }

        if (surfaceMoveLoopClip != null)
        {
            _loopAudioSource = gameObject.AddComponent<AudioSource>();
            _loopAudioSource.clip = surfaceMoveLoopClip;
            _loopAudioSource.loop = true;
            _loopAudioSource.volume = surfaceMoveLoopVolume;
            _loopAudioSource.playOnAwake = false;

            // AddComponent로 만든 소스는 믹서 그룹이 비어 있다 →
            // 설정의 효과음 슬라이더를 무시한 채 이동 루프음만 계속 들린다.
            AudioRouting.Route(_loopAudioSource, AudioChannel.SFX);
        }
    }

    private string GetCurrentAreaOverride()
    {
        if (homeAreaCollider != null && homeAreaCollider.OverlapPoint(transform.position))
        {
            return "UpgroundScene_home";
        }
        return "";
    }

    public void PlayFootstep()
    {
        _drivenByAnimationEvent = true;
        if (_controller == null || !_controller.IsGrounded) return;
        PlayStep();
    }

    public void PlayJump()
    {
        bool surface = IsSurfaceScene();
        TileType layer = surface ? TileType.Empty : CurrentLayer();
        PlayOneShotForSurface(FootstepSurfaceSelector.SelectJump(surface, layer, GetCurrentAreaOverride()));
    }

    public void PlayLand()
    {
        bool surface = IsSurfaceScene();
        TileType layer = surface ? TileType.Empty : CurrentLayer();
        PlayOneShotForSurface(FootstepSurfaceSelector.SelectLand(surface, layer, GetCurrentAreaOverride()));
    }

    private void PlayOneShotForSurface(string key)
    {
        var sm = SoundManager.Instance;
        if (sm == null || string.IsNullOrEmpty(key)) return;
        sm.PlaySFX(key, 1f, volume);
    }

    private void Update()
    {
        if (_controller == null || _rb == null) return;

        UpdateSurfaceMoveLoop();

        if (_drivenByAnimationEvent) return;

        if (!_controller.IsGrounded)
        {
            _timer = 0f;
            return;
        }

        float speed = Mathf.Abs(_rb.linearVelocity.x);
        if (speed < minSpeed)
        {
            _timer = 0f;
            return;
        }

        _timer -= Time.deltaTime;
        if (_timer > 0f) return;

        _timer = FootstepTiming.IntervalFor(speed);
        PlayStep();
    }

    // 👇👇👇 수정된 루프음 재생 로직 👇👇👇
    private void UpdateSurfaceMoveLoop()
    {
        if (_loopAudioSource == null || surfaceMoveLoopClip == null) return;

        bool isMoving = _controller.IsGrounded &&
                        Mathf.Abs(_rb.linearVelocity.x) >= minSpeed;

        string currentArea = GetCurrentAreaOverride();

        // 이동 조건 충족 여부
        bool meetsLoopCondition = isMoving && IsSurfaceScene() && string.IsNullOrEmpty(currentArea);

        if (meetsLoopCondition)
        {
            // 이동 중이라면 정지 타이머 초기화, 시작 타이머 증가
            _loopStopTimer = 0f;
            _loopStartTimer += Time.deltaTime;

            // 설정한 지연 시간을 넘겼고, 아직 재생 중이 아니라면 재생
            if (_loopStartTimer >= loopStartDelay && !_loopAudioSource.isPlaying)
            {
                _loopAudioSource.Play();
            }
        }
        else
        {
            // 멈췄다면 시작 타이머 초기화, 정지 타이머 증가
            _loopStartTimer = 0f;
            _loopStopTimer += Time.deltaTime;

            // 설정한 유예 시간을 넘겼고, 재생 중이라면 정지
            if (_loopStopTimer >= loopStopDelay && _loopAudioSource.isPlaying)
            {
                _loopAudioSource.Stop();
            }
        }
    }
    // 👆👆👆 ======================== 👆👆👆

    private void PlayStep()
    {
        var sm = SoundManager.Instance;
        if (sm == null) return;

        bool surface = IsSurfaceScene();
        TileType layer = surface ? TileType.Empty : CurrentLayer();
        string currentArea = GetCurrentAreaOverride();

        FootstepPair pair = FootstepSurfaceSelector.Select(surface, layer, currentArea);

        _rightFoot = !_rightFoot;
        string key = _rightFoot ? pair.B : pair.A;
        bool fellBack = !sm.HasSFX(key);
        if (fellBack) key = pair.A;

        if (debugLog)
        {
            string locInfo = !string.IsNullOrEmpty(currentArea) ? $"오버라이드: {currentArea}" : (surface ? "지상" : $"지하/{layer}");
            Debug.Log($"[Footstep] {locInfo} · " +
                      $"{(_rightFoot ? "오른발" : "왼발")} → {key}" +
                      (fellBack ? "  (B 미등록 → A 폴백)" : "") +
                      $"  [씬: {SceneManager.GetActiveScene().name}]");
        }

        float pitch = pitchJitter <= 0f ? 1f : Random.Range(1f - pitchJitter, 1f + pitchJitter);
        sm.PlaySFXExclusive("footstep", key, pitch, volume);
    }

    private TileType CurrentLayer()
    {
        if (_controller == null || TileDataManager.Instance == null) return TileType.Empty;

        int chunkY = Mathf.FloorToInt(_controller.transform.position.y / ChunkWorldSize);
        return TileDataManager.Instance.GetTileTypeAtDepth(chunkY);
    }

    private bool IsSurfaceScene() => SurfaceSceneRegistry.IsActiveSceneSurface(surfaceScenes);
}