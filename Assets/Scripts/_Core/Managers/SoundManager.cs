// @tags: sound, audio, manager, singleton, bgm, sfx, pool, ambience, loop
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class SoundManager : MonoBehaviour
{
    #region Singleton
    public static SoundManager Instance { get; private set; }

    /// <summary>
    /// 씬에 배치돼 있지 않아도 게임 시작 시 자동 생성한다.
    ///
    /// 이전에는 MarketScene에만 배치돼 있어서, 마켓에 들어가기 전까지 지상·지하 어디서도
    /// Instance가 null이었다 → 모든 재생 호출이 조용히 무시됐다. 씬마다 수동 배치하는
    /// 대신 TelemetryRunner와 같은 부트스트랩 패턴을 쓴다.
    ///
    /// 참조(soundData/mixer/groups)는 Awake에서 Resources로 자동 해석하므로
    /// 인스펙터 연결이 필요 없다. 씬에 수동 배치된 인스턴스가 있으면 그쪽이 이긴다.
    ///
    /// ⚠ 자가복구: GameManager.OpenMainMenu의 DestroyPersistentObjects()가 이 DDOL 싱글톤을
    /// 파괴하면, [RuntimeInitializeOnLoadMethod]는 세션당 한 번만 돌아 재생성되지 않는다
    /// → 메인메뉴를 다녀오면 BGM·SFX·앰비언스가 영구 무음이 되는 버그가 있었다.
    /// static 이벤트 구독은 씬/DDOL 파괴와 무관하게 살아남으므로, 씬 로드마다 인스턴스를 되살린다.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        EnsureExists();
        SceneManager.sceneLoaded -= OnSceneLoadedEnsure;
        SceneManager.sceneLoaded += OnSceneLoadedEnsure;
    }

    private static void OnSceneLoadedEnsure(Scene scene, LoadSceneMode mode)
    {
        EnsureExists();

        // 새 씬에 손으로 놓인 AudioSource는 믹서 그룹이 비어 있는 경우가 많다
        // → 설정의 볼륨 슬라이더를 통째로 무시한다. 씬 로드 직후 쓸어담아 연결한다.
        if (Instance != null) Instance.RouteUnassignedSceneSources();
    }

    /// <summary>씬에 배치돼 있지 않아도 인스턴스를 보장한다. 라우팅 헬퍼에서도 부른다.</summary>
    public static void EnsureExists()
    {
        if (Instance != null) return; // 씬 배치 인스턴스가 있으면 그쪽 Awake가 먼저 Instance를 잡는다

        var go = new GameObject("[SoundManager]");
        go.AddComponent<SoundManager>(); // Awake가 나머지를 처리한다
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        transform.SetParent(null); // 최상위 부모로 이동
        DontDestroyOnLoad(gameObject);
        ResolveMissingReferences();
        InitializeAudioSources();
    }

    /// <summary>
    /// 인스펙터에서 비어 있는 참조를 Resources에서 채운다.
    /// 코드로 생성된 인스턴스(Bootstrap)와 씬 배치 인스턴스 양쪽에서 동작한다.
    /// 인스펙터에 이미 값이 있으면 그것을 존중한다.
    /// </summary>
    private void ResolveMissingReferences()
    {
        if (soundData == null) soundData = Resources.Load<SoundDataSO>("SoundData");
        if (mainMixer == null) mainMixer = Resources.Load<AudioMixer>("Master");

        if (mainMixer == null)
        {
            Debug.LogError("SoundManager: Master 믹서를 찾지 못했다. " +
                           "Assets/Sound/Master.mixer를 Assets/Resources/로 옮길 것.");
            return;
        }

        if (bgmGroup == null)      bgmGroup      = FindGroup("BGM");
        if (sfxGroup == null)      sfxGroup      = FindGroup("SFX");
        if (ambienceGroup == null) ambienceGroup = FindGroup("Ambience");
    }

    /// <summary>
    /// FindMatchingGroups는 경로 부분일치라 "BGM"이 자식 "BGM/Ambience"까지 물고 온다.
    /// 이름이 정확히 일치하는 것을 우선 고른다.
    /// </summary>
    private AudioMixerGroup FindGroup(string groupName)
    {
        var groups = mainMixer.FindMatchingGroups(groupName);
        if (groups == null || groups.Length == 0) return null;

        foreach (var g in groups)
            if (g != null && g.name == groupName) return g;

        return groups[0];
    }
    #endregion

    [Header("Data")]
    public SoundDataSO soundData;

    [Header("Mixer Settings")]
    public AudioMixer mainMixer;
    public AudioMixerGroup bgmGroup;
    public AudioMixerGroup sfxGroup;
    public AudioMixerGroup ambienceGroup;   // Master.mixer > BGM > Ambience

    private AudioSource bgmSource;

    // ── SFX 원샷 풀 ──
    // 소스가 1개면 pitch가 공유 속성이라 마지막 호출이 아직 울리는 이전 소리의 음정까지
    // 바꾼다. 라운드로빈 풀로 소리마다 독립적인 pitch/위치를 준다.
    private const int SfxPoolSize = 10;
    private AudioSource[] _sfxPool;
    private int _sfxCursor;

    private readonly SfxThrottle _throttle = new SfxThrottle(0.04f);

    /// <summary>0이면 지터 없음. 발소리·곡괭이처럼 초당 여러 번 반복되는 소리에만 쓴다.</summary>
    public float PitchJitter { get; set; } = 0.02f;

    [Header("Debug")]
    [Tooltip("재생되는 키를 콘솔에 찍는다. 어떤 소리가 나는지 확인할 때만 켠다(연타 시 로그가 많다).")]
    public bool logPlays = false;

    [Tooltip("키가 등록돼 있지 않아 무음으로 넘어간 호출도 찍는다. '왜 소리가 안 나지' 추적용.")]
    public bool logSilentMisses = false;

    // ── 앰비언스 ──
    // 낮 지상은 매미와 새가 동시에 깔려야 한다 → 레이어 2개.
    // 각 레이어는 크로스페이드용 소스 2개를 가진다(총 4 AudioSource).
    public enum AmbienceLayer { Primary = 0, Secondary = 1 }

    private sealed class AmbienceChannel
    {
        public AudioSource A;
        public AudioSource B;
        public bool UsingA;            // 현재 들리는 쪽
        public string CurrentKey;
        public Coroutine Fade;

        // 크로스페이드가 계산하는 '논리 볼륨'(0~1). 실제 AudioSource.volume은
        // 여기에 기준 음량(ambienceBaseVolume)과 실내 덕킹(_ambienceScale)을 곱한 값이다.
        // 세 값을 분리해야 크로스페이드가 덕킹을 덮어쓰지 않는다.
        public float VolA;
        public float VolB;

        public AudioSource Active   => UsingA ? A : B;
        public AudioSource Inactive => UsingA ? B : A;

        public float GetVol(AudioSource s) => s == A ? VolA : VolB;
        public void SetVol(AudioSource s, float v) { if (s == A) VolA = v; else VolB = v; }
    }

    private AmbienceChannel[] _ambience;

    // 앰비언스 소스에 붙는 저역통과 필터(소스마다 1개). 실내 먹먹함 연출용.
    private AudioLowPassFilter[] _ambienceFilters;

    [Header("Ambience")]
    [Tooltip("앰비언스 기준 음량. 클립 원본(특히 밤 풀벌레)이 커서 전역으로 낮춰 쓴다.\n" +
             "실내 덕킹은 이 값 위에 곱해진다 — 0.5 × 0.5 = 실내에서 원본의 25%.")]
    [SerializeField, Range(0f, 1f)] private float ambienceBaseVolume = 0.5f;

    // 실내 덕킹 스케일. 1 = 실외(덕킹 없음).
    private float _ambienceScale = 1f;
    private Coroutine _ambienceFilterFade;

    /// <summary>저역통과를 사실상 끈 상태의 컷오프(Unity 기본 최대치).</summary>
    public const float AmbienceCutoffOpen = 22000f;

    // ── 상태 루프 (심장·드릴 모터·제트팩) ──
    // handle은 호출측이 정하는 식별자. handle당 전용 AudioSource 1개를 지연 생성해 캐시한다.
    private sealed class LoopChannel
    {
        public AudioSource Source;
        public string Key;
        public Coroutine Fade;
    }

    private readonly Dictionary<string, LoopChannel> _loops = new Dictionary<string, LoopChannel>();

    private Dictionary<string, AudioClip> bgmClipDict;
    private Dictionary<string, AudioClip> sfxClipDict;

    private void InitializeAudioSources()
    {
        // bgmSource 초기화
        bgmSource = gameObject.AddComponent<AudioSource>();
        bgmSource.outputAudioMixerGroup = bgmGroup;
        bgmSource.loop = true;
        bgmSource.playOnAwake = false;

        // SFX 원샷 풀 — 3D 재생 시 개별 위치가 필요하므로 소스마다 자식 오브젝트를 만든다
        _sfxPool = new AudioSource[SfxPoolSize];
        for (int i = 0; i < SfxPoolSize; i++)
        {
            var go = new GameObject($"SfxSource_{i}");
            go.transform.SetParent(transform, false);

            var src = go.AddComponent<AudioSource>();
            src.outputAudioMixerGroup = sfxGroup;
            src.loop = false;
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 3f;
            src.maxDistance = 25f;
            _sfxPool[i] = src;
        }

        // 앰비언스 채널 2개 × 소스 2개
        _ambience = new AmbienceChannel[2];
        _ambienceFilters = new AudioLowPassFilter[4];
        for (int i = 0; i < 2; i++)
        {
            _ambience[i] = new AmbienceChannel
            {
                A = CreateAmbienceSource($"Ambience{i}_A", i * 2),
                B = CreateAmbienceSource($"Ambience{i}_B", i * 2 + 1),
                UsingA = true,
            };
        }
    }

    private AudioSource CreateAmbienceSource(string name, int filterIndex)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);

        var src = go.AddComponent<AudioSource>();
        src.outputAudioMixerGroup = ambienceGroup != null ? ambienceGroup : bgmGroup;
        src.loop = true;
        src.playOnAwake = false;
        src.spatialBlend = 0f;
        src.volume = 0f;

        // AudioLowPassFilter는 같은 GameObject의 AudioSource에만 걸린다 →
        // 소스마다 오브젝트가 따로라 필터도 소스마다 붙인다.
        // 컨테이너(집) 안에서 BGM이 먹먹해질 때 앰비언스도 같이 먹먹해지도록 쓴다.
        var lpf = go.AddComponent<AudioLowPassFilter>();
        lpf.cutoffFrequency = AmbienceCutoffOpen;
        _ambienceFilters[filterIndex] = lpf;

        return src;
    }

    void Start()
    {
        InitializeDictionaries();
        LoadVolumes();
    }

    private void LoadVolumes()
    {
        SetMasterVolume(GetMasterVolume());
        SetBGMVolume(GetBGMVolume());
        SetSFXVolume(GetSFXVolume());
    }

    private void InitializeDictionaries()
    {
        bgmClipDict = new Dictionary<string, AudioClip>();
        sfxClipDict = new Dictionary<string, AudioClip>();

        if (soundData == null)
        {
            Debug.LogError("SoundManager: SoundDataSO is not assigned!");
            return;
        }

        // SoundData에 항목은 있는데 클립 참조가 끊긴 것들을 모아 한 번만 경고한다.
        //
        // 왜 필요한가: 이 시스템은 "클립이 없으면 조용히 무음"이 원칙이라(아직 음원을 안 채운
        // 훅이 경고를 스팸하지 않도록) 끊긴 참조도 똑같이 조용하다. 그래서
        // 파일을 교체(.mp3 → .ogg 등)하고 Rescan을 잊으면 SoundData가 삭제된 에셋의 GUID를
        // 가리킨 채 남고, 원인 표시 없이 소리만 사라진다. 실제로 한 번 겪었다.
        //
        // "항목 자체가 없음"(= 아직 안 채운 소리)은 계속 조용하고,
        // "항목은 있는데 클립이 null"(= 끊긴 참조)만 잡아낸다.
        var broken = new List<string>();

        foreach (var clip in soundData.bgmClips)
        {
            if (string.IsNullOrEmpty(clip.soundName)) continue;
            if (clip.audioClip != null) bgmClipDict[clip.soundName] = clip.audioClip;
            else broken.Add($"{clip.soundName} (BGM)");
        }

        foreach (var clip in soundData.sfxClips)
        {
            if (string.IsNullOrEmpty(clip.soundName)) continue;
            if (clip.audioClip != null) sfxClipDict[clip.soundName] = clip.audioClip;
            else broken.Add(clip.soundName);
        }

        if (broken.Count > 0)
        {
            Debug.LogWarning(
                $"[SoundManager] 클립 참조가 끊긴 항목 {broken.Count}개 — 그 소리는 무음이 된다:\n" +
                "  " + string.Join(", ", broken) + "\n" +
                "원인: 음원 파일을 교체·삭제한 뒤 Tools/Sound/Rescan SFX Folder 를 안 돌렸을 가능성이 크다.\n" +
                "해결: Assets/Audio/SFX/ 에 키와 같은 이름의 파일이 있는지 확인하고 Rescan 실행.",
                soundData);
        }
    }

    /// <summary>
    /// 배경음악을 재생합니다.
    /// </summary>
    public void PlayBGM(string soundName)
    {
        if (bgmClipDict.TryGetValue(soundName, out AudioClip clip))
        {
            if (bgmSource.clip == clip && bgmSource.isPlaying) return;

            bgmSource.clip = clip;
            bgmSource.Play();
        }
        else
        {
            Debug.LogWarning($"SoundManager: BGM not found: {soundName}");
        }
    }

    #region SFX
    /// <summary>효과음(SFX)을 재생합니다. 클립이 없으면 조용히 무시합니다.</summary>
    public void PlaySFX(string soundName) => PlaySFX(soundName, 1f, 1f);

    /// <summary>pitch/volume을 지정해 재생합니다.</summary>
    public void PlaySFX(string soundName, float pitch, float volume = 1f)
    {
        var src = AcquireSfxSource(soundName, out AudioClip clip);
        if (src == null) return;

        src.transform.localPosition = Vector3.zero;
        src.spatialBlend = 0f;
        src.pitch = pitch;
        src.PlayOneShot(clip, volume);
    }

    /// <summary>PitchJitter만큼 음정을 흔들어 재생합니다 — 발소리·곡괭이처럼 반복되는 소리용.</summary>
    public void PlaySFXJittered(string soundName)
    {
        float j = PitchJitter;
        PlaySFX(soundName, j <= 0f ? 1f : Random.Range(1f - j, 1f + j), 1f);
    }

    /// <summary>
    /// 같은 소리를 interval 간격으로 count번 재생한다 — 강화 망치질 같은 연타 연출용.
    ///
    /// 코루틴을 SoundManager(DontDestroyOnLoad)가 돌리므로, 호출한 UI가 도중에 닫혀
    /// 파괴돼도 소리가 끊기지 않는다. UI 쪽에서 StartCoroutine하면 그런 문제가 생긴다.
    ///
    /// 대기는 Realtime이다 — 강화·정산 UI는 timeScale이 0일 수 있다.
    /// pitchStep을 주면 타격마다 음정이 올라가 상승감이 생긴다.
    /// </summary>
    public void PlaySFXRepeat(string key, int count, float interval = 0.15f,
                              float pitchStep = 0f, float volume = 1f)
    {
        if (string.IsNullOrEmpty(key) || count <= 0) return;
        if (!HasSFX(key)) return;
        StartCoroutine(RepeatRoutine(key, count, interval, pitchStep, volume));
    }

    private System.Collections.IEnumerator RepeatRoutine(
        string key, int count, float interval, float pitchStep, float volume)
    {
        for (int i = 0; i < count; i++)
        {
            PlaySFX(key, 1f + pitchStep * i, volume);
            if (i < count - 1) yield return new WaitForSecondsRealtime(interval);
        }
    }

    /// <summary>월드 좌표에서 3D로 재생합니다 — 돌 파괴·폭발·낙석처럼 위치가 있는 소리용.</summary>
    public void PlaySFXAt(string soundName, Vector3 worldPos, float pitch = 1f, float volume = 1f)
    {
        var src = AcquireSfxSource(soundName, out AudioClip clip);
        if (src == null) return;

        src.transform.position = worldPos;
        src.spatialBlend = 1f;
        src.pitch = pitch;
        src.PlayOneShot(clip, volume);
    }

    /// <summary>
    /// 풀에서 다음 소스를 꺼낸다. 클립이 없거나 스로틀에 걸리면 null.
    ///
    /// 불변식: PlaySFXAt이 spatialBlend를 1로 바꾸므로, 2D 재생 경로(PlaySFX)는 매번
    /// spatialBlend와 위치를 명시적으로 되돌린다. 되돌리지 않으면 다음 2D 원샷이
    /// 엉뚱한 위치에서 들린다.
    /// </summary>
    private AudioSource AcquireSfxSource(string soundName, out AudioClip clip)
    {
        clip = null;
        if (sfxClipDict == null) return null;
        if (string.IsNullOrEmpty(soundName)) return null;

        if (!sfxClipDict.TryGetValue(soundName, out clip) || clip == null)
        {
            if (logSilentMisses)
                Debug.LogWarning($"[SFX] 무음 — '{soundName}' 키가 등록돼 있지 않다.");
            return null;
        }

        if (!_throttle.ShouldPlay(soundName, Time.unscaledTime)) return null;
        if (_sfxPool == null || _sfxPool.Length == 0) return null;

        if (logPlays) Debug.Log($"[SFX] {soundName}  (clip: {clip.name})");

        var src = _sfxPool[_sfxCursor];
        _sfxCursor = (_sfxCursor + 1) % _sfxPool.Length;
        return src;
    }

    // 기존 코드와의 호환성을 위한 메서드
    public void PlaySound(string soundName) => PlaySFX(soundName);

    /// <summary>
    /// 해당 SFX 클립이 등록되어 있는지 확인합니다.
    /// 아직 사운드를 채우지 않은 연출 훅에서 경고 스팸 없이 조건부 재생할 때 사용.
    /// </summary>
    public bool HasSFX(string soundName)
        => sfxClipDict != null && !string.IsNullOrEmpty(soundName) && sfxClipDict.ContainsKey(soundName);

    /// <summary>
    /// 등록된 SFX 클립을 반환합니다. 없으면 null.
    /// 커스텀 AudioSource로 직접 재생해야 하는 연출(피치·볼륨 제어)에서 사용.
    /// </summary>
    public AudioClip GetSFX(string soundName)
        => sfxClipDict != null && !string.IsNullOrEmpty(soundName)
           && sfxClipDict.TryGetValue(soundName, out AudioClip clip) ? clip : null;
    #endregion

    public void StopBGM() => bgmSource.Stop();

    #region Ambience
    /// <summary>
    /// 해당 레이어의 앰비언스를 교체한다. 같은 키가 이미 재생 중이면 무시한다
    /// (씬 재진입·시간대 재통지로 크로스페이드가 재시작되면 소리가 끊긴다).
    /// 클립이 없으면 아무 것도 하지 않는다 — 이전 앰비언스도 유지된다.
    /// </summary>
    public void SetAmbience(AmbienceLayer layer, string key, float fade = 2f)
    {
        if (_ambience == null) return;

        var ch = _ambience[(int)layer];
        if (ch.CurrentKey == key) return;

        if (string.IsNullOrEmpty(key)) { StopAmbience(layer, fade); return; }
        if (sfxClipDict == null || !sfxClipDict.TryGetValue(key, out AudioClip clip) || clip == null)
            return; // 미등록 — 조용히 무시

        ch.CurrentKey = key;

        var next = ch.Inactive;
        next.clip = clip;
        ch.SetVol(next, 0f);      // 논리 볼륨부터 0으로 — 앞선 크로스페이드가 중단됐을 수 있다
        ApplyAmbienceVolume(ch);
        next.Play();

        if (ch.Fade != null) StopCoroutine(ch.Fade);
        ch.Fade = StartCoroutine(CrossFadeAmbience(ch, fade));
    }

    public void StopAmbience(AmbienceLayer layer, float fade = 2f)
    {
        if (_ambience == null) return;

        var ch = _ambience[(int)layer];
        if (ch.CurrentKey == null) return;
        ch.CurrentKey = null;

        if (ch.Fade != null) StopCoroutine(ch.Fade);
        ch.Fade = StartCoroutine(FadeOutAmbience(ch, fade));
    }

    public void StopAllAmbience(float fade = 2f)
    {
        StopAmbience(AmbienceLayer.Primary, fade);
        StopAmbience(AmbienceLayer.Secondary, fade);
    }

    private System.Collections.IEnumerator CrossFadeAmbience(AmbienceChannel ch, float duration)
    {
        AudioSource from = ch.Active;
        AudioSource to   = ch.Inactive;
        float fromStart = ch.GetVol(from);

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = duration > 0f ? Mathf.Clamp01(t / duration) : 1f;
            ch.SetVol(from, Mathf.Lerp(fromStart, 0f, k));
            ch.SetVol(to,   Mathf.Lerp(0f, 1f, k));
            ApplyAmbienceVolume(ch);
            yield return null;
        }

        ch.SetVol(from, 0f);
        ch.SetVol(to, 1f);
        ApplyAmbienceVolume(ch);
        from.Stop();

        ch.UsingA = !ch.UsingA;
        ch.Fade = null;
    }

    private System.Collections.IEnumerator FadeOutAmbience(AmbienceChannel ch, float duration)
    {
        float aStart = ch.VolA, bStart = ch.VolB;

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = duration > 0f ? Mathf.Clamp01(t / duration) : 1f;
            ch.VolA = Mathf.Lerp(aStart, 0f, k);
            ch.VolB = Mathf.Lerp(bStart, 0f, k);
            ApplyAmbienceVolume(ch);
            yield return null;
        }

        ch.VolA = 0f; ch.VolB = 0f;
        ApplyAmbienceVolume(ch);
        ch.A.Stop(); ch.B.Stop();
        ch.Fade = null;
    }

    /// <summary>논리 볼륨 × 기준 음량 × 실내 덕킹을 실제 AudioSource에 반영한다.</summary>
    private void ApplyAmbienceVolume(AmbienceChannel ch)
    {
        float g = ambienceBaseVolume * _ambienceScale;
        ch.A.volume = ch.VolA * g;
        ch.B.volume = ch.VolB * g;
    }

    /// <summary>
    /// 앰비언스를 먹먹하게(저역통과) + 작게(덕킹) 만든다 — 집·컨테이너 실내 진입 연출용.
    ///
    /// BGM 쪽 먹먹함은 <c>ContainerFade</c>가 스피커의 AudioLowPassFilter를 직접 만지지만,
    /// 앰비언스 소스는 이 매니저가 런타임에 만드는 자식 오브젝트라 인스펙터로 물릴 수 없다.
    /// 그래서 같은 연출을 여기 API로 연다.
    ///
    /// 볼륨은 크로스페이드가 쓰는 논리 볼륨과 곱해지므로, 실내에 있는 동안
    /// 시간대가 바뀌어 앰비언스가 교체돼도 덕킹이 풀리지 않는다.
    /// </summary>
    /// <param name="cutoffHz">저역통과 컷오프. <see cref="AmbienceCutoffOpen"/>이면 사실상 원음.</param>
    /// <param name="volumeScale">0~1. 1이면 원래 크기.</param>
    /// <param name="duration">변화에 걸리는 시간(초). 0이면 즉시.</param>
    public void SetAmbienceFilter(float cutoffHz, float volumeScale, float duration = 1.5f)
    {
        if (_ambience == null || _ambienceFilters == null) return;

        if (_ambienceFilterFade != null) StopCoroutine(_ambienceFilterFade);
        _ambienceFilterFade = StartCoroutine(
            AmbienceFilterRoutine(cutoffHz, Mathf.Clamp01(volumeScale), duration));
    }

    /// <summary>실내 연출을 원상 복구한다.</summary>
    public void ClearAmbienceFilter(float duration = 1.5f)
        => SetAmbienceFilter(AmbienceCutoffOpen, 1f, duration);

    private System.Collections.IEnumerator AmbienceFilterRoutine(
        float targetCutoff, float targetScale, float duration)
    {
        float startCutoff = _ambienceFilters[0] != null
            ? _ambienceFilters[0].cutoffFrequency
            : AmbienceCutoffOpen;
        float startScale = _ambienceScale;

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = duration > 0f ? Mathf.Clamp01(t / duration) : 1f;

            SetAmbienceCutoff(Mathf.Lerp(startCutoff, targetCutoff, k));
            _ambienceScale = Mathf.Lerp(startScale, targetScale, k);
            for (int i = 0; i < _ambience.Length; i++) ApplyAmbienceVolume(_ambience[i]);

            yield return null;
        }

        SetAmbienceCutoff(targetCutoff);
        _ambienceScale = targetScale;
        for (int i = 0; i < _ambience.Length; i++) ApplyAmbienceVolume(_ambience[i]);

        _ambienceFilterFade = null;
    }

    private void SetAmbienceCutoff(float hz)
    {
        for (int i = 0; i < _ambienceFilters.Length; i++)
            if (_ambienceFilters[i] != null) _ambienceFilters[i].cutoffFrequency = hz;
    }
    #endregion

    #region State Loops
    /// <summary>
    /// 조건이 유지되는 동안 계속 울리는 소리를 시작한다.
    /// 같은 handle로 다시 호출하면 클립만 교체하고 페이드는 유지한다.
    /// 클립이 없으면 아무 것도 하지 않는다.
    /// </summary>
    public void Loop(string handle, string key, float fade = 0.15f, float pitch = 1f)
    {
        if (string.IsNullOrEmpty(handle)) return;
        if (sfxClipDict == null || string.IsNullOrEmpty(key)) return;

        if (!sfxClipDict.TryGetValue(key, out AudioClip clip) || clip == null)
        {
            if (logSilentMisses)
                Debug.LogWarning($"[SFX] 무음 — '{key}' 키가 등록돼 있지 않다. (loop: {handle})");
            return;
        }

        var ch = GetOrCreateChannel(handle);
        ch.Source.loop = true;
        ch.Source.pitch = pitch;

        // 이미 같은 클립으로 울리는 중이면 재시작하지 않는다(매 프레임 호출돼도 안전)
        if (ch.Key == key && ch.Source.isPlaying)
        {
            if (ch.Fade == null && ch.Source.volume < 1f)
                ch.Fade = StartCoroutine(FadeLoop(ch, 1f, fade));
            return;
        }

        ch.Key = key;
        ch.Source.clip = clip;
        ch.Source.Play();

        if (ch.Fade != null) StopCoroutine(ch.Fade);
        ch.Fade = StartCoroutine(FadeLoop(ch, 1f, fade));
    }

    /// <summary>
    /// 같은 handle의 이전 재생을 끊고 처음부터 다시 재생한다 — 겹침이 생기면 안 되는 소리용.
    ///
    /// 원샷 풀(PlaySFX)은 호출마다 다른 소스를 쓰므로 클립이 길면 소리가 쌓인다.
    /// 발소리처럼 빠르게 반복되는 것은 handle 전용 소스 하나를 재사용해 항상 1개만 울린다.
    /// </summary>
    public void PlaySFXExclusive(string handle, string key, float pitch = 1f, float volume = 1f)
    {
        if (string.IsNullOrEmpty(handle)) return;
        if (sfxClipDict == null || string.IsNullOrEmpty(key)) return;

        if (!sfxClipDict.TryGetValue(key, out AudioClip clip) || clip == null)
        {
            if (logSilentMisses)
                Debug.LogWarning($"[SFX] 무음 — '{key}' 키가 등록돼 있지 않다. (exclusive: {handle})");
            return;
        }

        if (!_throttle.ShouldPlay(key, Time.unscaledTime)) return;

        if (logPlays) Debug.Log($"[SFX] {key}  (clip: {clip.name}, exclusive: {handle})");

        var ch = GetOrCreateChannel(handle);
        if (ch.Fade != null) { StopCoroutine(ch.Fade); ch.Fade = null; }

        ch.Key = null;              // 루프 상태가 아님을 표시 (IsLooping이 false가 되도록)
        ch.Source.loop = false;
        ch.Source.clip = clip;
        ch.Source.pitch = pitch;
        ch.Source.volume = volume;
        ch.Source.Play();           // 재생 중이면 끊고 처음부터
    }

    /// <summary>handle 전용 AudioSource를 지연 생성해 캐시한다. Loop / PlaySFXExclusive 공용.</summary>
    private LoopChannel GetOrCreateChannel(string handle)
    {
        if (_loops.TryGetValue(handle, out LoopChannel ch)) return ch;

        var go = new GameObject($"Loop_{handle}");
        go.transform.SetParent(transform, false);

        var src = go.AddComponent<AudioSource>();
        src.outputAudioMixerGroup = sfxGroup;
        src.loop = true;
        src.playOnAwake = false;
        src.spatialBlend = 0f;
        src.volume = 0f;

        ch = new LoopChannel { Source = src };
        _loops[handle] = ch;
        return ch;
    }

    public void StopLoop(string handle, float fade = 0.25f)
    {
        if (string.IsNullOrEmpty(handle)) return;
        if (!_loops.TryGetValue(handle, out LoopChannel ch)) return;
        if (!ch.Source.isPlaying && ch.Source.volume <= 0f) return;

        ch.Key = null;
        if (ch.Fade != null) StopCoroutine(ch.Fade);
        ch.Fade = StartCoroutine(FadeLoop(ch, 0f, fade));
    }

    /// <summary>재생 중인 루프의 음정을 바꾼다 — 심장 박동 가속 등.</summary>
    public void SetLoopPitch(string handle, float pitch)
    {
        if (string.IsNullOrEmpty(handle)) return;
        if (_loops.TryGetValue(handle, out LoopChannel ch) && ch.Source != null)
            ch.Source.pitch = pitch;
    }

    public bool IsLooping(string handle)
        => !string.IsNullOrEmpty(handle)
           && _loops.TryGetValue(handle, out LoopChannel ch)
           && ch.Key != null;

    private System.Collections.IEnumerator FadeLoop(LoopChannel ch, float target, float duration)
    {
        float start = ch.Source.volume;

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = duration > 0f ? Mathf.Clamp01(t / duration) : 1f;
            ch.Source.volume = Mathf.Lerp(start, target, k);
            yield return null;
        }

        ch.Source.volume = target;
        if (target <= 0f) ch.Source.Stop();
        ch.Fade = null;
    }
    #endregion

    #region Volume Control
    public float GetMasterVolume() => PlayerPrefs.GetFloat("MasterVolume", 1f);
    public float GetBGMVolume() => PlayerPrefs.GetFloat("BGMVolume", 1f);
    public float GetSFXVolume() => PlayerPrefs.GetFloat("SFXVolume", 1f);

    public void SetMasterVolume(float volume)
    {
        SetMixerVolume("MasterVolume", volume);
        PlayerPrefs.SetFloat("MasterVolume", volume);
    }

    public void SetBGMVolume(float volume)
    {
        SetMixerVolume("BGMVolume", volume);
        PlayerPrefs.SetFloat("BGMVolume", volume);
    }

    public void SetSFXVolume(float volume)
    {
        SetMixerVolume("SFXVolume", volume);
        PlayerPrefs.SetFloat("SFXVolume", volume);
    }

    private void SetMixerVolume(string parameterName, float volume)
    {
        if (mainMixer == null) return;

        // 0~1 값을 -80dB ~ 0dB로 변환
        float dB = volume > 0.0001f ? Mathf.Log10(volume) * 20 : -80f;
        mainMixer.SetFloat(parameterName, dB);
    }

    // ── 미리듣기 ──
    // 설정 오버레이는 '적용'을 눌러야 저장한다. 하지만 볼륨은 귀로 확인해야 고를 수 있으므로,
    // 슬라이더를 잡고 있는 동안은 저장 없이 믹서에만 반영한다.
    // 취소로 닫으면 ReapplySavedVolumes()가 저장값으로 되돌린다.
    public void PreviewMasterVolume(float volume) => SetMixerVolume("MasterVolume", volume);
    public void PreviewBGMVolume(float volume)    => SetMixerVolume("BGMVolume", volume);
    public void PreviewSFXVolume(float volume)    => SetMixerVolume("SFXVolume", volume);

    /// <summary>PlayerPrefs에 저장된 볼륨을 믹서에 다시 적용한다(미리듣기 취소용).</summary>
    public void ReapplySavedVolumes() => LoadVolumes();
    #endregion

    #region Mixer Routing
    [Header("Auto Routing")]
    [Tooltip("씬 로드마다 믹서 그룹이 비어 있는 AudioSource를 찾아 SFX 그룹에 연결한다.\n" +
             "끄면 설정의 효과음 슬라이더가 씬에 손으로 놓인 소스를 조종하지 못한다.")]
    [SerializeField] private bool autoRouteSceneSources = true;

    [Tooltip("자동 연결된 소스를 콘솔에 찍는다. 어떤 소리가 믹서를 안 거치고 있었는지 확인용.")]
    [SerializeField] private bool logAutoRouting = false;

    /// <summary>
    /// 믹서 그룹이 비어 있는 AudioSource를 전부 SFX 그룹에 연결한다.
    ///
    /// 그룹이 없는 소스는 믹서를 우회해 리스너로 직행한다 → 설정의 볼륨 슬라이더가 통하지 않는다.
    /// 씬 로드 직후에 돌기 때문에, 자기 Awake에서 이미 채널을 지정한 소스
    /// (BGMCycleManager의 BGM 등)는 그룹이 차 있어 여기서 건드리지 않는다.
    /// 런타임에 생성되는 프리팹은 이 스윕에 잡히지 않으므로 각자 AudioRouting.Route를 부른다.
    /// </summary>
    public void RouteUnassignedSceneSources()
    {
        if (!autoRouteSceneSources || sfxGroup == null) return;

        var sources = FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < sources.Length; i++)
        {
            var src = sources[i];
            if (src == null || src.outputAudioMixerGroup != null) continue;

            src.outputAudioMixerGroup = sfxGroup;
            if (logAutoRouting)
                Debug.Log($"[SoundManager] 믹서 미연결 AudioSource를 SFX로 연결: {src.name}", src);
        }
    }

    /// <summary>
    /// 클립을 월드 좌표에서 3D로 재생한다 — AudioSource.PlayClipAtPoint의 대체.
    /// 원본은 믹서 그룹이 없는 임시 소스를 만들어 효과음 슬라이더를 무시한다.
    /// 이쪽은 SFX 풀(=SFX 그룹)을 쓰므로 설정이 그대로 먹는다.
    /// </summary>
    public void PlayClipAt(AudioClip clip, Vector3 worldPos, float volume = 1f, float pitch = 1f)
    {
        if (clip == null || _sfxPool == null || _sfxPool.Length == 0) return;

        var src = _sfxPool[_sfxCursor];
        _sfxCursor = (_sfxCursor + 1) % _sfxPool.Length;

        src.transform.position = worldPos;
        src.spatialBlend = 1f;
        src.pitch = pitch;
        src.PlayOneShot(clip, volume);
    }

    /// <summary>클립을 2D로 재생한다(위치 없는 UI·피드백용). 효과음 슬라이더를 따른다.</summary>
    public void PlayClip(AudioClip clip, float volume = 1f, float pitch = 1f)
    {
        if (clip == null || _sfxPool == null || _sfxPool.Length == 0) return;

        var src = _sfxPool[_sfxCursor];
        _sfxCursor = (_sfxCursor + 1) % _sfxPool.Length;

        // PlaySFXAt이 spatialBlend를 1로 바꿔놓을 수 있다 → 2D 경로는 매번 되돌린다.
        src.transform.localPosition = Vector3.zero;
        src.spatialBlend = 0f;
        src.pitch = pitch;
        src.PlayOneShot(clip, volume);
    }
    #endregion
}
