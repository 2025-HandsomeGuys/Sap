// @tags: relic, detection, sfx, audio, procedural
using UnityEngine;

namespace Relic
{
    /// <summary>
    /// 탐지파동 "띵" 재생기. 사인파를 코드로 합성하므로 오디오 에셋이 필요 없다.
    /// (SoundDataSO에 "relic_detect_ping"을 등록하면 그 클립이 우선 사용된다.)
    ///
    /// 겹침 방지 3중 — 한 파동에서 여러 청크가 동시에 발견돼도 소리가 합산되어
    /// 커지지 않아야 한다:
    ///   1. 단일 보이스 재리트리거 — PlayOneShot을 쓰지 않는다. 소스가 하나뿐이고
    ///      Stop() 후 Play()하므로 진폭 합산이 구조적으로 불가능하다.
    ///   2. 최소 간격 0.12초 — 같은 프레임의 다중 발견을 "띵" 1회로 흡수한다.
    ///   3. 순번 볼륨 감쇠 — 뒤로 갈수록 작아진다(하한 0.35).
    /// </summary>
    public static class DetectionPingSfx
    {
        // 리터럴이 아니라 SfxKeys를 참조한다 — 양쪽이 갈라지면 조용히 무음이 된다.
        private const string OverrideKey = SfxKeys.RelicDetectPing;
        private const float MinInterval = 0.12f;

        private const int SampleRate = 44100;
        private const float ClipLength = 0.25f;
        private const float BaseFreq = 1318.5f;   // E6 — 맑은 "띵"

        private static AudioSource _src;
        private static AudioClip _clip;
        private static int _index;
        private static float _lastPlayTime = -999f;

        /// <summary>파동 발동 시 호출. 순번을 리셋해야 두 번째 발동이 최고음에서 시작하지 않는다.</summary>
        public static void BeginSequence()
        {
            _index = 0;
        }

        /// <summary>핑 1회. 순번이 오를수록 피치는 높아지고 볼륨은 낮아진다.</summary>
        public static void PlayNext()
        {
            // 최소 간격 — 같은 프레임 다중 발견을 1회로 흡수(겹침 방지 2)
            if (Time.unscaledTime - _lastPlayTime < MinInterval) return;

            var src = EnsureSource();
            if (src == null) return;   // SoundManager 미초기화 — 조용히 스킵

            src.clip = ResolveClip();
            if (src.clip == null) return;

            src.pitch = Mathf.Min(1.5f, 1f + _index * 0.06f);
            src.volume = Mathf.Max(0.35f, 1f - _index * 0.12f);

            // 단일 보이스 재리트리거(겹침 방지 1)
            src.Stop();
            src.Play();

            _lastPlayTime = Time.unscaledTime;
            _index++;
        }

        private static AudioSource EnsureSource()
        {
            // Unity의 "가짜 null" 대응: Enter Play Mode Options로 도메인 리로드를 끄면
            // static 참조가 파괴된 오브젝트를 가리킨 채 살아남는다. 매 호출 검사한다.
            if (_src != null) return _src;

            var sm = SoundManager.Instance;
            if (sm == null) return null;

            var go = new GameObject("DetectionPingSfx");
            Object.DontDestroyOnLoad(go);
            _src = go.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.loop = false;
            // 필수: 이걸 빠뜨리면 마스터/SFX 볼륨을 0으로 내려도 이 소리만 계속 울린다.
            _src.outputAudioMixerGroup = sm.sfxGroup;
            return _src;
        }

        private static AudioClip ResolveClip()
        {
            var sm = SoundManager.Instance;
            if (sm != null && sm.HasSFX(OverrideKey))
            {
                var over = sm.GetSFX(OverrideKey);
                if (over != null) return over;
            }
            return _clip != null ? _clip : (_clip = BuildChime());
        }

        /// <summary>지수 감쇠 엔벨로프를 씌운 사인파 + 옥타브 배음. 최초 1회만 생성된다.</summary>
        private static AudioClip BuildChime()
        {
            int count = Mathf.RoundToInt(SampleRate * ClipLength);
            var data = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Exp(-t * 14f);                       // 빠른 감쇠 = "띵"
                float w = Mathf.Sin(2f * Mathf.PI * BaseFreq * t)
                        + 0.35f * Mathf.Sin(4f * Mathf.PI * BaseFreq * t); // 옥타브 배음
                data[i] = w * env * 0.35f;
            }

            var clip = AudioClip.Create("DetectPing", count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
