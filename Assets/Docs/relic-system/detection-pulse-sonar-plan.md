# 탐지파동 소나 + 미니맵 발견 알림 구현 계획

> **에이전트 작업자용:** 태스크 단위 구현. 각 태스크는 독립 컴파일 가능 단위.
> **이 프로젝트는 UVCS를 쓰므로 git 커밋 스텝이 없다.** Unity 컴파일 / 플레이 모드 확인은 **사용자가 직접** 수행한다.

**Goal:** 탐지파동 유물의 소나 링을 레벨 무관 고정 속도로 바꾸고, 발견한 특수청크를 발견 순간 3초간 미니맵에서 "띵" 소리와 함께 방향 표시한다.

**Architecture:** `DetectionPulseRelic.PulseVFX`의 기존 핑 발사 지점이 유일한 순차 트리거다. 여기서 ① 소리를 직접 재생하고(`DetectionPingSfx`) ② `DetectedChunkStore.OnPinged`를 발행한다. `UndergroundMinimap`이 이벤트를 받아 좌표별 3초 타이머를 켜고, 시야 안이면 기존 영구 마커를 펄스시키고 시야 밖이면 원 가장자리에 임시 방향 마커를 띄운다.

**Tech Stack:** Unity C#, `Relic.*` 네임스페이스, 절차적 `AudioClip.Create`, uGUI `Image`/`RectTransform`.

설계 문서: `Assets/Docs/relic-system/detection-pulse-sonar-design.md`

## Global Constraints

- 소나 속도: `pulseSpeed = 35f` (유닛/초). `duration = Mathf.Max(0.3f, maxR / pulseSpeed)`.
- 알림 지속: `AnnounceDuration = 3f`초. 가장자리 클램프 반경 `radiusPx * 0.92f`.
- 사운드 겹침 방지 3중: 단일 보이스 재리트리거(`PlayOneShot` 금지) / 최소 간격 `0.12f`초 / 볼륨 `Mathf.Max(0.35f, 1f - i * 0.12f)`.
- 피치: `1f + i * 0.06f`, 상한 `1.5f`. 파동마다 `BeginSequence()`로 순번 리셋.
- 사운드는 **반드시** `SoundManager.Instance.sfxGroup`으로 라우팅한다. `SoundManager.Instance == null`이면 조용히 스킵.
- SFX 오버라이드 키: `"relic_detect_ping"`.
- `Report()`(데이터)와 `OnPinged`(연출)는 분리 유지 — 설계 §3의 "결정" 참조. `Report` 호출 시점을 옮기지 않는다.
- 이 작업은 연출·UI·오디오라 자동 테스트 대상이 아니다. 각 태스크는 **사용자 플레이 모드 확인**으로 끝난다.

---

### Task 1: 소나 고정 속도 전환

`DetectionPulseRelic`의 `pulseDuration`(고정 시간)을 `pulseSpeed`(고정 속도)로 대체한다. 레벨이 올라 반경이 커져도 링 속도가 일정해진다.

**Files:**
- Modify: `Assets/Scripts/Gameplay/Relics/Behaviours/DetectionPulseRelic.cs`

**Interfaces:**
- Produces: `PulseVFX(Vector2 center, float maxR, List<Vector2> pings)`의 내부 지속시간이 `maxR / pulseSpeed`로 계산됨. 시그니처 변경 없음.

- [ ] **Step 1: 필드 교체 (16행)**

`[SerializeField] private float pulseDuration = 0.8f;` 를 아래로 교체:

```csharp
        // 소나 확산 속도(유닛/초). 고정 시간이 아니라 고정 속도라서
        // 레벨이 올라 반경이 커져도 링이 퍼지는 체감 속도가 같다.
        [SerializeField] private float pulseSpeed = 35f;
```

- [ ] **Step 2: `PulseVFX` 진입부에 지속시간 계산 추가 (70행 메서드)**

`private IEnumerator PulseVFX(...)` 본문 첫 줄에 추가:

```csharp
        private IEnumerator PulseVFX(Vector2 center, float maxR, List<Vector2> pings)
        {
            // 고정 속도 → 반경에 비례한 지속시간. 하한은 maxR이 비정상적으로 작을 때의 방어.
            float duration = Mathf.Max(0.3f, maxR / Mathf.Max(1f, pulseSpeed));

            // 확장 링
            var ringGo = new GameObject("RelicDetectionRing");
```

