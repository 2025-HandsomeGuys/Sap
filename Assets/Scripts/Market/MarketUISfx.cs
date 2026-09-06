// @tags: market, sound, sfx, ui, procedural, chime, chiptune, retro, synth
using System.Collections.Generic;
using UnityEngine;

namespace Market
{
    /// <summary>
    /// 마켓(주식·코인) UI 공용 효과음.
    /// 오디오 에셋 없이 코드로 합성한다. UI 상호작용(커서 이동·탭·타이핑)은 마우스 "찰칵"
    /// 클릭음, 거래 계열(매수/매도·수량)은 기존 부드러운 사인 톤 유지, 코인 결과(승/패)만
    /// 레트로 톤 — 모두 낮은 gain으로 귀에 거슬리지 않게.
    /// 물리 마우스 클릭음은 PlayMouseClick (씬 어디를 눌러도 "모니터 클릭" 촉감).
    ///
    /// 재생 우선순위:
    ///  1. SoundDataSO에 같은 이름의 클립("market_ui_click" 등)이 등록돼 있으면 그 클립 재생
    ///     — 사운드 디자이너가 실제 음원으로 교체 가능 (CoinSfx와 동일 패턴)
    ///  2. 없으면 절차 합성 클립 재생 (SoundManager의 SFX 믹서 그룹을 타므로 볼륨 설정 적용)
    ///
    /// 같은 프레임에 두 소리가 겹치면 우선순위 높은 쪽만 남긴다
    /// (예: 매수 버튼 = 공용 Click + 결과 Confirm → Confirm만 들림).
    /// </summary>
    public static class MarketUISfx
    {
        public enum Kind
        {
            None,     // 무음 — MarketSfxOverride로 특정 버튼을 음소거할 때
            Type,     // 얇은 마우스 클릭 틱 — 타이핑/코인 드럼롤 (찰칵찰칵)
            Click,    // 기본 버튼 클릭 (수량 조절 등) — 부드러운 사인 "틱" 유지
            Select,   // 마우스 "찰칵" 클릭 — 카드/항목 선택
            Tab,      // 마우스 더블클릭 "찰-칵" — 탭·모드 전환
            Confirm,  // 매수/매도/베팅 성공 (사인 5도 상승 차임) — 유지
            Deny,     // 거래 실패/거부 (사인 하강 2음) — 유지
            Win,      // 코인 적중/떡상 (레트로 상승 아르페지오)
            Crash,    // 폭락/상장폐지 (레트로 하강 스윕)
            Scroll,   // 목록/카탈로그 휠 스크롤 — 부드러운 리플(클릭 아님, 연속돼도 안 거슬리게)
            Blip,     // 코인 가속 드럼롤 "띠" — 간격·피치가 점점 올라가는 비프(차트 끝점 스텝과 동시 발화)
        }

