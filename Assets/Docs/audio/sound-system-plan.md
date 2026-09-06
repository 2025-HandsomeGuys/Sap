# 사운드 시스템 구현 계획

> **에이전트 작업자용:** 이 계획은 태스크 단위로 실행한다. 스텝은 체크박스(`- [ ]`)로 추적한다.
> 설계 근거는 `Assets/Docs/audio/sound-system-design.md`를 먼저 읽을 것.

**목표:** 핵심 게임플레이 효과음 35종을 넣기 위한 오디오 인프라를 구축하고, 모든 훅을
코드에 삽입한다. 클립은 나중에 채운다.

**아키텍처:** 기존 `SoundManager` 싱글톤을 확장한다 — SFX 원샷 풀(10개), 3D 재생, 앰비언스
2레이어 크로스페이드, 상태 루프 핸들. 순수 로직(스로틀·발소리 간격·앰비언스 선택)은
MonoBehaviour에서 분리해 EditMode 테스트가 가능하게 만든다. 훅은 전부 `HasSFX` 가드를
통과하므로 클립이 없으면 무음이다.

**기술 스택:** Unity 2D, C#, `UnityEngine.Audio.AudioMixer`, NUnit(EditMode)

---

## 전역 제약

이 절의 규칙은 **모든 태스크에 암묵적으로 포함된다.**

- **git 명령을 쓰지 않는다.** 이 프로젝트는 UVCS를 쓴다. `git add`/`git commit`/`git diff`
  전부 금지. 계획의 "체크인 지점"은 사용자가 UVCS로 직접 처리한다.
- **Unity 테스트를 실행하지 않는다.** 테스트 파일 작성·수정만 한다. `mcp__mcp-unity__run_tests`
  등 테스트 실행 도구를 호출하지 않는다. 실행은 사용자가 Test Runner에서 직접 한다.
- **파일 검색은 `@tags:` 우선.** 새로 만드는 `.cs` 파일 첫 줄에 `// @tags: ...`를 넣는다.
- **기존 공개 API 시그니처를 바꾸지 않는다.** `SoundManager`의 `PlayBGM(string)`,
  `PlaySFX(string)`, `PlaySound(string)`, `HasSFX(string)`, `GetSFX(string)`, `StopBGM()`,
  볼륨 6종은 그대로 유지한다. `MarketUISfx`, `CoinSfx`, `CodeUIKit.PlaySfx`,
  `WorldMapOverlay`, `SettingsOverlayUI`가 이미 호출하고 있다.
- **모든 재생은 `HasSFX` 가드를 통과한다.** 클립 미등록 시 무음이고 경고를 찍지 않는다.
  `Debug.LogWarning`으로 스팸을 내지 않는다.
- **EditMode 테스트 위치:** `Assets/Tests/EditMode/`. 어셈블리는 기존 `EditModeTests.asmdef`
  를 그대로 쓴다(이미 `GameScripts`를 참조한다). asmdef를 수정하지 않는다.
- **키 문자열은 반드시 `SfxKeys` 상수를 통해서만 쓴다.** 리터럴 직접 사용 금지.
  예외: `MarketUISfx`/`CoinSfx`의 기존 자체 상수는 그대로 둔다.

---

## 파일 구조

### 신규

| 파일 | 책임 |
|---|---|
| `Assets/Scripts/_Core/Managers/SfxKeys.cs` | 사운드 키 상수 전체. 로직 없음 |
| `Assets/Scripts/Audio/SfxThrottle.cs` | 같은 키 연타 억제. 순수 C#, 시간 주입 |
| `Assets/Scripts/Audio/FootstepTiming.cs` | 속도 → 스텝 간격 계산. 순수 static |
| `Assets/Scripts/Audio/AmbienceSelector.cs` | (지상여부, 시간대, 층) → 앰비언스 2키. 순수 static |
| `Assets/Scripts/Audio/AmbienceDirector.cs` | `AmbienceSelector` 결과를 `SoundManager`에 반영하는 MonoBehaviour |
| `Assets/Scripts/Audio/FootstepPlayer.cs` | 발소리 재생 MonoBehaviour |
| `Assets/Scripts/Audio/HeartbeatSfx.cs` | 스태미나 심장박동 루프 MonoBehaviour |
| `Assets/Scripts/Editor/SfxFolderImporter.cs` | 폴더 스캔 → `SoundData.asset` 일괄 등록 |
| `Assets/Tests/EditMode/SfxThrottleTests.cs` | `SfxThrottle` 테스트 |
| `Assets/Tests/EditMode/FootstepTimingTests.cs` | `FootstepTiming` 테스트 |
| `Assets/Tests/EditMode/AmbienceSelectorTests.cs` | `AmbienceSelector` 테스트 |

### 수정

| 파일 | 변경 |
|---|---|
| `Assets/Sound/Master.mixer` | `Ambience` 그룹 추가 (BGM의 자식) — **Unity 에디터에서 수동** |
| `Assets/Scripts/_Core/Managers/SoundManager.cs` | 원샷 풀, 3D 재생, 앰비언스, 상태 루프 |
| `Assets/Scripts/Audio/UpGround/BGMCycleManager.cs` | 믹서 그룹 누락 수정, `maxVolume` 제거 |
| 훅 대상 18개 파일 | 각 1~3줄 삽입 (Task 10~13) |

---

## Task 1: 믹서 Ambience 그룹 + BGMCycleManager 버그 수정

**Files:**
- Modify: `Assets/Sound/Master.mixer` (Unity 에디터 수동 작업)
- Modify: `Assets/Scripts/Audio/UpGround/BGMCycleManager.cs`

**Interfaces:**
- Produces: 믹서에 `Ambience` `AudioMixerGroup`이 존재한다. Task 4가 `SoundManager`의
  인스펙터 필드 `ambienceGroup`에 이것을 연결한다.

- [ ] **Step 1: 믹서에 Ambience 그룹 추가 (사용자 수동 작업)**

이 스텝은 코드로 할 수 없다. `.mixer`는 YAML이지만 손으로 편집하면 GUID 참조가 깨진다.
사용자에게 다음을 요청한다:

```
1. Assets/Sound/Master.mixer 더블클릭 → Audio Mixer 창
2. Groups 패널에서 BGM 우클릭 → "Add child group" → 이름 "Ambience"
3. 저장 (Ctrl+S)
```

`Ambience`에는 노출 파라미터를 만들지 않는다. BGM 그룹의 자식이므로 `BGMVolume`이
자동으로 함께 적용된다.

⚠ **BGM 그룹의 Volume은 건드리지 않는다.** 그 값은 노출 파라미터 `BGMVolume` 그 자체라
게임 시작 시 `SoundManager.LoadVolumes()`가 사용자 설정값으로 덮어쓴다. 정적 트림을
넣을 자리가 아니다 — 그건 `BGMCycleManager.maxVolume`이 담당한다.

- [ ] **Step 2: `BGMCycleManager` 수정**

`Assets/Scripts/Audio/UpGround/BGMCycleManager.cs`의 `SetupAudioSources()`를 교체한다.
현재 `secondarySource`가 `AddComponent`로 생성돼 `outputAudioMixerGroup`이 `null`이다
→ 크로스페이드 후반 절반이 BGM 볼륨 슬라이더를 무시한다.

```csharp
    private void SetupAudioSources()
    {
        // 첫 번째 AudioSource (기존에 달려있는 컴포넌트 사용)
        primarySource = GetComponent<AudioSource>();
        primarySource.loop = true;
        primarySource.playOnAwake = false;
        primarySource.volume = 1f;

        // 두 번째 AudioSource (같은 오브젝트에 추가하여 필터 공유)
        secondarySource = gameObject.AddComponent<AudioSource>();
        secondarySource.loop = true;
        secondarySource.playOnAwake = false;
        secondarySource.volume = 0f;

        // AddComponent로 만든 소스는 믹서 그룹이 비어 있다 → 그대로 두면 크로스페이드
        // 후반 절반이 BGM 볼륨 슬라이더를 무시한다. primary의 그룹을 그대로 복사한다.
        secondarySource.outputAudioMixerGroup = primarySource.outputAudioMixerGroup;
    }
```

- [ ] **Step 3: `maxVolume`은 그대로 둔다 (계획 수정됨)**

당초 이 필드를 제거하고 "볼륨은 믹서에 맡긴다"로 갈 계획이었으나 **취소했다.**
믹서의 BGM 그룹 볼륨은 노출 파라미터 `BGMVolume` 그 자체(guid `b14ce469…`가
BGM 그룹의 `m_Volume`)라, 에디터에서 감쇠를 걸어도 `SoundManager.LoadVolumes()`가
런타임에 덮어쓴다. 정적 트림을 둘 자리가 믹서에 없다.

실제 버그는 Step 2의 믹서 그룹 누락 하나뿐이었다. 필드에 그 이유를 주석으로 남긴다.

```csharp
    // 지상 BGM의 정적 음량 트림.
    // 믹서의 BGM 그룹 볼륨은 노출 파라미터 "BGMVolume" 그 자체라서
    // SoundManager.SetBGMVolume()이 런타임에 계속 덮어쓴다(설정 슬라이더).
    // 따라서 "이 음악은 원래 좀 작게" 같은 정적 감쇠는 믹서가 아니라 여기서 준다.
    [Range(0f, 1f)]
    public float maxVolume = 0.4f;
```

- [ ] **Step 4: 검증 (사용자 확인)**

Unity에서 `UpgroundScene`을 재생한다. 확인할 것:
- 지상 BGM이 들린다
- 설정 오버레이에서 BGM 볼륨을 0으로 내리면 **완전히** 무음이 된다
  (이전에는 크로스페이드 직후 소리가 남아 있었다)
- 볼륨 체감이 이전과 비슷하다. 너무 크면 믹서의 BGM 그룹 게인을 더 내린다

**→ 체크인 지점** (사용자가 UVCS로 처리)

---

## Task 2: SfxKeys 상수

**Files:**
- Create: `Assets/Scripts/_Core/Managers/SfxKeys.cs`

**Interfaces:**
- Produces: `public static class SfxKeys` — 35개 `public const string`. 이후 모든 태스크가
  이 상수만 쓴다.

- [ ] **Step 1: 파일 생성**

