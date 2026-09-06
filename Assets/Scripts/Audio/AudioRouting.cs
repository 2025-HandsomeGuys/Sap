// @tags: audio, mixer, routing, volume, settings
using UnityEngine;
using UnityEngine.Audio;

/// <summary>설정 슬라이더가 조종하는 믹서 채널. Master.mixer의 그룹 이름과 1:1 대응한다.</summary>
public enum AudioChannel
{
    SFX,
    BGM,
    Ambience,
}

/// <summary>
/// AudioSource를 SoundManager의 AudioMixer 그룹에 연결한다.
///
/// 왜 필요한가: 설정의 사운드 슬라이더는 믹서의 노출 파라미터
/// (MasterVolume/BGMVolume/SFXVolume)만 움직인다. outputAudioMixerGroup이 비어 있는
/// AudioSource는 믹서를 거치지 않고 곧장 리스너로 나가기 때문에, 슬라이더를 0으로 내려도
/// 그 소리만 그대로 들린다. 씬·프리팹에 손으로 놓은 AudioSource와 런타임에
/// AddComponent로 만든 AudioSource가 전부 이 경우였다.
///
/// 새 AudioSource를 만들거나 인스펙터에서 물려 쓸 때는 Awake에서 한 줄로 연결한다:
///   AudioRouting.Route(mySource);                       // 효과음
///   AudioRouting.Route(bgmSource, AudioChannel.BGM);    // 배경음악
/// 씬에 놓인 소스를 깜빡해도 SoundManager가 씬 로드마다 쓸어담아 SFX로 연결한다
/// (SoundManager.RouteUnassignedSceneSources).
/// </summary>
public static class AudioRouting
{
    /// <summary>채널에 해당하는 믹서 그룹. SoundManager/믹서가 없으면 null.</summary>
    public static AudioMixerGroup GetGroup(AudioChannel channel)
    {
        SoundManager.EnsureExists();
        var sm = SoundManager.Instance;
        if (sm == null) return null;

        switch (channel)
        {
            case AudioChannel.BGM:      return sm.bgmGroup;
            case AudioChannel.Ambience: return sm.ambienceGroup != null ? sm.ambienceGroup : sm.bgmGroup;
            default:                    return sm.sfxGroup;
        }
    }

    /// <summary>
    /// 소스를 채널에 연결한다. 기본은 '비어 있을 때만' 채운다 —
    /// 인스펙터에서 일부러 다른 그룹을 물려둔 소스를 덮어쓰지 않기 위해서다.
    /// </summary>
    /// <returns>실제로 연결됐으면 true.</returns>
    public static bool Route(AudioSource src, AudioChannel channel = AudioChannel.SFX, bool overwrite = false)
    {
        if (src == null) return false;
        if (!overwrite && src.outputAudioMixerGroup != null) return false;

        var group = GetGroup(channel);
        if (group == null) return false;

        src.outputAudioMixerGroup = group;
        return true;
    }

    /// <summary>루트와 자식에 달린 모든 AudioSource를 연결한다(비활성 포함).</summary>
    public static int RouteHierarchy(GameObject root, AudioChannel channel = AudioChannel.SFX, bool overwrite = false)
    {
        if (root == null) return 0;

        int routed = 0;
        var sources = root.GetComponentsInChildren<AudioSource>(includeInactive: true);
        for (int i = 0; i < sources.Length; i++)
            if (Route(sources[i], channel, overwrite)) routed++;

        return routed;
    }

    /// <summary>
    /// AudioSource.PlayClipAtPoint의 대체. 원본은 믹서 그룹이 없는 임시 소스를 만들기 때문에
    /// 효과음 슬라이더를 무시한다. 이쪽은 SoundManager의 SFX 풀을 거친다.
    /// </summary>
    public static void PlayClipAt(AudioClip clip, Vector3 worldPos, float volume = 1f)
    {
        if (clip == null) return;

        SoundManager.EnsureExists();
        var sm = SoundManager.Instance;
        if (sm != null) { sm.PlayClipAt(clip, worldPos, volume); return; }

        // 최후의 폴백 — 매니저가 없으면 소리가 아예 안 나는 것보다는 낫다.
        AudioSource.PlayClipAtPoint(clip, worldPos, volume);
    }
}