- [ ] **Step 3: 루프의 `pulseDuration` 참조 2곳을 `duration`으로 교체 (99·101행)**

```csharp
            float t = 0f;
            while (t < duration)
            {
                float k = t / duration;
```

- [ ] **Step 4: 사용자 확인**

Unity 컴파일 후 플레이 모드에서:
- 레벨 1과 레벨 3 탐지파동을 각각 발동
- **기대:** 링이 퍼지는 속도가 두 레벨에서 눈으로 같아 보인다. 레벨 3은 더 멀리, 더 오래 퍼진다(약 1.7초).
- `pulseSpeed`가 인스펙터에 노출되어 조절 가능하다.

---

### Task 2: `DetectedChunkStore`에 연출 이벤트 추가

파동이 좌표를 훑은 순간을 알리는 채널. 데이터 변경 이벤트(`OnChanged`)와 분리한다.

**Files:**
- Modify: `Assets/Scripts/Systems/Compass/DetectedChunkStore.cs`

**Interfaces:**
- Produces:
  - `public event Action<Vector2Int> DetectedChunkStore.OnPinged` — 파동 링이 해당 좌표를 통과한 순간 1회 발행
  - `public void DetectedChunkStore.RaisePinged(Vector2Int coord)` — 발행 트리거

- [ ] **Step 1: 이벤트와 발행 메서드 추가**

`public event Action OnChanged;`(41행) 아래에 추가:

```csharp
    /// <summary>발견 데이터 변경 시 발행. 뷰가 마커를 갱신한다.</summary>
    public event Action OnChanged;

    /// <summary>
    /// 파동 링이 이 좌표를 훑고 지나간 순간 발행되는 순수 연출 신호.
    /// OnChanged와 의도적으로 분리한다 — OnChanged는 세이브 로드(Apply)에서도
    /// 발행되므로, 여기에 연출을 붙이면 게임 시작 시 전부 한꺼번에 울린다.
    /// </summary>
    public event Action<Vector2Int> OnPinged;

    /// <summary>탐지 유물 전용. 데이터는 건드리지 않고 연출 신호만 쏜다.</summary>
    public void RaisePinged(Vector2Int coord) => OnPinged?.Invoke(coord);
```

- [ ] **Step 2: 사용자 확인**

Unity 컴파일만 통과하면 된다(아직 호출자·구독자 없음).
- **기대:** 컴파일 에러 없음. 기존 미니맵·전체지도 동작 변화 없음.

---

### Task 3: `DetectionPingSfx` — 절차적 "띵" 재생기

에셋 없이 사인파 차임을 합성해 재생한다. 겹쳐서 커지는 상황을 구조적으로 차단한다.

**Files:**
- Create: `Assets/Scripts/Gameplay/Relics/Behaviours/DetectionPingSfx.cs`
- Modify: `Assets/Scripts/_Core/Managers/SoundManager.cs:133` (`GetSFX` 추가)

**Interfaces:**
- Produces:
  - `public AudioClip SoundManager.GetSFX(string soundName)` — 등록된 SFX 클립 반환, 없으면 null
  - `public static void DetectionPingSfx.BeginSequence()` — 파동 시작 시 호출. 순번을 0으로 리셋
  - `public static void DetectionPingSfx.PlayNext()` — 핑 1회 재생. 순번을 1 증가시키고 피치↑·볼륨↓ 적용

- [ ] **Step 1: 파일 생성**

```csharp
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
        private const string OverrideKey = "relic_detect_ping";
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
```

- [ ] **Step 2: `SoundManager.GetSFX` 추가**

`SoundManager`에는 클립을 **꺼내오는** API가 없다(`PlaySFX`는 내부 소스로 재생만 한다). 커스텀 AudioSource로 재생하려면 필요하다.

`Assets/Scripts/_Core/Managers/SoundManager.cs`의 `HasSFX`(132~133행) 아래에 추가:

```csharp
    /// <summary>등록된 SFX 클립을 반환한다. 없으면 null. (커스텀 AudioSource로 재생할 때 사용)</summary>
    public AudioClip GetSFX(string soundName)
        => sfxClipDict != null && soundName != null && sfxClipDict.TryGetValue(soundName, out var c) ? c : null;
```

- [ ] **Step 3: 사용자 확인**

Unity 컴파일 통과 확인(아직 호출자 없음).
- **기대:** 컴파일 에러 없음.

---

### Task 4: 파동에 소리·이벤트 연결