```csharp
// @tags: sound, sfx, keys, constants, audio
/// <summary>
/// 효과음 키 상수. SoundDataSO에 등록하는 soundName과 1:1로 대응한다.
///
/// 문자열 리터럴을 흩뿌리면 오타가 무음으로 조용히 넘어가서 디버깅이 어렵다.
/// 재생 호출은 반드시 이 상수를 통해서만 한다.
///
/// 명명 규칙: <도메인>_<동작> snake_case. 기존 market_ui_* / coin_* 와 맞춘다.
/// 클립이 없으면 HasSFX 가드에서 조용히 무음 처리된다 — 키를 먼저 정의하고
/// 음원은 나중에 채워도 된다.
/// </summary>
public static class SfxKeys
{
    // ── 플레이어 ──
    public const string PlayerJump      = "player_jump";
    public const string PlayerHeartbeat = "player_heartbeat"; // 루프
    public const string StepGrass       = "step_grass";
    public const string StepDirt        = "step_dirt";

    // ── 채굴 ──
    public const string DigSwing   = "dig_swing";
    public const string DigHit     = "dig_hit";
    public const string DigBlocked = "dig_blocked";
    public const string RockBreak  = "rock_break";
    public const string IceBreak   = "ice_break";
    public const string Explosion  = "explosion";

    // ── 도구 · 유물 ──
    public const string DrillOn           = "drill_on";
    public const string DrillMotor        = "drill_motor";    // 루프
    public const string PowerDown         = "power_down";
    public const string JetpackThrust     = "jetpack_thrust"; // 루프
    public const string RelicLightning    = "relic_lightning";
    public const string RelicDetectPing   = "relic_detect_ping"; // DetectionPingSfx가 이미 조회
    public const string RelicPortalReturn = "relic_portal_return";
    public const string RelicActivate     = "relic_activate";

    // ── 이동 · 전환 ──
    public const string ElevatorMove = "elevator_move";
    public const string PortalEnter  = "portal_enter";

    // ── 경제 · UI ──
    public const string UiButton      = "ui_button";
    public const string UiTerminalOn  = "ui_terminal_on";
    public const string ShopSell      = "shop_sell";
    public const string ShopBuy       = "shop_buy";
    public const string UpgradeUnlock = "upgrade_unlock";
    public const string CauldronBrew  = "cauldron_brew";
    public const string CoinTick      = "coin_tick"; // CoinSfx.Tick이 이미 조회

    // ── 하루 사이클 · 결과 ──
    public const string SleepSnore       = "sleep_snore";
    public const string MorningRooster   = "morning_rooster";
    public const string DaySummaryProfit = "daysummary_profit";
    public const string DaySummaryLoss   = "daysummary_loss";
    public const string QuestComplete    = "quest_complete";
    public const string MissionSuccess   = "mission_success";
    public const string GameOver         = "game_over";

    // ── 앰비언스 (루프) ──
    public const string AmbSurfaceDayCicada    = "amb_surface_day_cicada";
    public const string AmbSurfaceDayBirds     = "amb_surface_day_birds";
    public const string AmbSurfaceNightInsects = "amb_surface_night_insects";
    public const string AmbLayerWind           = "amb_layer_wind";
    public const string AmbCaveDrip            = "amb_cave_drip";

    /// <summary>
    /// 에디터 툴(SfxFolderImporter)이 "아직 음원이 없는 키" 목록을 뽑을 때 쓴다.
    /// 새 키를 추가하면 여기에도 넣어야 리포트에 잡힌다.
    /// </summary>
    public static readonly string[] All =
    {
        PlayerJump, PlayerHeartbeat, StepGrass, StepDirt,
        DigSwing, DigHit, DigBlocked, RockBreak, IceBreak, Explosion,
        DrillOn, DrillMotor, PowerDown, JetpackThrust,
        RelicLightning, RelicDetectPing, RelicPortalReturn, RelicActivate,
        ElevatorMove, PortalEnter,
        UiButton, UiTerminalOn, ShopSell, ShopBuy, UpgradeUnlock, CauldronBrew, CoinTick,
        SleepSnore, MorningRooster, DaySummaryProfit, DaySummaryLoss,
        QuestComplete, MissionSuccess, GameOver,
        AmbSurfaceDayCicada, AmbSurfaceDayBirds, AmbSurfaceNightInsects,
        AmbLayerWind, AmbCaveDrip,
    };
}
```

- [ ] **Step 2: 컴파일 확인 (사용자)**

Unity로 전환해 컴파일 에러가 없는지 확인한다. 이 파일은 의존성이 없으므로 실패할 이유가
없지만, 이후 태스크가 전부 이 파일에 의존한다.

---

## Task 3: SfxThrottle (순수 로직 + 테스트)

**Files:**
- Create: `Assets/Scripts/Audio/SfxThrottle.cs`
- Test: `Assets/Tests/EditMode/SfxThrottleTests.cs`

**Interfaces:**
- Produces: `SfxThrottle(float minInterval)`, `bool ShouldPlay(string key, float now)`,
  `void Clear()`. Task 5의 `SoundManager`가 인스턴스 하나를 들고 쓴다.

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/EditMode/SfxThrottleTests.cs`:

```csharp
using NUnit.Framework;

public class SfxThrottleTests
{
    [Test]
    public void FirstCall_AlwaysPlays()
    {
        var t = new SfxThrottle(0.04f);
        Assert.IsTrue(t.ShouldPlay("a", 0f));
    }

    [Test]
    public void SameKeyWithinInterval_IsSuppressed()
    {
        var t = new SfxThrottle(0.04f);
        t.ShouldPlay("a", 0f);
        Assert.IsFalse(t.ShouldPlay("a", 0.03f));
    }

    [Test]
    public void SameKeyAfterInterval_PlaysAgain()
    {
        var t = new SfxThrottle(0.04f);
        t.ShouldPlay("a", 0f);
        Assert.IsTrue(t.ShouldPlay("a", 0.05f));
    }

    [Test]
    public void DifferentKeys_DoNotInterfere()
    {
        var t = new SfxThrottle(0.04f);
        t.ShouldPlay("a", 0f);
        Assert.IsTrue(t.ShouldPlay("b", 0.01f));
    }

    [Test]
    public void SuppressedCall_DoesNotExtendWindow()
    {
        // 억제된 호출이 타임스탬프를 갱신하면 연타 중 영원히 막힌다.
        var t = new SfxThrottle(0.04f);
        t.ShouldPlay("a", 0f);
        t.ShouldPlay("a", 0.03f);   // 억제됨
        Assert.IsTrue(t.ShouldPlay("a", 0.045f)); // 최초 재생 기준 0.045초 → 통과해야 한다
    }

    [Test]
    public void Clear_ResetsAllKeys()
    {
        var t = new SfxThrottle(0.04f);
        t.ShouldPlay("a", 0f);
        t.Clear();
        Assert.IsTrue(t.ShouldPlay("a", 0.01f));
    }

    [Test]
    public void NullOrEmptyKey_IsSuppressed()
    {
        var t = new SfxThrottle(0.04f);
        Assert.IsFalse(t.ShouldPlay(null, 0f));
        Assert.IsFalse(t.ShouldPlay("", 0f));
    }
}
```

- [ ] **Step 2: 구현**

`Assets/Scripts/Audio/SfxThrottle.cs`:

```csharp
// @tags: sound, sfx, throttle, audio, pure
using System.Collections.Generic;

/// <summary>
/// 같은 키의 연타를 억제한다.
///
/// 한 프레임에 여러 번 발생하는 이벤트(다중 콜라이더 히트, 빠른 휠 스크롤)가
/// PlayOneShot으로 합산되면 소리가 터진다. MarketUISfx의 스크롤 스로틀과 같은 이유.
///
/// 시간을 주입받는 순수 C# — MonoBehaviour가 아니므로 EditMode에서 테스트한다.
///
/// 불변식: 억제된 호출은 타임스탬프를 갱신하지 않는다. 갱신하면 연타가 이어지는 동안
/// 창이 계속 밀려서 소리가 영원히 안 난다.
/// </summary>
public sealed class SfxThrottle
{
    private readonly Dictionary<string, float> _lastPlayed = new Dictionary<string, float>();
    private readonly float _minInterval;

    public SfxThrottle(float minInterval = 0.04f)
    {
        _minInterval = minInterval;
    }

    /// <summary>재생해도 되면 true를 돌려주고 타임스탬프를 갱신한다.</summary>
    public bool ShouldPlay(string key, float now)
    {
        if (string.IsNullOrEmpty(key)) return false;

        if (_lastPlayed.TryGetValue(key, out float last) && now - last < _minInterval)
            return false; // 억제 — 타임스탬프는 갱신하지 않는다

        _lastPlayed[key] = now;
        return true;
    }

    public void Clear() => _lastPlayed.Clear();
}
```

- [ ] **Step 3: 테스트 실행 (사용자)**

Unity Test Runner > EditMode에서 `SfxThrottleTests` 7개를 실행한다. Claude는 실행하지 않는다.

---

## Task 4: SoundManager — 원샷 풀 + 3D 재생

**Files:**
- Modify: `Assets/Scripts/_Core/Managers/SoundManager.cs`

**Interfaces:**
- Consumes: `SfxThrottle` (Task 3), `SfxKeys` (Task 2)
- Produces:
  - `void PlaySFX(string key)` — 기존 시그니처 유지
  - `void PlaySFX(string key, float pitch, float volume = 1f)`
  - `void PlaySFXAt(string key, Vector3 worldPos, float pitch = 1f, float volume = 1f)`
  - `float PitchJitter { get; set; }` — 기본 0. 호출측이 지터를 원할 때 `0.02f` 지정
  - `void PlaySFXJittered(string key)` — `PitchJitter` 적용 재생 (발소리·곡괭이용)

- [ ] **Step 1: 필드와 초기화 교체**

`SoundManager.cs`의 필드 선언부에서 `private AudioSource sfxSource;`를 지우고 아래로
교체한다. `bgmSource`는 그대로 둔다.

```csharp
    [Header("Mixer Settings")]
    public AudioMixer mainMixer;
    public AudioMixerGroup bgmGroup;
    public AudioMixerGroup sfxGroup;
    public AudioMixerGroup ambienceGroup;   // Task 1에서 만든 그룹을 인스펙터에서 연결

    private AudioSource bgmSource;

    // ── SFX 원샷 풀 ──
    // 소스가 1개면 pitch가 공유 속성이라 마지막 호출이 아직 울리는 이전 소리의 음정까지
    // 바꾼다. 라운드로빈 풀로 소리마다 독립적인 pitch/위치를 준다.
    private const int SfxPoolSize = 10;
    private AudioSource[] _sfxPool;
    private int _sfxCursor;

    private readonly SfxThrottle _throttle = new SfxThrottle(0.04f);

    /// <summary>0이면 지터 없음. 발소리·곡괭이처럼 초당 여러 번 반복되는 소리에만 0.02f 정도를 쓴다.</summary>
    public float PitchJitter { get; set; } = 0.02f;
```

- [ ] **Step 2: `InitializeAudioSources()` 교체**

```csharp
    private void InitializeAudioSources()
    {
        // bgmSource 초기화
        bgmSource = gameObject.AddComponent<AudioSource>();
        bgmSource.outputAudioMixerGroup = bgmGroup;
        bgmSource.loop = true;
        bgmSource.playOnAwake = false;

        // SFX 원샷 풀
        _sfxPool = new AudioSource[SfxPoolSize];
        for (int i = 0; i < SfxPoolSize; i++)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.outputAudioMixerGroup = sfxGroup;
            src.loop = false;
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            _sfxPool[i] = src;
        }
    }
