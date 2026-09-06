using UnityEngine;
using System.Collections;

[RequireComponent(typeof(AudioSource))]
public class BGMCycleManager : MonoBehaviour
{
    // 싱글톤과 DontDestroyOnLoad를 제거하여 씬 이동 시 파괴되도록 변경했습니다.

    [Header("시간대별 배경음악")]
    public AudioClip morningBGM;   // 아침(낮)에 틀 음악
    public AudioClip afternoonBGM; // 오후(밤)에 틀 음악

    [Header("크로스페이드 설정")]
    public float fadeDuration = 2f; // 음악이 교체되는 페이드 시간(초)

    // 지상 BGM의 정적 음량 트림.
    // 믹서의 BGM 그룹 볼륨은 노출 파라미터 "BGMVolume" 그 자체라서
    // SoundManager.SetBGMVolume()이 런타임에 계속 덮어쓴다(설정 슬라이더).
    // 따라서 "이 음악은 원래 좀 작게" 같은 정적 감쇠는 믹서가 아니라 여기서 준다.
    [Range(0f, 1f)]
    public float maxVolume = 0.4f;

    private AudioSource primarySource;
    private AudioSource secondarySource;
    private Coroutine fadeCoroutine;

    private void Awake()
    {
        // 씬마다 새로 생성되도록 싱글톤 로직을 제거했습니다.
        SetupAudioSources();
    }

    private void SetupAudioSources()
    {
        // 첫 번째 AudioSource (기존에 달려있는 컴포넌트 사용)
        primarySource = GetComponent<AudioSource>();
        primarySource.loop = true;
        primarySource.playOnAwake = false;
        primarySource.volume = maxVolume;

        // 두 번째 AudioSource (같은 오브젝트에 추가하여 필터 공유)
        secondarySource = gameObject.AddComponent<AudioSource>();
        secondarySource.loop = true;
        secondarySource.playOnAwake = false;
        secondarySource.volume = 0f;

        // 씬에 손으로 놓인 primary도 믹서 그룹이 비어 있는 경우가 많다 → 그러면 지상 BGM 전체가
        // 설정의 BGM/마스터 슬라이더를 무시한다. 비어 있을 때만 BGM 그룹으로 연결한다.
        AudioRouting.Route(primarySource, AudioChannel.BGM);

        // AddComponent로 만든 소스는 믹서 그룹이 비어 있다 → 그대로 두면 크로스페이드
        // 후반 절반이 BGM 볼륨 슬라이더를 무시한다. primary의 그룹을 그대로 복사한다.
        secondarySource.outputAudioMixerGroup = primarySource.outputAudioMixerGroup;
    }

    private void OnEnable()
    {
        if (DayCycleManager.Instance != null)
        {
            DayCycleManager.Instance.OnDateTimeChanged += HandleTimeChanged;
        }
    }

    private void Start()
    {
        if (DayCycleManager.Instance != null)
        {
            DayCycleManager.Instance.OnDateTimeChanged -= HandleTimeChanged;
            DayCycleManager.Instance.OnDateTimeChanged += HandleTimeChanged;

            PlayBGMImmediate(DayCycleManager.Instance.CurrentDay, DayCycleManager.Instance.CurrentTime);
        }
    }

    private void OnDisable()
    {
        if (DayCycleManager.Instance != null)
        {
            DayCycleManager.Instance.OnDateTimeChanged -= HandleTimeChanged;
        }
    }

    private void HandleTimeChanged(int day, TimeOfDay timeOfDay)
    {
        AudioClip targetClip = GetClipByTime(timeOfDay);
        if (targetClip != null)
        {
            PlayBGM(targetClip);
        }
    }

    private AudioClip GetClipByTime(TimeOfDay timeOfDay)
    {
        switch (timeOfDay)
        {
            case TimeOfDay.Morning: return morningBGM;
            case TimeOfDay.Afternoon: return afternoonBGM;
            default: return null;
        }
    }

    private void PlayBGMImmediate(int day, TimeOfDay timeOfDay)
    {
        AudioClip targetClip = GetClipByTime(timeOfDay);
        if (targetClip == null) return;

        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);

        primarySource.Stop();
        secondarySource.Stop();

        primarySource.clip = targetClip;
        primarySource.volume = maxVolume;
        secondarySource.volume = 0f;

        primarySource.Play();
    }

    public void PlayBGM(AudioClip newClip)
    {
        if (primarySource.clip == newClip && primarySource.isPlaying && primarySource.volume > 0f) return;
        if (secondarySource.clip == newClip && secondarySource.isPlaying && secondarySource.volume > 0f) return;

        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
        fadeCoroutine = StartCoroutine(CrossFadeRoutine(newClip));
    }

    private IEnumerator CrossFadeRoutine(AudioClip newClip)
    {
        AudioSource activeSource = (primarySource.isPlaying && primarySource.volume > 0f) ? primarySource : secondarySource;
        AudioSource newSource = (activeSource == primarySource) ? secondarySource : primarySource;

        newSource.clip = newClip;
        newSource.volume = 0f;
        newSource.Play();

        float timer = 0f;
        float startVolume = activeSource.volume;

        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            float progress = timer / fadeDuration;

            if (activeSource.isPlaying)
            {
                activeSource.volume = Mathf.Lerp(startVolume, 0f, progress);
            }
            newSource.volume = Mathf.Lerp(0f, maxVolume, progress);

            yield return null;
        }

        if (activeSource.isPlaying)
        {
            activeSource.Stop();
            activeSource.volume = 0f;
        }
        newSource.volume = maxVolume;
    }
}