Task 1~3을 실제로 이어붙인다. 이 태스크가 끝나면 **소리는 완성**된다(미니맵 없이도 동작).

**Files:**
- Modify: `Assets/Scripts/Gameplay/Relics/Behaviours/DetectionPulseRelic.cs`

**Interfaces:**
- Consumes: `DetectionPingSfx.BeginSequence()`, `DetectionPingSfx.PlayNext()` (Task 3), `DetectedChunkStore.RaisePinged(Vector2Int)` (Task 2)

- [ ] **Step 1: 핑 좌표와 청크 좌표를 같은 순서로 들고 가도록 버퍼 추가**

현재 `pings`는 `List<Vector2>`(월드 좌표)라서 어느 청크인지 역추적할 수 없다. `OnActivate`의 VFX 블록(49~60행)을 아래로 교체:

```csharp
            // 파동 VFX (월드 링 + 발견 청크 핑). 순수 연출.
            if (ctx.runner != null)
            {
                float worldRadius = radius * ChunkCoords.WorldSize;
                var pings = new List<Vector2>(_buffer.Count);
                var pingCoords = new List<Vector2Int>(_buffer.Count);
                foreach (var hit in _buffer)
                {
                    Vector2 c = ChunkCenterWorld(hit.coord);
                    pings.Add(c);
                    pingCoords.Add(hit.coord);
                    worldRadius = Mathf.Max(worldRadius, Vector2.Distance(ctx.player.position, c));
                }
                ctx.runner.StartCoroutine(PulseVFX(ctx.player.position, worldRadius, pings, pingCoords));
            }
```

- [ ] **Step 2: `PulseVFX` 시그니처에 좌표 리스트 추가 + 순번 리셋**

```csharp
        private IEnumerator PulseVFX(Vector2 center, float maxR, List<Vector2> pings,
                                     List<Vector2Int> pingCoords)
        {
            // 고정 속도 → 반경에 비례한 지속시간. 하한은 maxR이 비정상적으로 작을 때의 방어.
            float duration = Mathf.Max(0.3f, maxR / Mathf.Max(1f, pulseSpeed));

            // 파동마다 순번 리셋 — 안 하면 두 번째 발동부터 항상 최고음에서 시작한다.
            DetectionPingSfx.BeginSequence();
```

- [ ] **Step 3: 핑 발사 지점에 소리·이벤트 추가 (기존 112~114행 분기)**

`pingFired[i] = true;` 바로 아래에 2줄 추가:

```csharp
                for (int i = 0; i < pings.Count; i++)
                {
                    if (pingFired[i] || r < pingDist[i]) continue;
                    pingFired[i] = true;

                    // 링이 이 청크를 훑은 순간 = 순차 알림 트리거.
                    // 소리는 relic이 직접 낸다(미니맵이 꺼져 있어도 들려야 하므로).
                    DetectionPingSfx.PlayNext();
                    DetectedChunkStore.Instance.RaisePinged(pingCoords[i]);

                    var go = new GameObject("RelicDetectionPing");
```

- [ ] **Step 4: 사용자 확인**

플레이 모드에서:
- 특수청크가 2개 이상 잡히는 지점에서 탐지파동 발동
- **기대:** 가까운 청크부터 순서대로 "띵 딩 딩↗" 소리가 난다. 링이 그 위치를 지나는 순간과 소리가 일치한다.
- **기대:** 여러 개가 동시에 잡혀도 소리가 겹쳐서 갑자기 커지지 않는다.
- 설정에서 SFX 볼륨을 0으로 → **"띵"도 같이 꺼진다.**
- 발동을 2회 연속 → **두 번째도 낮은 음에서 시작한다.**

---

### Task 5: 미니맵 알림 상태 + 구독 라이프사이클

이벤트를 받아 좌표별 3초 타이머를 관리한다. 표시는 Task 6에서 붙인다.

**Files:**
- Modify: `Assets/Scripts/UI/Player/UndergroundMinimap.cs`

**Interfaces:**
- Consumes: `DetectedChunkStore.OnPinged` (Task 2)
- Produces:
  - `private Dictionary<Vector2Int, float> _announceUntil` — 좌표별 알림 만료 시각
  - `private bool IsAnnouncing(Vector2Int coord, out float pulse01)` — 알림 중이면 true, `pulse01`은 0~1 펄스값

- [ ] **Step 1: 필드 추가**

`private Sprite _detectSprite;`(110행) 아래에 추가:

```csharp
    private Sprite _detectSprite;

    // 탐지파동 발견 알림 — 좌표별 만료 시각. 링이 훑은 순간부터 3초간 반짝인다.
    private const float AnnounceDuration = 3f;
    private readonly System.Collections.Generic.Dictionary<Vector2Int, float> _announceUntil
        = new System.Collections.Generic.Dictionary<Vector2Int, float>();
    // 만료 키 수거용 재사용 버퍼. foreach 도중 Remove하면 InvalidOperationException이 나므로
    // 순회 후 일괄 제거한다. 매 프레임 도는 경로라 GC를 피해 필드로 캐시한다.
    private readonly System.Collections.Generic.List<Vector2Int> _announceExpired
        = new System.Collections.Generic.List<Vector2Int>();
```

- [ ] **Step 2: 구독/해제 추가**

`void Awake()`(123행) 위에 추가:

```csharp
    // DetectedChunkStore는 앱 수명 싱글톤이다. 해제하지 않으면 씬 전환 후
    // 파괴된 미니맵이 이벤트에 매달려 MissingReferenceException을 던진다.
    void OnEnable()  => DetectedChunkStore.Instance.OnPinged += OnDetectPinged;
    void OnDisable() => DetectedChunkStore.Instance.OnPinged -= OnDetectPinged;

    private void OnDetectPinged(Vector2Int coord)
    {
        _announceUntil[coord] = Time.time + AnnounceDuration;
    }
```

- [ ] **Step 3: 판정 헬퍼 추가**

`private RectTransform GetOrCreateDetectMarker(int i)`(404행) 위에 추가:

```csharp
    /// <summary>
    /// 알림 중이면 true. pulse01은 0~1 사이를 오가는 반짝임 계수(끝날수록 약해진다).
    /// </summary>
    private bool IsAnnouncing(Vector2Int coord, out float pulse01)
    {
        pulse01 = 0f;
        if (!_announceUntil.TryGetValue(coord, out float until)) return false;

        float remain = until - Time.time;
        if (remain <= 0f) { _announceExpired.Add(coord); return false; }

        float blink = 0.5f + 0.5f * Mathf.Sin(Time.time * 14f);  // 초당 ~2.2회
        pulse01 = blink * Mathf.Clamp01(remain / AnnounceDuration); // 끝으로 갈수록 감쇠
        return true;
    }

    /// <summary>만료 키 일괄 제거. 마커 순회가 끝난 뒤 반드시 호출한다.</summary>
    private void FlushExpiredAnnounces()
    {
        if (_announceExpired.Count == 0) return;
        foreach (var c in _announceExpired) _announceUntil.Remove(c);
        _announceExpired.Clear();
    }
```

- [ ] **Step 4: 사용자 확인**

컴파일 통과 확인. 플레이 모드에서 탐지파동 발동.
- **기대:** 컴파일 에러 없음. 미니맵 표시는 아직 기존과 동일(변화 없음). 예외 없음.
- 발동 직후 다른 씬(마켓 등) 진입 → **예외가 나지 않는다.**

---

### Task 6: 미니맵 반짝임 + 가장자리 방향 마커

알림 상태를 실제 표시로 연결한다. 이 태스크가 끝나면 기능 완성이다.

**Files:**
- Modify: `Assets/Scripts/UI/Player/UndergroundMinimap.cs:368-418`

**Interfaces:**
- Consumes: `IsAnnouncing(Vector2Int, out float)`, `FlushExpiredAnnounces()`, `_announceExpired` (Task 5)

- [ ] **Step 1: 가장자리 마커 풀 필드 추가**

Task 5에서 추가한 `_announceExpired` 아래에 추가:

```csharp
    // 시야 밖 발견 알림용 임시 마커 풀(영구 마커와 분리).
    private readonly System.Collections.Generic.List<RectTransform> _edgeMarkers
        = new System.Collections.Generic.List<RectTransform>();
```

- [ ] **Step 2: `UpdateDetectedMarkers` 본문 교체 (384~401행)**

`int idx = 0;`부터 `_detectMarkers[i].gameObject.SetActive(false);`까지를 아래로 교체:

```csharp
        int idx = 0;
        int edgeIdx = 0;
        foreach (var kv in all)
        {
            Vector3 w = ChunkCoords.ToWorld(kv.Key);
            Vector2 world = new Vector2(w.x + half, w.y + half);
            Vector2 local = (world - pc0) * PX_PER_WORLD;

            // 시야 안/밖 판정은 매 프레임 재계산한다. 알림 3초 동안 플레이어가 움직이면
            // 가장자리 마커 <-> 영구 마커로 자연스럽게 전환된다.
            bool announcing = IsAnnouncing(kv.Key, out float pulse);

            if (local.magnitude > radiusPx)
            {
                // 시야 밖: 알림 중일 때만 가장자리에 방향 마커를 띄운다.
                if (!announcing) continue;

                RectTransform ert = GetOrCreateEdgeMarker(edgeIdx++);
                ert.gameObject.SetActive(true);
                ert.anchoredPosition = local.normalized * (radiusPx * 0.92f);
                ert.localScale = Vector3.one * (1f + pulse * 0.6f);
                var eimg = ert.GetComponent<Image>();
                eimg.color = new Color(1f, 0.85f, 0.35f, 0.35f + pulse * 0.65f);
                continue;
            }

            RectTransform rt = GetOrCreateDetectMarker(idx++);
            rt.gameObject.SetActive(true);
            rt.anchoredPosition = local;

            // 스케일은 매 프레임 명시적으로 세팅한다. 이 풀은 dictionary 순회 순서대로
            // 인덱스가 배정되므로 같은 오브젝트가 프레임마다 다른 좌표를 담당할 수 있다.
            // 리셋을 빠뜨리면 알림이 끝난 뒤 그 오브젝트를 물려받은 마커가 부푼 채로 남는다.
            rt.localScale = announcing ? Vector3.one * (1f + pulse * 0.6f) : Vector3.one;

            var img = rt.GetComponent<Image>();
            if (announcing)
                img.color = new Color(1f, 0.95f, 0.6f, 0.5f + pulse * 0.5f);
            else
                img.color = kv.Value.visited
                    ? new Color(0.7f, 0.7f, 0.7f, 0.5f)
                    : new Color(1f, 0.85f, 0.35f, 1f);
        }

        for (int i = idx; i < _detectMarkers.Count; i++)
            _detectMarkers[i].gameObject.SetActive(false);
        for (int i = edgeIdx; i < _edgeMarkers.Count; i++)
            _edgeMarkers[i].gameObject.SetActive(false);

        FlushExpiredAnnounces();
```

- [ ] **Step 3: 가장자리 마커 생성 메서드 추가**

`GetOrCreateDetectMarker`(404행) 아래에 추가:

```csharp
    private RectTransform GetOrCreateEdgeMarker(int i)
    {
        if (i < _edgeMarkers.Count) return _edgeMarkers[i];
        var marker = new GameObject($"MiniEdgeDetect{i}").AddComponent<Image>();
        marker.transform.SetParent(_root, false);
        marker.sprite = _detectSprite;
        marker.raycastTarget = false;
        var rt = marker.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(12f, 12f);
        _generated.Add(marker);   // 미니맵 재빌드 시 함께 정리되도록 필수
        _edgeMarkers.Add(rt);
        return rt;
    }
```

- [ ] **Step 4: 사용자 확인 (전체 기능)**

플레이 모드에서 설계 §7 검증 항목 전체를 확인한다:

1. 레벨 1 / 레벨 3 발동 → **링 확산 속도가 눈으로 같아 보인다**
2. 특수청크 여러 개가 잡히는 지점에서 발동 → **소리가 순차로 나고, 겹쳐 커지지 않는다**
3. **미니맵 시야 밖** 특수청크 발견 → **원 가장자리에 방향 마커가 3초 뜨고 사라진다**
4. 알림 중 그 방향으로 이동 → 가장자리 마커가 영구 마커로 전환된다
5. SFX 볼륨 0 → "띵"이 같이 꺼진다
6. 알림 직후 다른 씬 진입 → 예외 없음
7. 발동 2회 연속 → 두 번째도 낮은 음부터 시작
8. **알림이 끝난 뒤** 미니맵 마커들이 **정상 크기로 돌아온다**(스케일 잔류 없음)

---

## 태스크 의존 관계

```
Task 1 (고정 속도) ─┐
Task 2 (OnPinged) ──┼─→ Task 4 (연결: 소리 완성)
Task 3 (SFX) ───────┘
Task 2 ─→ Task 5 (미니맵 상태) ─→ Task 6 (미니맵 표시: 기능 완성)
```

Task 1·2·3은 서로 독립이라 순서를 바꿔도 된다. Task 4는 1·2·3 이후, Task 6은 5 이후.