```

- [ ] **Step 3: 재생 API 교체**

기존 `PlaySFX(string)`를 아래 블록 전체로 교체한다. `PlaySound`, `HasSFX`, `GetSFX`는
건드리지 않는다.

```csharp
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
        if (!sfxClipDict.TryGetValue(soundName, out clip) || clip == null) return null;
        if (!_throttle.ShouldPlay(soundName, Time.unscaledTime)) return null;
        if (_sfxPool == null || _sfxPool.Length == 0) return null;

        var src = _sfxPool[_sfxCursor];
        _sfxCursor = (_sfxCursor + 1) % _sfxPool.Length;
        return src;
    }
```

`PlaySFX`에서 기존 `Debug.LogWarning($"SoundManager: SFX not found: {soundName}")`가
사라진 것이 의도다. 훅을 40개 꽂아두고 클립을 나중에 채우는 구조라 경고가 스팸이 된다.

- [ ] **Step 4: 3D 재생을 위한 자식 오브젝트 준비**

`PlaySFXAt`이 `src.transform.position`을 쓰는데, 풀 소스는 전부 `SoundManager`
GameObject에 붙어 있어서 위치를 개별로 못 준다. `InitializeAudioSources()`의 풀 생성
루프를 아래로 교체한다 — 소스마다 자식 GameObject를 만든다.

```csharp
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
```

- [ ] **Step 5: 인스펙터 연결 (사용자)**

`SoundManager`가 붙은 오브젝트를 찾아 `Ambience Group` 필드에 Task 1에서 만든
`Master.mixer > Ambience` 그룹을 드래그한다.

- [ ] **Step 6: 검증 (사용자)**

`Assets/Audio/SFX/` 폴더를 만들고 아무 wav 하나를 `ui_button.wav`로 넣은 뒤,
`SoundData.asset`에 `soundName = ui_button`으로 수동 등록한다. 게임을 켜고 코드 생성
UI(인벤토리·상점 등)의 버튼을 누르면 소리가 나야 한다 — `CodeUIKit.PlaySfx`가 이미
이 경로를 탄다.

**→ 체크인 지점**

---

## Task 5: SoundManager — 앰비언스 2레이어

**Files:**
- Modify: `Assets/Scripts/_Core/Managers/SoundManager.cs`

**Interfaces:**
- Consumes: Task 4의 `ambienceGroup` 필드
- Produces:
  - `enum AmbienceLayer { Primary, Secondary }`
  - `void SetAmbience(AmbienceLayer layer, string key, float fade = 2f)`
  - `void StopAmbience(AmbienceLayer layer, float fade = 2f)`
  - `void StopAllAmbience(float fade = 2f)`

- [ ] **Step 1: 레이어 자료구조 추가**

`SoundManager.cs`의 필드 선언부(풀 아래)에 추가한다.

```csharp
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

        public AudioSource Active   => UsingA ? A : B;
        public AudioSource Inactive => UsingA ? B : A;
    }

    private AmbienceChannel[] _ambience;
```

- [ ] **Step 2: 초기화 추가**

`InitializeAudioSources()` 맨 끝에 이어 붙인다.

```csharp
        // 앰비언스 채널 2개 × 소스 2개
        _ambience = new AmbienceChannel[2];
        for (int i = 0; i < 2; i++)
        {
            _ambience[i] = new AmbienceChannel
            {
                A = CreateAmbienceSource($"Ambience{i}_A"),
                B = CreateAmbienceSource($"Ambience{i}_B"),
                UsingA = true,
            };
        }
    }

    private AudioSource CreateAmbienceSource(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);

        var src = go.AddComponent<AudioSource>();
        src.outputAudioMixerGroup = ambienceGroup != null ? ambienceGroup : bgmGroup;
        src.loop = true;
        src.playOnAwake = false;
        src.spatialBlend = 0f;
        src.volume = 0f;
        return src;
```

(마지막 `}`는 기존 `InitializeAudioSources()`의 닫는 괄호를 재활용하는 형태다 —
위 코드를 그대로 붙이면 `InitializeAudioSources()`가 닫히고 `CreateAmbienceSource()`가
새로 열린 뒤 닫힌다.)

- [ ] **Step 3: 앰비언스 API 추가**

`StopBGM()` 아래에 추가한다.

```csharp
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
        next.volume = 0f;
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
        float fromStart = from.volume;

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = duration > 0f ? Mathf.Clamp01(t / duration) : 1f;
            from.volume = Mathf.Lerp(fromStart, 0f, k);
            to.volume   = Mathf.Lerp(0f, 1f, k);
            yield return null;
        }

        from.volume = 0f;
        from.Stop();
        to.volume = 1f;

        ch.UsingA = !ch.UsingA;
        ch.Fade = null;
    }

    private System.Collections.IEnumerator FadeOutAmbience(AmbienceChannel ch, float duration)
    {
        AudioSource a = ch.A, b = ch.B;
        float aStart = a.volume, bStart = b.volume;

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = duration > 0f ? Mathf.Clamp01(t / duration) : 1f;
            a.volume = Mathf.Lerp(aStart, 0f, k);
            b.volume = Mathf.Lerp(bStart, 0f, k);
            yield return null;
        }

        a.volume = 0f; a.Stop();
        b.volume = 0f; b.Stop();
        ch.Fade = null;
    }
    #endregion
```

- [ ] **Step 4: 검증 (사용자)**

임시로 아무 루프 가능한 wav를 `amb_surface_day_cicada`로 등록하고, 콘솔에서
`SoundManager.Instance.SetAmbience(SoundManager.AmbienceLayer.Primary, SfxKeys.AmbSurfaceDayCicada)`
를 호출하는 디버그 키를 잠깐 붙여 페이드 인이 되는지 본다. 확인 후 디버그 코드는 지운다.

**→ 체크인 지점**

---

## Task 6: SoundManager — 상태 루프

**Files:**
- Modify: `Assets/Scripts/_Core/Managers/SoundManager.cs`

**Interfaces:**
- Produces:
  - `void Loop(string handle, string key, float fade = 0.15f, float pitch = 1f)`
  - `void StopLoop(string handle, float fade = 0.25f)`
  - `void SetLoopPitch(string handle, float pitch)`
  - `bool IsLooping(string handle)`

- [ ] **Step 1: 자료구조 추가**

필드 선언부에 추가한다.

```csharp
    // ── 상태 루프 (심장·드릴 모터·제트팩) ──
    // handle은 호출측이 정하는 식별자. handle당 전용 AudioSource 1개를 지연 생성해 캐시한다.
    private sealed class LoopChannel
    {
        public AudioSource Source;
        public string Key;
        public Coroutine Fade;
    }

    private readonly Dictionary<string, LoopChannel> _loops = new Dictionary<string, LoopChannel>();
```

- [ ] **Step 2: 루프 API 추가**

`#region Ambience` 블록 아래에 추가한다.

```csharp
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
        if (!sfxClipDict.TryGetValue(key, out AudioClip clip) || clip == null) return;

        if (!_loops.TryGetValue(handle, out LoopChannel ch))
        {
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
        }

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
```

- [ ] **Step 3: `using` 확인**

`SoundManager.cs` 상단에 `using System.Collections.Generic;`이 이미 있다. 추가로 필요한
것은 없다(`IEnumerator`는 전체 이름으로 썼다).

- [ ] **Step 4: 검증 (사용자)**

Unity에서 컴파일 에러가 없는지 확인한다. 실제 동작은 Task 9(`HeartbeatSfx`)에서 확인한다.

**→ 체크인 지점**

---

## Task 7: 에디터 툴 — SFX 폴더 일괄 등록

**Files:**
- Create: `Assets/Scripts/Editor/SfxFolderImporter.cs`

**Interfaces:**
- Consumes: `SfxKeys.All` (Task 2), `SoundDataSO` (기존)
- Produces: 메뉴 `Tools/Sound/Rescan SFX Folder`

- [ ] **Step 1: 구현**

```csharp
// @tags: sound, sfx, editor, tool, import, soundata
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Assets/Audio/SFX/ 안의 오디오 파일을 스캔해 파일명(확장자 제외)을 그대로 키로 삼아
/// SoundData.asset의 sfxClips에 등록한다.
///
/// 40여 개를 인스펙터로 손수 넣는 건 고역이고, 오타가 나면 무음이라 알아채기 어렵다.
/// 음원을 받아 파일명만 SfxKeys의 값과 맞춰 폴더에 넣고 이 메뉴를 누르면 끝난다.
///
/// 규칙:
///  - 같은 키가 이미 있으면 클립 참조만 갱신한다(중복 추가 없음).
///  - 폴더에 없는데 에셋에만 남은 키는 삭제하지 않는다(수동 등록분 보호). 경고만 띄운다.
///  - SfxKeys.All에 있는데 폴더에 파일이 없는 키를 콘솔에 나열한다
///    → 어떤 소리가 아직 안 채워졌는지 한눈에 본다.
/// </summary>
public static class SfxFolderImporter
{
    private const string SfxFolder   = "Assets/Audio/SFX";
    private const string SoundDataPath = "Assets/Sound/SoundData.asset";

    [MenuItem("Tools/Sound/Rescan SFX Folder")]
    public static void Rescan()
    {
        var soundData = AssetDatabase.LoadAssetAtPath<SoundDataSO>(SoundDataPath);
        if (soundData == null)
        {
            Debug.LogError($"[SfxFolderImporter] SoundData를 찾지 못했다: {SoundDataPath}");
            return;
        }

        if (!Directory.Exists(SfxFolder))
        {
            Debug.LogError($"[SfxFolderImporter] 폴더가 없다: {SfxFolder}\n" +
                           "폴더를 만들고 음원을 넣은 뒤 다시 실행할 것.");
            return;
        }

        // 1) 폴더 스캔 — 파일명(확장자 제외) → 클립
        var found = new Dictionary<string, AudioClip>();
        string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { SfxFolder });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null) continue;

            string key = Path.GetFileNameWithoutExtension(path);
            if (found.ContainsKey(key))
            {
                Debug.LogWarning($"[SfxFolderImporter] 키 중복: '{key}' — {path} 를 건너뛴다.");
                continue;
            }
            found[key] = clip;
        }

        // 2) SoundData에 반영
        var so = new SerializedObject(soundData);
        SerializedProperty list = so.FindProperty("sfxClips");

        int updated = 0, added = 0;
        foreach (var kv in found)
        {
            int index = IndexOfKey(list, kv.Key);
            if (index >= 0)
            {
                var elem = list.GetArrayElementAtIndex(index);
                var clipProp = elem.FindPropertyRelative("audioClip");
                if (clipProp.objectReferenceValue != kv.Value)
                {
                    clipProp.objectReferenceValue = kv.Value;
                    updated++;
                }
            }
            else
            {
                list.arraySize++;
                var elem = list.GetArrayElementAtIndex(list.arraySize - 1);
                elem.FindPropertyRelative("soundName").stringValue = kv.Key;
                elem.FindPropertyRelative("audioClip").objectReferenceValue = kv.Value;
                added++;
            }
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(soundData);
        AssetDatabase.SaveAssets();

        // 3) 리포트
        Debug.Log($"[SfxFolderImporter] 완료 — 신규 {added}개, 갱신 {updated}개 " +
                  $"(폴더에서 찾은 클립 {found.Count}개)");

        var missing = SfxKeys.All.Where(k => !found.ContainsKey(k)).ToArray();
        if (missing.Length > 0)
        {
            Debug.LogWarning($"[SfxFolderImporter] 아직 음원이 없는 키 {missing.Length}개:\n" +
                             "  " + string.Join("\n  ", missing));
        }
        else
        {
            Debug.Log("[SfxFolderImporter] SfxKeys의 모든 키에 음원이 있다.");
        }

        var orphan = new List<string>();
        for (int i = 0; i < list.arraySize; i++)
        {
            string key = list.GetArrayElementAtIndex(i).FindPropertyRelative("soundName").stringValue;
            if (!found.ContainsKey(key)) orphan.Add(key);
        }
        if (orphan.Count > 0)
        {
            Debug.LogWarning($"[SfxFolderImporter] 폴더에 없는데 SoundData에만 있는 키 " +
                             $"{orphan.Count}개(수동 등록분일 수 있어 삭제하지 않았다):\n" +
                             "  " + string.Join(", ", orphan));
        }
    }

    private static int IndexOfKey(SerializedProperty list, string key)
    {
        for (int i = 0; i < list.arraySize; i++)
        {
            var name = list.GetArrayElementAtIndex(i).FindPropertyRelative("soundName");
            if (name != null && name.stringValue == key) return i;
        }
        return -1;
    }
}
```