        // ===================================================
        // 공개 API
        // ===================================================
        /// <summary>pitch(기본 1)로 재생 속도·음높이를 조절한다 — 코인 가속 드럼롤의 피치 상승 등.</summary>
        public static void Play(Kind kind, float pitch = 1f)
        {
            if (kind == Kind.None) return;

            // 스크롤 스로틀 — 빠른 휠은 프레임마다 호출돼 PlayOneShot이 중첩 합산되며
            // 소리가 터진다. 클립 길이(30ms) 이상 간격을 강제해 겹침 없이 "차라라락"만 남긴다.
            if (kind == Kind.Scroll)
            {
                if (Time.unscaledTime - _lastScrollTime < ScrollMinInterval) return;
                _lastScrollTime = Time.unscaledTime;
            }

            // 같은 프레임 중복 — 낮은 우선순위는 스킵, 높은 쪽은 이전 소리를 끊고 교체
            if (Time.frameCount == _lastFrame)
            {
                if (Priority(kind) <= Priority(_lastKind)) return;
                if (_source) _source.Stop();
            }
            _lastFrame = Time.frameCount;
            _lastKind = kind;

            var src = ResolveSource();
            if (src == null) return;

            // 1) 사용자 지정 에셋 클립 우선 (MarketSfxSet — Inspector에서 자유 교체)
            var custom = _assets != null ? _assets.Clip(kind) : null;
            if (custom != null)
            {
                src.pitch = pitch; // 에셋은 원음 기준(기본 1) — 드럼롤 등 요청 피치만 반영
                src.PlayOneShot(custom, _assets.volume);
                return;
            }

            // 2) SoundDataSO에 같은 이름으로 등록된 클립 (기존 경로)
            var sm = SoundManager.Instance;
            string clipName = ClipName(kind);
            if (sm != null && sm.HasSFX(clipName))
            {
                sm.PlaySFX(clipName);
                return;
            }

            // 3) 코드 합성음 폴백
            if (!_cache.TryGetValue(kind, out var clip) || clip == null)
                _cache[kind] = clip = Build(kind);
            if (clip == null) return;

            src.pitch = pitch * Random.Range(0.98f, 1.02f); // 요청 피치 × 미세 랜덤(연타 시 단조로움 방지)
            src.PlayOneShot(clip);
        }

        /// <summary>씬의 효과음 에셋 세트를 지정한다(비우면 코드 합성음). MarketSceneController가 호출.</summary>
        public static void SetAssetSet(MarketSfxSet set) => _assets = set;

        // ===================================================
        // 재생 인프라
        // ===================================================
        private static AudioSource _source;
        private static MarketSfxSet _assets; // 사용자 지정 오디오 에셋 세트(없으면 합성음)
        private static readonly Dictionary<Kind, AudioClip> _cache = new Dictionary<Kind, AudioClip>();
        private static int _lastFrame = -1;
        private static Kind _lastKind = Kind.None;
        private static float _lastScrollTime = -1f;
        private const float ScrollMinInterval = 0.06f; // 스크롤 재생 최소 간격(초) — 클립 겹침 방지

        private static int Priority(Kind k)
        {
            switch (k)
            {
                case Kind.Type:
                case Kind.Scroll:
                case Kind.Blip: return 0;
                case Kind.Click: return 1;
                case Kind.Select:
                case Kind.Tab: return 2;
                case Kind.Confirm:
                case Kind.Deny: return 3;
                case Kind.Win:
                case Kind.Crash: return 4;
                default: return -1;
            }
        }

        private static string ClipName(Kind k)
        {
            switch (k)
            {
                case Kind.Type: return "market_ui_type";
                case Kind.Click: return "market_ui_click";
                case Kind.Select: return "market_ui_select";
                case Kind.Tab: return "market_ui_tab";
                case Kind.Confirm: return "market_ui_confirm";
                case Kind.Deny: return "market_ui_deny";
                case Kind.Win: return "market_ui_win";
                case Kind.Crash: return "market_ui_crash";
                case Kind.Scroll: return "market_ui_scroll";
                case Kind.Blip: return "market_ui_blip";
                default: return null;
            }
        }

        private static AudioSource ResolveSource()
        {
            if (_source != null)
            {
                RefreshMixerRouting();
                return _source;
            }
            var go = new GameObject("[MarketUISfx]");
            Object.DontDestroyOnLoad(go);
            _source = go.AddComponent<AudioSource>();
            _source.loop = false;
            _source.playOnAwake = false;
            RefreshMixerRouting();
            return _source;
        }

        /// <summary>SFX 믹서 그룹으로 라우팅 — 옵션의 SFX 볼륨 슬라이더가 그대로 적용된다.</summary>
        private static void RefreshMixerRouting()
        {
            if (_source.outputAudioMixerGroup == null && SoundManager.Instance != null)
                _source.outputAudioMixerGroup = SoundManager.Instance.sfxGroup;
        }