- [ ] **Step 2: 폴더 생성 (사용자)**

`Assets/Audio/SFX/` 폴더를 만든다.

- [ ] **Step 3: 검증 (사용자)**

`Tools/Sound/Rescan SFX Folder` 실행. 폴더가 비어 있어도 동작해야 하고, 콘솔에
"아직 음원이 없는 키 39개" 목록이 떠야 한다. 이 목록이 앞으로의 작업 목록이 된다.

**→ 체크인 지점**

---

## Task 8: AmbienceSelector (순수 로직 + 테스트) + AmbienceDirector

**Files:**
- Create: `Assets/Scripts/Audio/AmbienceSelector.cs`
- Create: `Assets/Scripts/Audio/AmbienceDirector.cs`
- Test: `Assets/Tests/EditMode/AmbienceSelectorTests.cs`

**Interfaces:**
- Consumes: `SfxKeys` (Task 2), `SoundManager.SetAmbience` / `StopAllAmbience` (Task 5),
  `TimeOfDay` enum (기존 `_Core/Data/Enums.cs`), `TileType` enum (기존)
- Produces: `AmbienceSet AmbienceSelector.Select(bool isSurface, TimeOfDay time, TileType layer)`

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/EditMode/AmbienceSelectorTests.cs`:

```csharp
using NUnit.Framework;

public class AmbienceSelectorTests
{
    [Test]
    public void SurfaceMorning_PlaysCicadaAndBirds()
    {
        var set = AmbienceSelector.Select(true, TimeOfDay.Morning, TileType.Dirt);
        Assert.AreEqual(SfxKeys.AmbSurfaceDayCicada, set.Primary);
        Assert.AreEqual(SfxKeys.AmbSurfaceDayBirds, set.Secondary);
    }

    [Test]
    public void SurfaceAfternoon_PlaysNightInsectsOnly()
    {
        var set = AmbienceSelector.Select(true, TimeOfDay.Afternoon, TileType.Dirt);
        Assert.AreEqual(SfxKeys.AmbSurfaceNightInsects, set.Primary);
        Assert.IsNull(set.Secondary);
    }

    [Test]
    public void SurfaceIgnoresLayer()
    {
        // 지상은 층 개념이 없다 — TileType이 뭐든 결과가 같아야 한다
        var a = AmbienceSelector.Select(true, TimeOfDay.Morning, TileType.Dirt);
        var b = AmbienceSelector.Select(true, TimeOfDay.Morning, TileType.MagmaRock);
        Assert.AreEqual(a.Primary, b.Primary);
        Assert.AreEqual(a.Secondary, b.Secondary);
    }

    [Test]
    public void UndergroundLayer1_IsSilent()
    {
        var set = AmbienceSelector.Select(false, TimeOfDay.Morning, TileType.Dirt);
        Assert.IsNull(set.Primary);
        Assert.IsNull(set.Secondary);
    }

    [Test]
    public void UndergroundLayer2_PlaysWind()
    {
        var set = AmbienceSelector.Select(false, TimeOfDay.Morning, TileType.HardStone);
        Assert.AreEqual(SfxKeys.AmbLayerWind, set.Primary);
        Assert.IsNull(set.Secondary);
    }

    [Test]
    public void UndergroundDeepLayers_PlayCaveDrip()
    {
        foreach (var t in new[] { TileType.CoolStone, TileType.Ice, TileType.HotStone,
                                  TileType.MagmaRock, TileType.MeteoriteRock })
        {
            var set = AmbienceSelector.Select(false, TimeOfDay.Morning, t);
            Assert.AreEqual(SfxKeys.AmbCaveDrip, set.Primary, $"층 {t}");
            Assert.IsNull(set.Secondary, $"층 {t}");
        }
    }

    [Test]
    public void UndergroundIgnoresTimeOfDay()
    {
        var a = AmbienceSelector.Select(false, TimeOfDay.Morning, TileType.HardStone);
        var b = AmbienceSelector.Select(false, TimeOfDay.Afternoon, TileType.HardStone);
        Assert.AreEqual(a.Primary, b.Primary);
    }

    [Test]
    public void UnknownTileType_IsSilent()
    {
        var set = AmbienceSelector.Select(false, TimeOfDay.Morning, TileType.Empty);
        Assert.IsNull(set.Primary);
        Assert.IsNull(set.Secondary);
    }
}
```

- [ ] **Step 2: `AmbienceSelector` 구현**

`Assets/Scripts/Audio/AmbienceSelector.cs`:

```csharp
// @tags: sound, ambience, audio, selector, pure, layer, daycycle
/// <summary>
/// (지상여부, 시간대, 층) → 앰비언스 2레이어 키.
///
/// MonoBehaviour와 분리한 순수 로직이라 EditMode에서 테스트한다.
/// 실제 재생은 AmbienceDirector가 한다.
/// </summary>
public readonly struct AmbienceSet
{
    public readonly string Primary;
    public readonly string Secondary;

    public AmbienceSet(string primary, string secondary)
    {
        Primary = primary;
        Secondary = secondary;
    }

    public static readonly AmbienceSet Silent = new AmbienceSet(null, null);

    public bool Equals(AmbienceSet other) => Primary == other.Primary && Secondary == other.Secondary;
}

public static class AmbienceSelector
{
    public static AmbienceSet Select(bool isSurface, TimeOfDay time, TileType layer)
    {
        if (isSurface)
        {
            // 지상은 층 개념이 없다 — 시간대로만 가른다
            return time == TimeOfDay.Morning
                ? new AmbienceSet(SfxKeys.AmbSurfaceDayCicada, SfxKeys.AmbSurfaceDayBirds)
                : new AmbienceSet(SfxKeys.AmbSurfaceNightInsects, null);
        }

        // 지하는 시간대와 무관하게 층으로만 가른다
        switch (layer)
        {
            case TileType.Dirt:            return AmbienceSet.Silent;   // 1층은 무음
            case TileType.HardStone:       return new AmbienceSet(SfxKeys.AmbLayerWind, null);
            case TileType.CoolStone:
            case TileType.Ice:
            case TileType.HotStone:
            case TileType.MagmaRock:
            case TileType.MeteoriteRock:   return new AmbienceSet(SfxKeys.AmbCaveDrip, null);
            default:                       return AmbienceSet.Silent;
        }
    }
}
```

- [ ] **Step 3: `AmbienceDirector` 구현**

`Assets/Scripts/Audio/AmbienceDirector.cs`:

```csharp
// @tags: sound, ambience, audio, director, daycycle, layer, monobehaviour
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 시간대·깊이에 따라 앰비언스 2레이어를 전환한다.
///
/// 판정 순서:
///  1) 씬 이름으로 지상/지하를 가른다(surfaceScenes에 포함되면 지상).
///  2) 지하면 플레이어 월드 Y를 청크 Y로 변환해 TileDataManager로 층을 얻는다.
///  3) 결과가 이전과 다를 때만 SoundManager에 반영한다.
///
/// 매 프레임 폴링하지 않고 pollInterval(기본 0.5초) 간격으로 검사한다.
/// 던전 안에서는 전부 정지한다(던전은 별도 연출 공간).
/// </summary>
public class AmbienceDirector : MonoBehaviour
{
    [Header("지상 판정")]
    [Tooltip("이 목록에 있는 씬은 지상으로 본다.")]
    [SerializeField] private string[] surfaceScenes = { "UpgroundScene", "SettlementScene" };

    [Header("폴링")]
    [SerializeField] private float pollInterval = 0.5f;
    [SerializeField] private float fadeDuration = 2f;

    [Header("참조 (비우면 런타임 탐색)")]
    [SerializeField] private Transform player;

    private AmbienceSet _current = AmbienceSet.Silent;
    private bool _hasApplied;
    private float _pollTimer;

    // 청크 1칸 = 10 월드유닛 (1000px / 100PPU)
    private const float ChunkWorldSize = 10f;

    private void OnEnable()
    {
        if (DayCycleManager.Instance != null)
            DayCycleManager.Instance.OnDateTimeChanged += OnDateTimeChanged;
    }

    private void OnDisable()
    {
        if (DayCycleManager.Instance != null)
            DayCycleManager.Instance.OnDateTimeChanged -= OnDateTimeChanged;

        if (SoundManager.Instance != null)
            SoundManager.Instance.StopAllAmbience(0.3f);
    }

    private void OnDateTimeChanged(int day, TimeOfDay time) => Evaluate();

    private void Update()
    {
        _pollTimer += Time.unscaledDeltaTime;
        if (_pollTimer < pollInterval) return;
        _pollTimer = 0f;
        Evaluate();
    }

    private void Evaluate()
    {
        var sm = SoundManager.Instance;
        if (sm == null) return;

        // 던전 안에서는 앰비언스를 끈다
        if (DungeonOverlayController.IsInDungeon)
        {
            Apply(AmbienceSet.Silent);
            return;
        }

        bool isSurface = IsSurfaceScene();
        TimeOfDay time = DayCycleManager.Instance != null
            ? DayCycleManager.Instance.CurrentTime
            : TimeOfDay.Morning;

        TileType layer = TileType.Empty;
        if (!isSurface)
        {
            Transform p = ResolvePlayer();
            if (p == null) return; // 플레이어가 아직 없다 — 다음 폴링에 재시도

            int chunkY = Mathf.FloorToInt(p.position.y / ChunkWorldSize);
            if (TileDataManager.Instance != null)
                layer = TileDataManager.Instance.GetTileTypeAtDepth(chunkY);
        }

        Apply(AmbienceSelector.Select(isSurface, time, layer));
    }

    private void Apply(AmbienceSet set)
    {
        if (_hasApplied && _current.Equals(set)) return;
        _current = set;
        _hasApplied = true;

        var sm = SoundManager.Instance;
        sm.SetAmbience(SoundManager.AmbienceLayer.Primary, set.Primary, fadeDuration);
        sm.SetAmbience(SoundManager.AmbienceLayer.Secondary, set.Secondary, fadeDuration);
    }

    private bool IsSurfaceScene()
    {
        string active = SceneManager.GetActiveScene().name;
        for (int i = 0; i < surfaceScenes.Length; i++)
            if (surfaceScenes[i] == active) return true;
        return false;
    }

    private Transform ResolvePlayer()
    {
        if (player != null) return player;

        var pc = Object.FindFirstObjectByType<PlayerController>();
        if (pc != null) player = pc.transform;
        return player;
    }
}
```

- [ ] **Step 4: 씬 배치 (사용자)**

`AmbienceDirector`를 `SoundManager`가 붙은 오브젝트에 함께 붙인다
(`DontDestroyOnLoad`라 씬 전환에도 살아남는다).

`SetAmbience`에 `null` 키를 넘기면 `StopAmbience`로 위임되므로, Secondary가 없는 상황도
자동 처리된다.

- [ ] **Step 5: 테스트 실행 (사용자)**

Test Runner > EditMode에서 `AmbienceSelectorTests` 8개를 실행한다.

**→ 체크인 지점**

---

## Task 9: FootstepTiming (순수 로직 + 테스트) + FootstepPlayer

**Files:**
- Create: `Assets/Scripts/Audio/FootstepTiming.cs`
- Create: `Assets/Scripts/Audio/FootstepPlayer.cs`
- Test: `Assets/Tests/EditMode/FootstepTimingTests.cs`

**Interfaces:**
- Consumes: `SfxKeys`, `SoundManager.PlaySFXJittered` (Task 4),
  `PlayerController.IsGrounded` / `isHardLanding` (기존)
- Produces: `float FootstepTiming.IntervalFor(float speed)`,
  `const float MinInterval = 0.18f`, `const float MaxInterval = 0.6f`

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/EditMode/FootstepTimingTests.cs`:

```csharp
using NUnit.Framework;

public class FootstepTimingTests
{
    [Test]
    public void FasterSpeed_GivesShorterInterval()
    {
        float slow = FootstepTiming.IntervalFor(2f);
        float fast = FootstepTiming.IntervalFor(6f);
        Assert.Less(fast, slow);
    }

    [Test]
    public void VeryFastSpeed_ClampsToMin()
    {
        Assert.AreEqual(FootstepTiming.MinInterval, FootstepTiming.IntervalFor(999f), 0.0001f);
    }

    [Test]
    public void VerySlowSpeed_ClampsToMax()
    {
        Assert.AreEqual(FootstepTiming.MaxInterval, FootstepTiming.IntervalFor(0.01f), 0.0001f);
    }

    [Test]
    public void ZeroSpeed_DoesNotDivideByZero()
    {
        float v = FootstepTiming.IntervalFor(0f);
        Assert.IsFalse(float.IsNaN(v));
        Assert.IsFalse(float.IsInfinity(v));
        Assert.AreEqual(FootstepTiming.MaxInterval, v, 0.0001f);
    }

    [Test]
    public void NegativeSpeed_UsesMagnitude()
    {
        // 왼쪽으로 걸어도 오른쪽과 같은 간격이어야 한다
        Assert.AreEqual(FootstepTiming.IntervalFor(4f), FootstepTiming.IntervalFor(-4f), 0.0001f);
    }

    [Test]
    public void ResultAlwaysWithinBounds()
    {
        for (float s = 0f; s < 30f; s += 0.37f)
        {
            float v = FootstepTiming.IntervalFor(s);
            Assert.GreaterOrEqual(v, FootstepTiming.MinInterval);
            Assert.LessOrEqual(v, FootstepTiming.MaxInterval);
        }
    }
}
```

- [ ] **Step 2: `FootstepTiming` 구현**

`Assets/Scripts/Audio/FootstepTiming.cs`:

```csharp
// @tags: sound, footstep, audio, timing, pure
using UnityEngine;

/// <summary>
/// 이동 속도 → 스텝 간격.
///
/// 속도에 반비례시키되 상·하한으로 클램프한다. 클램프가 없으면 느리게 걸을 때
/// 발소리가 몇 초에 한 번씩 나고, 빠를 때는 기관총처럼 들린다.
/// </summary>
public static class FootstepTiming
{
    public const float MinInterval = 0.18f;
    public const float MaxInterval = 0.6f;

    /// <summary>baseInterval은 "속도 1일 때의 간격". 기본값은 지상 걷기 기준으로 맞췄다.</summary>
    public static float IntervalFor(float speed, float baseInterval = 0.9f)
    {
        float raw = baseInterval / Mathf.Max(Mathf.Abs(speed), 0.1f);
        return Mathf.Clamp(raw, MinInterval, MaxInterval);
    }
}
```

`speed = 0.01f`일 때 `0.9 / 0.1 = 9` → `MaxInterval`로 클램프된다.
`speed = 999f`일 때 `0.9 / 999 ≈ 0.0009` → `MinInterval`로 클램프된다.

- [ ] **Step 3: `FootstepPlayer` 구현**

`Assets/Scripts/Audio/FootstepPlayer.cs`:

```csharp
// @tags: sound, footstep, audio, player, monobehaviour, surface
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 플레이어 발소리. PlayerController와 같은 GameObject에 붙인다.
///
/// 접지 상태이고 수평 속도가 임계 이상일 때만 타이머를 돌린다.
/// 표면은 씬으로 판정한다 — 지상이면 풀밭, 지하면 흙.
/// 점프 중·하드랜딩 중에는 재생하지 않는다.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class FootstepPlayer : MonoBehaviour
{
    [Header("판정")]
    [Tooltip("이 속도 미만이면 걷는 것으로 보지 않는다.")]
    [SerializeField] private float minSpeed = 0.6f;

    [Tooltip("이 목록에 있는 씬은 지상(풀밭)으로 본다.")]
    [SerializeField] private string[] surfaceScenes = { "UpgroundScene", "SettlementScene" };

    private PlayerController _controller;
    private Rigidbody2D _rb;
    private float _timer;

    private void Awake()
    {
        _controller = GetComponent<PlayerController>();
        _rb = GetComponent<Rigidbody2D>();
        if (_rb == null) _rb = GetComponentInChildren<Rigidbody2D>();
    }

    private void Update()
    {
        if (_controller == null || _rb == null) return;

        // 공중·하드랜딩 중에는 발소리가 나면 안 된다
        if (!_controller.IsGrounded || _controller.isHardLanding)
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

        var sm = SoundManager.Instance;
        if (sm == null) return;
        sm.PlaySFXJittered(IsSurfaceScene() ? SfxKeys.StepGrass : SfxKeys.StepDirt);
    }

    private bool IsSurfaceScene()
    {
        string active = SceneManager.GetActiveScene().name;
        for (int i = 0; i < surfaceScenes.Length; i++)
            if (surfaceScenes[i] == active) return true;
        return false;
    }
}
```

- [ ] **Step 4: 프리팹 부착 (사용자)**

플레이어 프리팹에 `FootstepPlayer`를 추가한다. `PlayerController`와 같은 GameObject여야
한다(`RequireComponent`).

주의: 프리팹은 Project 창에서 더블클릭해 Prefab Edit 모드로 편집한다. 씬 인스턴스에
붙인 뒤 Apply하면 다른 값이 틀어질 수 있다.

- [ ] **Step 5: 테스트 실행 (사용자)**

Test Runner > EditMode에서 `FootstepTimingTests` 6개를 실행한다.

**→ 체크인 지점**

---

## Task 10: HeartbeatSfx

**Files:**
- Create: `Assets/Scripts/Audio/HeartbeatSfx.cs`

**Interfaces:**
- Consumes: `SoundManager.Loop` / `StopLoop` / `SetLoopPitch` (Task 6), `SfxKeys`,
  `StaminaManager.PlayerStats` (기존)
- Produces: `const string LoopHandle = "heartbeat"` — Task 13의 게임오버 훅이 정지에 쓴다

- [ ] **Step 1: `PlayerStat`의 스태미나 접근자 확인**

구현 전에 `Assets/Scripts/UI/Player/Stats/` 아래에서 `PlayerStat`의 현재/최대 스태미나
프로퍼티 이름을 확인한다. 아래 코드는 `CurrentStamina` / `MaxStamina`를 가정한다.
이름이 다르면 그에 맞춰 고친다.

```
Grep(pattern: "Stamina", path: "Assets/Scripts/UI/Player/Stats", output_mode: "content")
```

- [ ] **Step 2: 구현**

`Assets/Scripts/Audio/HeartbeatSfx.cs`:

```csharp
// @tags: sound, heartbeat, stamina, audio, loop, monobehaviour, player
using UnityEngine;

/// <summary>
/// 스태미나가 낮을 때 심장 박동 루프를 재생한다.
///
/// 비율이 threshold 아래로 내려가면 루프 시작, 위로 올라오면 정지.
/// 낮을수록 pitch를 올려(1.0 → 1.35) 박동이 빨라지는 느낌을 준다.
///
/// 게임오버 연출 진입 시에는 GameOverHandler가 StopLoop(LoopHandle)로 즉시 끈다.
/// </summary>
public class HeartbeatSfx : MonoBehaviour
{
    /// <summary>SoundManager.Loop의 핸들. 외부(게임오버)에서 정지할 때 쓴다.</summary>
    public const string LoopHandle = "heartbeat";

    [Header("발동 조건")]
    [Tooltip("스태미나 비율이 이 값 미만이면 심장 소리가 시작된다.")]
    [SerializeField, Range(0.05f, 0.6f)] private float threshold = 0.25f;

    [Header("박동 가속")]
    [SerializeField] private float pitchAtThreshold = 1.0f;
    [SerializeField] private float pitchAtZero = 1.35f;

    [Header("참조 (비우면 런타임 탐색)")]
    [SerializeField] private StaminaManager staminaManager;

    private bool _active;

    private void Update()
    {
        var sm = SoundManager.Instance;
        if (sm == null) return;

        if (staminaManager == null)
        {
            staminaManager = Object.FindFirstObjectByType<StaminaManager>();
            if (staminaManager == null) return;
        }

        var stats = staminaManager.PlayerStats;
        if (stats == null) return;

        float max = stats.MaxStamina;
        if (max <= 0f) return;
        float ratio = Mathf.Clamp01(stats.CurrentStamina / max);

        if (ratio < threshold)
        {
            if (!_active)
            {
                sm.Loop(LoopHandle, SfxKeys.PlayerHeartbeat);
                _active = true;
            }

            // threshold에서 0으로 갈수록 pitch 상승
            float k = threshold > 0f ? 1f - (ratio / threshold) : 1f;
            sm.SetLoopPitch(LoopHandle, Mathf.Lerp(pitchAtThreshold, pitchAtZero, k));
        }
        else if (_active)
        {
            sm.StopLoop(LoopHandle);
            _active = false;
        }
    }

    private void OnDisable()
    {
        if (_active && SoundManager.Instance != null)
            SoundManager.Instance.StopLoop(LoopHandle, 0.1f);
        _active = false;
    }
}
```

- [ ] **Step 3: 프리팹 부착 (사용자)**

플레이어 프리팹에 `HeartbeatSfx`를 추가한다(Prefab Edit 모드에서).

- [ ] **Step 4: 검증 (사용자)**

임시 wav를 `player_heartbeat`로 등록하고, 지하에서 파기를 반복해 스태미나를 25% 아래로
떨어뜨린다. 심장 소리가 페이드 인 되고, 더 낮아질수록 빨라지고, 회복하면 페이드 아웃
되어야 한다.

**→ 체크인 지점**

---

## Task 11: 훅 — 플레이어 · 채굴

**Files:**
- Modify: `Assets/Scripts/UI/Player/PlayerController.cs` — `Jump()` (686행 부근)
- Modify: `Assets/Scripts/UI/Player/Strategies/PickaxeStrategy.cs` — `PerformDig()` (183~281행)
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/DiggableRock.cs` — `DestroyRock()` (462행)
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Behaviours/IndestructibleHitFeedback.cs`
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Entities/FragileIceBlock.cs` — `BreakIce()`
- Modify: `Assets/Scripts/Gameplay/Environment/IceBreakable.cs` — `BreakObject()`
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/InfinityMapManager.cs` — `ExplodeTerrain()` (1056행)

**Interfaces:**
- Consumes: `SfxKeys`, `SoundManager.PlaySFX` / `PlaySFXJittered` / `PlaySFXAt`

- [ ] **Step 1: 점프**

`PlayerController.Jump()`의 마지막 `rb.AddForce(...)` 다음 줄에 추가한다.

```csharp
        rb.AddForce(jumpVector * force * jumpDir, ForceMode2D.Impulse);

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SfxKeys.PlayerJump);
    }
```

- [ ] **Step 2: 곡괭이 휘두르기**

`PickaxeStrategy.PerformDig()`의 `_context.RaiseDigSwing();` 다음 줄에 추가한다.

```csharp
        _context.RaiseDigSwing();

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXJittered(SfxKeys.DigSwing);
```

- [ ] **Step 3: 곡괭이 명중**

`PerformDig()`의 히트 판정 결과를 쓰는 지점 — `if (hitAnyRock)` **직전**에 추가한다.
`hitAnyRock`/`hitAnyTerrain` 중 하나라도 참이면 뭔가에 맞은 것이다.

```csharp
        if (hitAnyRock || hitAnyTerrain)
        {
            if (SoundManager.Instance != null)
                SoundManager.Instance.PlaySFXJittered(SfxKeys.DigHit);
        }

        if (hitAnyRock)
            _staminaManager?.AddDiggingReduction(_maxStaminaReduction);
```

`dig_swing`과 `dig_hit`이 같은 프레임에 겹치는데, `SfxThrottle`은 **키별로** 관리하므로
서로 억제하지 않는다. 의도한 동작이다 — 휘두르는 소리 위에 맞는 소리가 얹힌다.

- [ ] **Step 4: 돌 부서짐**

`DiggableRock.DestroyRock()`의 `GetComponent<RockBreakVFX>()?.Play(...)` **직전**에
추가한다. VFX와 같은 타이밍이어야 한다.

```csharp
        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(SfxKeys.RockBreak, _spriteRenderer.bounds.center);

        // 지형 픽셀 제거 + 콜라이더 갱신 후 조각 스폰
        GetComponent<RockBreakVFX>()?.Play(_spriteRenderer.bounds.center, SizeScale);
```

- [ ] **Step 5: 못 캐는 것 튕김**

`IndestructibleHitFeedback.OnHitAttempt()`를 교체한다. 인스펙터 클립이 있으면 그것을
쓰고, 없으면 키 경로로 폴백한다 — 이미 프리팹에 연결된 참조를 깨지 않기 위해서다.

```csharp
    public void OnHitAttempt(Vector2 worldPos, int toolIndex)
    {
        Debug.Log($"[IndestructibleHitFeedback] 파괴 불가 영역 타격 — {gameObject.name}, tool={toolIndex}");

        // 인스펙터에 클립이 물려 있으면 그것을 우선한다(기존 프리팹 설정 보존).
        if (_audioSource != null && _hitSound != null)
        {
            _audioSource.PlayOneShot(_hitSound);
            return;
        }

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(SfxKeys.DigBlocked, worldPos);
    }
```

- [ ] **Step 6: 얼음 부서짐 (2곳)**

`FragileIceBlock.BreakIce()`의 사운드 블록을 교체한다.

```csharp
            // 인스펙터 클립 우선, 없으면 SoundManager 키 경로
            if (breakSound != null)
            {
                AudioSource.PlayClipAtPoint(breakSound, transform.position, 1.0f);
            }
            else if (SoundManager.Instance != null)
            {
                SoundManager.Instance.PlaySFXAt(SfxKeys.IceBreak, transform.position);
            }
```

`IceBreakable.BreakObject()`의 `// 1. Play sound` 블록을 같은 방식으로 교체한다.

```csharp
            // 1. Play sound — 인스펙터 클립 우선, 없으면 SoundManager 키 경로
            if (breakSound != null)
            {
                AudioSource.PlayClipAtPoint(breakSound, transform.position);
            }
            else if (SoundManager.Instance != null)
            {
                SoundManager.Instance.PlaySFXAt(SfxKeys.IceBreak, transform.position);
            }
```

- [ ] **Step 7: 폭발 (공통)**

`InfinityMapManager.ExplodeTerrain()`의 시작 부분에 추가한다. 이 한 곳이
`ScrapExplosion`, `DelayedBlast`, `ExplosiveMineralReactor`, `DashBombRelic`을 전부
커버한다 — 개별 호출부에 흩뿌리지 않는다.

```csharp
    public void ExplodeTerrain(Vector2 targetWorldPos, float radius)
    {
        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(SfxKeys.Explosion, targetWorldPos);

        float maxScale = 1.0f; // 원형이므로 scale 불필요
```

- [ ] **Step 8: 컴파일 확인 (사용자)**

Unity에서 컴파일 에러가 없는지 확인한다.

**→ 체크인 지점**

---

## Task 12: 훅 — 도구 · 유물

**Files:**
- Modify: `Assets/Scripts/UI/Player/Tools/ToolController.cs` — `EquipTool()` (88행)
- Modify: `Assets/Scripts/UI/Player/Strategies/DrillStrategy.cs` — `HandleUpdate()` (185~265행)
- Modify: `Assets/Scripts/Gameplay/Relics/Behaviours/JetpackRelic.cs` — `SetExhaust()` (166행)
- Modify: `Assets/Scripts/Gameplay/Relics/Behaviours/LightningRelic.cs` — `OnActivate()` (52행)
- Modify: `Assets/Scripts/Gameplay/Relics/Behaviours/OneWayPortalRelic.cs` — `OnActiveEnd()` (78행)

**Interfaces:**
- Consumes: `SfxKeys`, `SoundManager.PlaySFX` / `Loop` / `StopLoop`
- Produces: 루프 핸들 문자열 `"drill"`, `"jetpack"`

- [ ] **Step 1: 드릴 장착음**

`ToolController.EquipTool()`을 교체한다. 드릴로 **바뀔 때만** 울린다 — 이미 드릴인
상태에서 재호출되면 소리가 안 나야 한다.

```csharp
    public void EquipTool(int index)
    {
        // 범위 벗어나면 무시
        if (index < 0 || index >= toolSprites.Length) return;

        int previousIndex = currentToolIndex;
        currentToolIndex = index;
        UpdateToolSprite(previousIndex);

        // 드릴로 전환되는 순간에만 기동음 (같은 도구 재장착은 무음)
        if (index == drillIndex && previousIndex != drillIndex && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SfxKeys.DrillOn);
    }
```

- [ ] **Step 2: 드릴 모터 루프**

`DrillStrategy`에 핸들 상수와 헬퍼를 추가한다. 클래스 필드 선언부(`_infiniteBattery`
아래)에 넣는다.

```csharp
    private const string MotorLoopHandle = "drill";
    private bool _motorOn;
```

`HandleUpdate()`의 **맨 끝**(`if (_isDrillDashing) { ... }` 블록 다음, 메서드 닫는 괄호
직전)에 추가한다.

```csharp
        // 모터 루프 — 차징 중이거나 대시 중이면 돌고, 아니면 멈춘다
        bool motorShouldRun = (isAiming || _isDrillDashing) && _currentBattery > 0f;
        UpdateMotorLoop(motorShouldRun);
    }

    /// <summary>드릴 모터 루프 on/off. 상태가 바뀔 때만 SoundManager를 건드린다.</summary>
    private void UpdateMotorLoop(bool shouldRun)
    {
        if (shouldRun == _motorOn) return;
        _motorOn = shouldRun;

        var sm = SoundManager.Instance;
        if (sm == null) return;

        if (shouldRun) sm.Loop(MotorLoopHandle, SfxKeys.DrillMotor);
        else           sm.StopLoop(MotorLoopHandle);
    }
```

- [ ] **Step 3: 드릴 배터리 소진**

`HandleUpdate()`의 대시 중 배터리 0 분기에 추가한다.

```csharp
                _currentBattery -= Time.deltaTime * DASH_BATTERY_DRAIN * DashDrainMultiplier;
                if (_currentBattery <= 0)
                {
                    _currentBattery = 0;
                    StopDrillDash();

                    if (SoundManager.Instance != null)
                    {
                        SoundManager.Instance.StopLoop(MotorLoopHandle, 0.1f);
                        SoundManager.Instance.PlaySFX(SfxKeys.PowerDown);
                    }
                    _motorOn = false;
                    return;
                }
```