        // ===================================================
        // 물리 마우스 클릭음 (MarketScene 전용 — "모니터 안에서 딸깍" 촉감)
        //   위젯 사운드(_source)와 별도 소스로 재생 → 디덥/Stop 영향 없이 자연스럽게 겹친다.
        //   씬 컴포넌트 MarketMouseClickSfx가 마우스를 뗄 때 1회 호출한다.
        // ===================================================
        private static AudioSource _mouseSource;
        private static AudioClip _mouseUpClip;

        /// <summary>마우스 물리 클릭음(단일, 뗄 때 1회). MarketSfxSet.mouseUp → SoundDataSO → 합성음 순.</summary>
        public static void PlayMouseClick()
        {
            var src = ResolveMouseSource();
            if (src == null) return;

            // 1) 사용자 지정 에셋 클립 우선 (MarketSfxSet.mouseUp)
            var customMouse = _assets != null ? _assets.mouseUp : null;
            if (customMouse != null)
            {
                src.pitch = 1f; // 가져온 에셋은 원음 그대로
                src.PlayOneShot(customMouse, _assets.volume);
                return;
            }

            // 2) SoundDataSO에 "market_mouse_up" 등록 시 교체
            var sm = SoundManager.Instance;
            if (sm != null && sm.HasSFX("market_mouse_up")) { sm.PlaySFX("market_mouse_up"); return; }

            // 3) 합성 노이즈 클릭 — 마우스 "칵" 촉감 (풀 볼륨)
            if (_mouseUpClip == null)
                _mouseUpClip = Synth("market_mouse_up", 0.13f,
                    new Seg { dur = 0.010f, f0 = 250, f1 = 150, noise = 0.80f, decay = 20f, attack = 0.0004f, lp = 0.5f });
            if (_mouseUpClip == null) return;
            src.pitch = Random.Range(0.97f, 1.03f);
            src.PlayOneShot(_mouseUpClip);
        }

        private static AudioSource ResolveMouseSource()
        {
            if (_mouseSource != null)
            {
                if (_mouseSource.outputAudioMixerGroup == null && SoundManager.Instance != null)
                    _mouseSource.outputAudioMixerGroup = SoundManager.Instance.sfxGroup;
                return _mouseSource;
            }
            var ui = ResolveSource(); // [MarketUISfx] 오브젝트 보장(없으면 생성)
            if (ui == null) return null;
            _mouseSource = ui.gameObject.AddComponent<AudioSource>();
            _mouseSource.loop = false;
            _mouseSource.playOnAwake = false;
            if (SoundManager.Instance != null)
                _mouseSource.outputAudioMixerGroup = SoundManager.Instance.sfxGroup;
            return _mouseSource;
        }

        // ===================================================
        // 절차 합성 — 사운드 레시피
        //   UI 클릭(Type/Select/Tab) = 마우스 "찰칵" 노이즈 클릭
        //   거래(Click/Confirm/Deny) = 부드러운 사인 유지 (수량·매수/매도 보호)
        //   코인 결과(Win/Crash) = 레트로 펄스 (극적 피드백 유지)
        // ===================================================
        private static AudioClip Build(Kind kind)
        {
            switch (kind)
            {
                case Kind.Type: // 얇은 마우스 클릭 틱 — 타이핑/코인 드럼롤 (연속 = 찰칵찰칵)
                    return Synth("market_ui_type", 0.10f,
                        new Seg { dur = 0.009f, f0 = 380, f1 = 260, noise = 0.82f, decay = 22f, attack = 0.0004f, lp = 0.5f });

                case Kind.Click: // 짧고 마른 "틱" — 기본 클릭 (수량 조절 등) — 유지
                    return Synth("market_ui_click", 0.11f,
                        new Seg { dur = 0.034f, f0 = 640, f1 = 470, harm = 0.12f, decay = 8f });

                case Kind.Select: // 마우스 "찰칵" 클릭 — 카드/항목 선택 (스크롤은 Kind.Scroll로 분리)
                    return Synth("market_ui_select", 0.14f,
                        new Seg { dur = 0.014f, f0 = 340, f1 = 190, noise = 0.88f, decay = 15f, attack = 0.0004f, lp = 0.55f });

                case Kind.Tab: // 마우스 더블클릭 "찰-칵" — 탭·모드 전환
                    return Synth("market_ui_tab", 0.13f,
                        new Seg { dur = 0.012f, f0 = 320, f1 = 210, noise = 0.85f, decay = 17f, attack = 0.0004f, lp = 0.5f },
                        new Seg { dur = 0.016f, f0 = 240, f1 = 150, noise = 0.85f, decay = 15f, attack = 0.0004f, lp = 0.5f });

                case Kind.Confirm: // G5→D6 완전5도 상승 차임 — 성공
                    return Synth("market_ui_confirm", 0.12f,
                        new Seg { dur = 0.070f, f0 = 784, f1 = 784, harm = 0.20f },
                        new Seg { dur = 0.140f, f0 = 1175, f1 = 1175, harm = 0.20f });

                case Kind.Deny: // E4→C4 하강 2음 — 정중한 거절
                    return Synth("market_ui_deny", 0.13f,
                        new Seg { dur = 0.090f, f0 = 330, f1 = 330, harm = 0.15f },
                        new Seg { dur = 0.130f, f0 = 262, f1 = 262, harm = 0.15f });

                case Kind.Win: // 레트로 상승 아르페지오(C5-E5-G5-C6 펄스) — 레벨업풍 승리/떡상
                    return Synth("market_ui_win", 0.075f,
                        new Seg { dur = 0.055f, f0 = 523, f1 = 523, pulse = 0.5f, decay = 4f },
                        new Seg { dur = 0.055f, f0 = 659, f1 = 659, pulse = 0.5f, decay = 4f },
                        new Seg { dur = 0.055f, f0 = 784, f1 = 784, pulse = 0.5f, decay = 4f },
                        new Seg { dur = 0.150f, f0 = 1047, f1 = 1047, pulse = 0.5f, decay = 3f });

                case Kind.Crash: // 레트로 하강 스윕(게임오버풍 펄스 + 살짝 그릿) — 폭락/상폐
                    return Synth("market_ui_crash", 0.09f,
                        new Seg { dur = 0.380f, f0 = 392, f1 = 98, pulse = 0.5f, noise = 0.04f, decay = 2.5f });

                case Kind.Scroll: // 부드러운 스크롤 리플 "차락" — 휠 연속 시 "차라라락"(클릭보다 부드럽고 어둡게)
                    // LP 노이즈 + 지수 감쇠로 실효 피크가 gain의 ~1/2까지 깎인다.
                    // 0.1 = 사용자가 청감으로 고른 값 — 에셋 클립(volume=1)보다 확실히 아래.
                    return Synth("market_ui_scroll", 0.1f,
                        new Seg { dur = 0.030f, f0 = 420, f1 = 300, noise = 0.70f, decay = 9f, attack = 0.001f, lp = 0.4f });

                case Kind.Blip: // 코인 드럼롤 "띠" — 모니터 심박음풍 단음 비프 (가속 드럼롤 — 재생 피치는 호출부가 올린다)
                    // gain이 큰 이유: 지수 감쇠 보상 — MarketSfxSet 에셋 클립(volume=1)과 체감 크기 맞춤
                    return Synth("market_ui_blip", 0.4f,
                        new Seg { dur = 0.09f, f0 = 1047, f1 = 1047, harm = 0.15f, decay = 3.5f });

                default:
                    return null;
            }
        }