- [ ] **Step 4: `DrillStrategy.Exit()`에서 루프 정리**

전략이 교체될 때 모터 소리가 남으면 안 된다. `Exit()`에 추가한다.

```csharp
    public void Exit()
    {
        // ... 기존 코드 ...

        if (SoundManager.Instance != null)
            SoundManager.Instance.StopLoop(MotorLoopHandle, 0.1f);
        _motorOn = false;
    }
```

(기존 `Exit()` 본문은 그대로 두고 위 3줄만 끝에 덧붙인다.)

- [ ] **Step 5: 제트팩 루프**

`JetpackRelic.SetExhaust()`를 교체한다. 이미 on/off 상태 변화를 감지하는 구조라
그대로 활용한다.

```csharp
        // 하강 추진 연기 on/off. 최초 요청 시 코드로 생성한다.
        private void SetExhaust(bool on)
        {
            var sm = SoundManager.Instance;
            if (sm != null)
            {
                if (on) sm.Loop("jetpack", SfxKeys.JetpackThrust);
                else    sm.StopLoop("jetpack");
            }

            if (on && _exhaust == null) BuildExhaust();
            if (_exhaust == null) return;
            if (_exhaustEmission.enabled != on) _exhaustEmission.enabled = on;
        }
```

`_exhaust == null`이면 early return 하는 기존 구조 **위**에 소리를 두는 것이 중요하다.
아래에 두면 파티클 생성에 실패했을 때 소리도 안 난다.

- [ ] **Step 6: `JetpackRelic.OnUnequip()`에서 루프 정리**

`OnUnequip()` 또는 `Cleanup()`에 추가한다(둘 중 `SetExhaust(false)`를 부르지 않는 쪽에만).
`Cleanup()`이 이미 `SetExhaust(false)`를 부른다면 이 스텝은 건너뛴다 — 코드를 먼저 확인할 것.

```csharp
            if (SoundManager.Instance != null)
                SoundManager.Instance.StopLoop("jetpack", 0.1f);
```

- [ ] **Step 7: 번개 유물**

`LightningRelic.OnActivate()`에서 **타겟이 확정된 뒤**에 넣는다. 광물이 없으면
`minerals.Count == 0`으로 early return 하므로, 그 위에 두면 아무 일도 안 일어났는데
천둥만 친다.

```csharp
            if (minerals.Count == 0) return;

            if (SoundManager.Instance != null)
                SoundManager.Instance.PlaySFX(SfxKeys.RelicLightning);
```

- [ ] **Step 8: 귀환석**

`OneWayPortalRelic.OnActiveEnd()`에서 **텔레포트가 성사된 뒤**에 넣는다. 메서드 맨 앞의
`if (_portalGo == null) return;` 방어 분기 아래여야 한다.

```csharp
        public override void OnActiveEnd()
        {
            if (_portalGo == null) return; // 설치 없이 종료(방어)

            if (ctx?.player != null)
            {
                Vector2 from = ctx.player.position;

                // 텔레포트 (엘리베이터 검증 경로) + 낙하속도 이월 방지
                ctx.player.position = _portalPos;
                if (_rb == null) _rb = ctx.player.GetComponentInParent<Rigidbody2D>();
                if (_rb != null) _rb.linearVelocity = Vector2.zero;

                if (SoundManager.Instance != null)
                    SoundManager.Instance.PlaySFXAt(SfxKeys.RelicPortalReturn, _portalPos);

                // 출발·도착 양쪽 플래시
                if (ctx.runner != null)
                {
                    ctx.runner.StartCoroutine(FlashRing(from));
                    ctx.runner.StartCoroutine(FlashRing(_portalPos));
                }
            }

            DestroyPortalVisual();
        }
```

- [ ] **Step 9: 유물 공통 발동음**

`Assets/Scripts/Gameplay/Relics/Core/RelicInputHandler.cs`에서 유물 발동이 확정되는
지점을 찾아 추가한다. **전용 키가 있는 유물은 제외**해야 번개 칠 때 천둥과 가동음이
겹치지 않는다.

먼저 발동 지점을 확인한다:

```
Grep(pattern: "OnActivate|Activate\(", path: "Assets/Scripts/Gameplay/Relics/Core/RelicInputHandler.cs", output_mode: "content", -n: true, -C: 5)
```

발동 확정 지점에 아래를 넣는다. `RelicID` 열거형 이름은 실제 코드에 맞춰 조정한다.

```csharp
                    // 전용 효과음이 있는 유물은 공통 가동음을 내지 않는다
                    // (번개=천둥, 귀환석=포탈음, 제트팩=분사 루프, 탐지=핑)
                    bool hasOwnSfx = id == RelicID.Lightning
                                  || id == RelicID.OneWayPortal
                                  || id == RelicID.Jetpack
                                  || id == RelicID.DetectionPulse;

                    if (!hasOwnSfx && SoundManager.Instance != null)
                        SoundManager.Instance.PlaySFX(SfxKeys.RelicActivate);
```

- [ ] **Step 10: 컴파일 확인 (사용자)**

**→ 체크인 지점**

---

## Task 13: 훅 — 이동 · 전환 · 경제 · UI

**Files:**
- Modify: `Assets/Scripts/UI/Interaction/Elevator/ElevatorManager.cs` — `TeleportRoutine()` (223행)
- Modify: `Assets/Scripts/UI/Interaction/Behaviours/ChunkPortalBehaviours.cs` — `ChunkEntranceBehaviour.Interact()` (25행)
- Modify: `Assets/Scripts/UI/Shop/ShopManager.cs` — `SellItem()`, `BuyItem()`
- Modify: `Assets/Scripts/UI/Upgrade/UpgradeOverlayUI.cs` — `OnUnlockClicked()` (1057행)
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Cauldron/DokkaebiCauldron.cs` — `BrewRoutine()` (103행)
- Modify: `Assets/Scripts/Market/MarketSceneController.cs` — 씬 진입
- Modify: `Assets/Scripts/UI/Core/CodeUIKit.cs` — 버튼 기본 SFX

**Interfaces:**
- Consumes: `SfxKeys`, `SoundManager.PlaySFX` / `PlaySFXAt`

- [ ] **Step 1: 엘리베이터**

`ElevatorManager.TeleportRoutine()`의 페이드 시작 직전에 넣는다.

```csharp
        _teleporting = true;

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SfxKeys.ElevatorMove);

        // 0. 화면 어둡게 — 이후 모든 이동/로딩을 가린다.
        yield return Fade(true);
```

- [ ] **Step 2: 구멍 진입**

`ChunkEntranceBehaviour.Interact()`의 마지막 줄 직전에 넣는다. 모든 방어 분기를 통과해
**실제로 입장이 확정된 뒤**여야 한다.

```csharp
        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SfxKeys.PortalEnter);

        DungeonOverlayController.Instance.EnterDungeon(Owner.ChunkPrefab, Owner.ChunkCoord);
    }
```

- [ ] **Step 3: 광물 판매**

`ShopManager.SellItem()`의 성공 경로 — `playerStats.AddGold(totalGold);` 다음에 넣는다.

```csharp
                    playerStats.AddGold(totalGold);
                    DayEarningsLedger.Report(DayEarningsCategory.MineralSale, totalGold);
                    LogShopTransaction("sell", mineral.mineralID.ToString(), amount, totalGold, fromWarehouse);

                    if (SoundManager.Instance != null)
                        SoundManager.Instance.PlaySFX(SfxKeys.ShopSell);

                    return true;
```

- [ ] **Step 4: 상점 구매**

`ShopManager.BuyItem()`은 `DayEarningsLedger.Report(DayEarningsCategory.ShopPurchase, ...)`
호출이 3곳(192, 215, 274행)에 있다. **각 호출 바로 다음**에 아래를 넣는다.

```csharp
                    if (SoundManager.Instance != null)
                        SoundManager.Instance.PlaySFX(SfxKeys.ShopBuy);
```

같은 프레임에 여러 번 불려도 `SfxThrottle`(40ms)이 중복을 걸러준다.

- [ ] **Step 5: 강화 해금**

`UpgradeOverlayUI.OnUnlockClicked()`의 `mgr.UnlockNode(_selected)`가 성공한 분기 안에
넣는다.

```csharp
        if (mgr.CanUnlock(_selected))
        {
            if (mgr.UnlockNode(_selected))
            {
                if (SoundManager.Instance != null)
                    SoundManager.Instance.PlaySFX(SfxKeys.UpgradeUnlock);

                // ... 기존 코드 ...
```

- [ ] **Step 6: 도깨비 가마솥**

`DokkaebiCauldron.BrewRoutine()`의 연출 시작 직전에 넣는다.

```csharp
    private IEnumerator BrewRoutine(MineralID input)
    {
        _busy = true;

        CauldronResult result = _resolver.Resolve(input, new System.Random(Environment.TickCount));

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(SfxKeys.CauldronBrew, transform.position);

        yield return PlayBrewAnimation(result); // partial(Task 7) — 연출
```

- [ ] **Step 7: 마켓 단말 기동음**

`MarketSceneController`에서 씬 진입이 확정되는 지점을 찾아 넣는다.

```
Grep(pattern: "void Start|void Awake|Enter|OnEnable", path: "Assets/Scripts/Market/MarketSceneController.cs", output_mode: "content", -n: true)
```

`Start()` 끝부분에 추가한다.

```csharp
        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SfxKeys.UiTerminalOn);
```

- [ ] **Step 8: 버튼 기본 효과음**

`CodeUIKit`에서 버튼을 만드는 함수들이 `sfxName`을 인자로 받는다. 기본값이 비어 있으면
소리가 안 나므로, `PlaySfx`가 빈 이름을 받았을 때 `SfxKeys.UiButton`으로 폴백하게 한다.

`CodeUIKit.PlaySfx()`를 교체한다.

```csharp
    /// <summary>SoundDataSO에 등록된 SFX만 재생(없으면 무음). 이름이 비면 공용 버튼음.</summary>
    public static void PlaySfx(string sfxName)
    {
        var sm = SoundManager.Instance;
        if (sm == null) return;

        string key = string.IsNullOrEmpty(sfxName) ? SfxKeys.UiButton : sfxName;
        if (sm.HasSFX(key)) sm.PlaySFX(key);
    }
```

이렇게 하면 코드 생성 UI의 **모든 버튼**이 자동으로 `ui_button`을 낸다. 개별 버튼이
`sfxName`을 지정하면 그것이 우선한다.

- [ ] **Step 9: 컴파일 확인 (사용자)**

**→ 체크인 지점**

---

## Task 14: 훅 — 하루 사이클 · 결과

**Files:**
- Modify: `Assets/Scripts/Gameplay/Environment/SleepSequence.cs` — `Run()` (29행)
- Modify: `Assets/Scripts/_Core/Managers/DayCycleManager.cs` — `AdvanceToNextDay()` / `SetMorning()`
- Modify: `Assets/Scripts/UI/DaySummary/DaySummaryUI.cs` — `Play()` (112행)
- Modify: `Assets/Scripts/UI/Quest/QuestManager.cs` — `CompleteQuest()` (245행)
- Modify: `Assets/Scripts/Gameplay/GameOverHandler.cs` — `TriggerGameOver()` (98행)
- Modify: `Assets/Scripts/UI/Settlement/EmergencyEscapeSequenceUI.cs` — `Play()` (39행)
- Modify: `Assets/Scripts/UI/Interaction/Behaviours/ChunkPortalBehaviours.cs` — `ChunkExitBehaviour.Interact()` (55행)

**Interfaces:**
- Consumes: `SfxKeys`, `SoundManager.PlaySFX` / `StopLoop`, `HeartbeatSfx.LoopHandle` (Task 10)

- [ ] **Step 1: 코고는 소리**

`SleepSequence.Run()`의 페이드아웃 직후, 날짜가 넘어가기 전에 넣는다.

```csharp
        // 1) 페이드아웃 — 화면을 검게 덮고 시작
        if (ScreenFader.Instance != null)
            yield return ScreenFader.Instance.FadeOut(FadeDuration);

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SfxKeys.SleepSnore);

        DayCycleManager.Instance.AdvanceToNextDay();
```

- [ ] **Step 2: 아침 닭** — ⚠ **이후 제거됨.** 기획 판단으로 뺐다(키·음원·훅 모두 삭제).
  아래 내용은 당시 계획의 기록이며 현재 코드에는 없다. 현행 사양은
  `sound-system-design.md` §4 참조.

`DayCycleManager.AdvanceToNextDay()`에 넣는다. `SetMorning()`에는 넣지 않는다 —
세이브 로드 시 `LoadData()`가 같은 이벤트를 쏘므로 게임을 켤 때마다 닭이 운다.

```csharp
    public void AdvanceToNextDay()
    {
        currentDay++;
        currentTime = TimeOfDay.Morning;

        Debug.Log($"[DayCycleManager] AdvanceToNextDay - Day {currentDay}, Time {currentTime}");
        OnDateTimeChanged?.Invoke(currentDay, currentTime);

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SfxKeys.MorningRooster);
    }