        // ===================================================
        // 절차 합성 — 엔진
        // ===================================================
        private struct Seg
        {
            public float dur;    // 길이(초)
            public float f0, f1; // 시작/끝 주파수(Hz) — 다르면 스윕
            public float harm;   // 2배음 혼합(0~0.5) — 살짝 밝고 따뜻한 질감 (사인 전용)
            public float noise;  // 노이즈 혼합(0~1) — 거친 질감용
            public float decay;  // 감쇠 속도(0=기본6) — 작을수록 길게 지속, 클수록 딱 끊김
            public float pulse;  // 0=사인 / >0=펄스(구형)파 듀티(0.5=사각, 0.25=얇은 리드) — 레트로 게임보이 톤
            public float attack; // 어택(초, 0=기본3ms) — 마우스 클릭처럼 스냅이 필요하면 0.0005 등으로 짧게
            public float lp;     // 노이즈 저역통과 계수(0=기본0.25≈2kHz) — 높일수록 밝고 또렷한 "찰칵"
        }

        private const int SampleRate = 44100;

        private static AudioClip Synth(string name, float gain, params Seg[] segs)
        {
            int total = 0;
            foreach (var s in segs) total += Mathf.CeilToInt(s.dur * SampleRate);
            if (total <= 0) return null;

            var data = new float[total];
            var rng = new System.Random(name.GetHashCode()); // 결정적 노이즈 — 실행마다 동일한 소리

            const float TwoPi = Mathf.PI * 2f;
            float phase = 0f;    // 세그먼트 사이 위상 연속 — 이음새 클릭 방지
            float lpNoise = 0f;  // 노이즈용 1-pole 저역통과 상태 — 날카로운 히스("스") 대신 부드러운 "삭"
            float lpPulse = 0f;  // 펄스용 1-pole 저역통과 상태 — 아이스픽 고조파만 깎아 덜 거슬리게
            int w = 0;
            foreach (var s in segs)
            {
                int n = Mathf.CeilToInt(s.dur * SampleRate);
                int attack = Mathf.Min(n, Mathf.CeilToInt((s.attack > 0f ? s.attack : 0.003f) * SampleRate)); // 어택(기본 3ms — 팝 방지)
                float dk = s.decay > 0f ? s.decay : 6f; // 감쇠 속도(미지정=6)
                for (int i = 0; i < n && w < total; i++, w++)
                {
                    float t01 = (float)i / n;
                    float freq = Mathf.Lerp(s.f0, s.f1, t01);
                    phase += freq / SampleRate;

                    float v;
                    if (s.pulse > 0f)
                    {
                        // 펄스(구형)파 — 게임보이 채널 톤. 듀티(pulse)로 두께 조절
                        float ph = phase - Mathf.Floor(phase); // 0~1 위상
                        v = ph < s.pulse ? 1f : -1f;
                    }
                    else
                    {
                        v = Mathf.Sin(phase * TwoPi);
                        if (s.harm > 0f)
                            v = (v + s.harm * Mathf.Sin(phase * TwoPi * 2f)) / (1f + s.harm);
                    }
                    if (s.noise > 0f)
                    {
                        // 노이즈 저역통과 — 낮으면 부드러운 "삭", 높이면 또렷한 "찰칵"
                        float wn = (float)(rng.NextDouble() * 2.0 - 1.0);
                        lpNoise += (s.lp > 0f ? s.lp : 0.25f) * (wn - lpNoise);
                        v = Mathf.Lerp(v, lpNoise, s.noise);
                    }
                    if (s.pulse > 0f)
                    {
                        // ~4.5kHz 저역통과 — 구형파의 가장 날카로운 고조파만 둥글려 귀에 덜 거슬리게
                        lpPulse += 0.45f * (v - lpPulse);
                        v = lpPulse;
                    }

                    // 반코사인 어택 + 지수 감쇠 — 종/마림바처럼 자연스러운 여운
                    float atk = i < attack ? 0.5f - 0.5f * Mathf.Cos(Mathf.PI * i / attack) : 1f;
                    float env = atk * Mathf.Exp(-dk * t01);
                    data[w] = v * env * gain;
                }
            }

            var clip = AudioClip.Create(name, total, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