```

- [ ] **Step 3: 정산 결과**

`DaySummaryUI.Play()`의 패널 페이드 인 직후에 넣는다. `report.total`의 부호로 가른다.

```csharp
        _canvasGroup.alpha = 0f; // 활성화 첫 프레임 번쩍임 방지
        _canvasObj.SetActive(true);
        yield return FadeGroup(_canvasGroup, 0f, 1f, PanelFadeDuration);

        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlaySFX(report.total >= 0
                ? SfxKeys.DaySummaryProfit
                : SfxKeys.DaySummaryLoss);
        }
```

- [ ] **Step 4: 퀘스트 완료**

`QuestManager.CompleteQuest()`의 완료 처리 직후에 넣는다. `quest.questType`으로 메인과
서브를 가른다.

```csharp
        // 퀘스트 완료 처리
        questStatuses[quest.questID] = QuestStatus.Completed;

        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlaySFX(quest.questType == QuestType.Sub
                ? SfxKeys.QuestComplete    // 서브 = 자전거 벨
                : SfxKeys.MissionSuccess); // 메인 = 미션 성공
        }

        // 서브퀘스트인 경우 슬롯에서 제거
        if (quest.questType == QuestType.Sub)
```

- [ ] **Step 5: 던전 클리어**

`ChunkExitBehaviour.Interact()`의 `DungeonOverlayController.Instance.ExitDungeon();`
직전에 넣는다.

```csharp
        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SfxKeys.MissionSuccess);

        DungeonOverlayController.Instance.ExitDungeon();
```

- [ ] **Step 6: 게임오버**

`GameOverHandler.TriggerGameOver()`의 상태 확정 직후에 넣는다. 심장 루프도 함께 끈다 —
스태미나 0으로 죽는 경우 심장 소리가 울리는 중이다.

```csharp
        if (_isGameOver || GameOverSequenceUI.IsPlaying) return;
        _isGameOver = true;

        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.StopLoop(HeartbeatSfx.LoopHandle, 0.1f);
            SoundManager.Instance.PlaySFX(SfxKeys.GameOver);
        }
```

- [ ] **Step 7: 긴급 탈출**

`EmergencyEscapeSequenceUI.Play(System.Action onComplete)`의 시작 부분에 같은 처리를
넣는다.

```csharp
        public static void Play(System.Action onComplete)
        {
            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.StopLoop(HeartbeatSfx.LoopHandle, 0.1f);
                SoundManager.Instance.PlaySFX(SfxKeys.GameOver);
            }

            // ... 기존 코드 ...
```

- [ ] **Step 8: 컴파일 확인 (사용자)**

**→ 체크인 지점**

---

## Task 15: 문서 갱신

**Files:**
- Modify: `Assets/Docs/audio/sound-system-design.md`
- Modify: `CLAUDE.md`

- [ ] **Step 1: 설계 문서에 구현 결과 반영**

`sound-system-design.md`의 §5 작업 순서를 완료 표시하고, 구현 중 설계와 달라진 부분이
있으면 그것을 적는다. 특히:
- `PlayerStat`의 스태미나 프로퍼티 이름이 `CurrentStamina`/`MaxStamina`가 아니었다면 실제 이름
- `RelicInputHandler`의 실제 발동 지점과 `RelicID` 열거형 값
- 믹서 BGM 그룹을 몇 dB로 맞췄는지

- [ ] **Step 2: `CLAUDE.md` 핵심 파일 표에 항목 추가**

`## 핵심 파일` 표에 아래 행을 추가한다.

```markdown
| `Assets/Scripts/_Core/Managers/SoundManager.cs` | 사운드 총괄 싱글톤. SFX 원샷 풀(10) + 3D `PlaySFXAt` + 앰비언스 2레이어 크로스페이드 + 상태 루프(`Loop`/`StopLoop`/`SetLoopPitch`). 모든 재생이 `HasSFX` 가드를 타므로 **클립 미등록 시 조용히 무음**(경고 없음) |
| `Assets/Scripts/_Core/Managers/SfxKeys.cs` | 효과음 키 상수 전체. 재생 호출은 **반드시 이 상수를 통해서만** 한다(리터럴 오타는 무음으로 조용히 넘어감). `SfxKeys.All`은 에디터 툴의 미등록 키 리포트용 |
| `Assets/Scripts/Audio/AmbienceDirector.cs` | 시간대·층에 따라 앰비언스 자동 전환. 판정은 순수 로직 `AmbienceSelector.Select(isSurface, time, layer)`로 분리(EditMode 테스트 있음) |
| `Assets/Scripts/Editor/SfxFolderImporter.cs` | `Tools/Sound/Rescan SFX Folder` — `Assets/Audio/SFX/`의 **파일명을 그대로 키로** `SoundData.asset`에 일괄 등록. 아직 음원이 없는 키를 콘솔에 나열해준다 |
```

- [ ] **Step 3: `CLAUDE.md` 아키텍처 제약에 항목 추가**

`## 아키텍처 제약` 절 끝에 추가한다.

```markdown
### 14. 새 효과음은 `SfxKeys` 상수 → `SoundManager` 경로로만 추가
인스펙터에 `AudioClip`을 직접 물리지 않는다. `SfxKeys`에 키를 정의하고
`SoundManager.PlaySFX(SfxKeys.X)` / `PlaySFXAt(...)` / `Loop(...)`을 호출한다.
음원은 `Assets/Audio/SFX/`에 **키와 같은 파일명**으로 넣고
`Tools/Sound/Rescan SFX Folder`를 실행하면 등록된다.

기존에 인스펙터 `AudioClip`을 쓰던 컴포넌트(`IceBreakable`, `FragileIceBlock`,
`IndestructibleHitFeedback` 등)는 **인스펙터 클립 우선, 없으면 키 폴백** 구조다.
프리팹에 이미 연결된 참조를 깨지 않기 위한 것이니 순서를 뒤집지 말 것.

앰비언스는 `SoundManager.SetAmbience(layer, key)`를 쓴다. `BGM` 그룹의 자식인
`Ambience` 믹서 그룹을 타므로 BGM 볼륨 슬라이더에 함께 묶인다.
```

- [ ] **Step 4: 설계 문서 §5 체크리스트 완료 표시**

---

## 자체 검토 결과

**설계 커버리지:**

| 설계 절 | 담당 태스크 |
|---|---|
| §2-1 한 사운드 = 한 클립, pitch 지터 | Task 4 (`PitchJitter`, `PlaySFXJittered`), Task 11 Step 2·3, Task 9 |
| §2-2 앰비언스는 BGM 자식 | Task 1 Step 1, Task 5 |
| §2-3 `BGMCycleManager` 유지·버그 수정 | Task 1 Step 2~4 |
| §3-1 `SoundManager` 확장 | Task 4, 5, 6 |
| §3-2 `SfxKeys` | Task 2 |
| §3-3 신규 컴포넌트 3개 | Task 8, 9, 10 |
| §3-4 에디터 툴 | Task 7 |
| §4-1~4-7 훅 매핑 35개 | Task 11, 12, 13, 14 |
| §⚠ 동작 변경 3건 | Task 1 Step 5(검증), Task 4 Step 6(검증) |

**타입 일관성 확인:**
- `SoundManager.AmbienceLayer`는 Task 5에서 정의하고 Task 8에서
  `SoundManager.AmbienceLayer.Primary`로 쓴다 — 중첩 열거형이라 이 경로가 맞다
- `HeartbeatSfx.LoopHandle`은 Task 10에서 정의하고 Task 14 Step 6·7에서 쓴다
- `SfxThrottle`은 Task 3에서 정의하고 Task 4의 `AcquireSfxSource`에서 쓴다
- `AmbienceSet.Equals`는 Task 8 Step 2에서 정의하고 같은 태스크 Step 3의
  `Apply`에서 쓴다

**확인이 필요한 미지수** (해당 태스크에서 먼저 Grep으로 확인하도록 스텝을 넣어뒀다):
- `PlayerStat`의 스태미나 프로퍼티 이름 → Task 10 Step 1
- `RelicInputHandler`의 발동 지점과 `RelicID` 값 → Task 12 Step 9
- `MarketSceneController`의 씬 진입 지점 → Task 13 Step 7
- `JetpackRelic.Cleanup()`이 이미 `SetExhaust(false)`를 부르는지 → Task 12 Step 6
