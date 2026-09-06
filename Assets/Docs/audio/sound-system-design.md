# 사운드 시스템 확장 설계 — 핵심 게임플레이 효과음

작성일: 2026-08-02

지금까지 이 프로젝트의 소리는 4갈래로 따로 놀았고, 중앙 시스템(`SoundManager`)에 등록된
클립은 2개(`footsteps`, `footsteps2`)뿐이며 그마저 호출부가 없어 실제로는 아무 소리도 나지
않았다. 이 문서는 핵심 게임플레이 효과음 39종을 넣기 위한 **인프라 확장**과 **훅 지점
매핑**을 정의한다.

원칙: **코드 훅을 전부 먼저 깔고, 클립은 나중에 채운다.** 모든 재생은 `HasSFX` 가드를
통과하므로 클립이 없으면 조용히 무음이고 경고도 없다. 사운드 파일을 받는 순서와 코드
작업 순서 사이에 의존성이 없다.

---

## 1. 현황 (작업 전)

| 경로 | 내용 | 문제 |
|---|---|---|
| `SoundManager` (싱글톤 + `SoundDataSO`) | 이름 키 → 클립 딕셔너리, AudioMixer 라우팅 | 등록 클립 2개, 호출부 0개. SFX 소스가 1개뿐 |
| `BGMCycleManager` | 지상 낮/밤 BGM 크로스페이드 | `SoundManager`를 안 탐. `secondarySource`에 믹서 그룹 미할당 |
| 개별 컴포넌트 인스펙터 `AudioClip` | 특수청크 VFX, `IceBreakable`, `LavaSinkBehavior` 등 11개 | 이름 키 없음, 믹서 그룹 보장 없음 |
| `MarketUISfx` / `CoinSfx` | 3단 폴백(에셋 세트 → `SoundDataSO` → 절차 합성) | 설계 양호. 이 패턴을 전역으로 확장한다 |

---

## 2. 결정 사항

### 2-1. 한 사운드 = 한 클립

같은 용도에 후보 음원이 여러 개 있는 경우(곡괭이 4종, 코고는 소리 2종 등)는 **그중 하나를
골라 확정**한다. 런타임 랜덤 선택은 하지 않는다. 따라서 클립 키는 1:1이고 `_1`/`_2` 변주
그룹핑도 없다.

예외적으로 **초당 여러 번 반복되는 소리**(`step_grass`, `step_dirt`, `dig_swing`)만 재생 시
pitch에 지터를 준다. 같은 클립이 기계적으로 반복될 때의 "따다다닥" 느낌을 없애는
용도이며, 클립을 바꿔 트는 것이 아니다. `SoundManager.PitchJitter = 0f`로 끌 수 있고,
발소리는 `FootstepPlayer.pitchJitter`(기본 0.06)로 따로 조절한다.

**예외 2: 발소리는 A/B 두 클립을 번갈아 낸다.** 왼발·오른발이라 랜덤 변주가 아니라
**결정적 교대**다. 키가 `step_*` / `step_*_b`로 쌍을 이룬다.

키 선택은 순수 로직 `FootstepSurfaceSelector.Select(isSurface, layer)`가 맡는다
(`AmbienceSelector`와 같은 구조, EditMode 테스트 있음).

| 상황 | A | B |
|---|---|---|
| 지상 | `step_grass` | `step_grass_b` |
| 지하 2층 (`TileType.HardStone`) | `step_hardstone` | `step_hardstone_b` |
| 지하 그 외 | `step_dirt` | `step_dirt_b` |

B가 등록되지 않은 표면은 `HasSFX` 검사로 A에 폴백한다. **폴백이 없으면 격 스텝마다
무음이 된다** — 새 층을 추가할 때 A만 넣어도 안전하게 동작하도록 한 장치다.

⚠ **키 이름은 음원 출처가 아니라 쓰이는 층을 가리킨다.** 2층은 눈(Snow) 음원을 쓰지만
키는 `step_hardstone`이다. 나중에 음원을 바꿔도 키는 그대로 두기 위해서다.

층 판정은 `AmbienceDirector`와 같은 방식이다 — 플레이어 월드 Y를 청크 Y로 변환해
`TileDataManager.GetTileTypeAtDepth`로 얻는다. 스텝마다(초당 2~5회) 호출되지만
정렬된 소형 리스트 검색이라 캐시할 만큼 비싸지 않다.

### 2-2. 앰비언스는 BGM 그룹의 자식

앰비언스(매미·새·풀벌레·바람·물방울)는 BGM 소스 1개로는 배경음악과 동시 재생이 안 되고,
SFX로 보내면 루프가 원샷 풀을 점유한다. 별도 채널이 필요하다.

```
Master (MasterVolume)
├─ BGM (BGMVolume)
│  └─ Ambience          ← 신규. 노출 파라미터 없음
└─ SFX (SFXVolume)
```

`Ambience`를 BGM의 **자식**으로 두는 이유: 유저 체감상 "배경음"이라 BGM 슬라이더가 같이
먹는 것이 자연스럽고, `SettingsOverlayUI`에 4번째 슬라이더를 추가하지 않아도 된다
(`SettingsOverlayUI`는 pending 값을 '적용' 버튼에서만 커밋하는 구조라 슬라이더 하나 추가
비용이 작지 않다). 믹서 계층만 추가하고 코드에서는 독립 `AudioSource`를 쓰므로
크로스페이드는 BGM과 따로 논다.

### 2-3. `BGMCycleManager`는 유지, 버그만 수정

크로스페이드 로직이 이미 정상 동작하고 지상 전용이므로 `SoundManager`로 흡수하지 않는다.
층별 BGM이 생기면 그때 통합한다. 지금은 두 가지만 고친다.

- `secondarySource`가 `AddComponent`로 생성돼 `outputAudioMixerGroup`이 비어 있다
  → **크로스페이드 후반 절반이 BGM 볼륨 슬라이더를 무시한다.** `primarySource`의 그룹을
  복사해 할당한다. **이것이 유일한 실제 버그다.**

⚠ **`maxVolume`은 제거하지 않는다.** 한때 "볼륨은 믹서에 맡긴다"고 판단해 걷어냈다가
되돌렸다. 이유: `Master.mixer`의 노출 파라미터 `BGMVolume`(guid `b14ce469…`)은
**BGM 그룹의 `m_Volume` 그 자체**다. 즉 에디터에서 BGM 그룹을 −8dB로 맞춰놔도
게임 시작 시 `SoundManager.LoadVolumes()` → `SetBGMVolume(1.0)` →
`SetFloat("BGMVolume", 0dB)`가 즉시 덮어쓴다. **믹서에는 정적 감쇠를 둘 자리가 없다.**

"이 음악은 원래 좀 작게" 같은 정적 트림은 `BGMCycleManager.maxVolume`(인스펙터,
기본 0.4)에서 준다. 믹서의 BGM 그룹 볼륨은 **오직 사용자 설정 슬라이더의 것**이다.
나중에 정적 트림을 믹서로 옮기고 싶다면 `BGM` 밑에 `Music` 자식 그룹을 만들어
거기에 감쇠를 걸고 BGM 소스들을 그쪽으로 재라우팅해야 한다(`Ambience`와 같은 구조).

---

## 3. 인프라

### 3-1. `SoundManager` 확장

기존 공개 API(`PlayBGM` / `PlaySFX(string)` / `PlaySound` / `HasSFX` / `GetSFX` / `StopBGM` /
볼륨 3종)는 **시그니처를 바꾸지 않는다.** `MarketUISfx`, `CoinSfx`, `CodeUIKit.PlaySfx`,
`WorldMapOverlay`, `SettingsOverlayUI`의 기존 호출부가 그대로 살아 있어야 한다.

추가할 것:

**원샷 풀** — 현재 SFX가 `AudioSource` 1개라서 `pitch`가 소스 공유 속성이다. 마지막 호출이
아직 울리고 있는 이전 소리의 음정까지 바꾼다. 라운드로빈 풀 10개로 교체한다.

```csharp
void PlaySFX(string key, float pitch, float volume = 1f)
void PlaySFXAt(string key, Vector3 worldPos, float pitch = 1f, float volume = 1f)
```

`PlaySFXAt`은 풀에서 소스를 꺼내 `spatialBlend = 1`, 위치를 지정해 재생한다. 돌 부서짐,
폭발, 낙석, 가마솥처럼 월드에 위치가 있는 소리용이다. 재생이 끝나면 `spatialBlend = 0`으로
되돌려 풀에 반납한다.

**앰비언스 2레이어** — 낮 지상은 매미와 새가 **동시에** 깔려야 한다. 레이어를 2개 두고
각 레이어가 크로스페이드용 소스 2개를 가진다(총 4 `AudioSource`).

```csharp
enum AmbienceLayer { Primary, Secondary }
void SetAmbience(AmbienceLayer layer, string key, float fade = 2f)
void StopAmbience(AmbienceLayer layer, float fade = 2f)
void StopAllAmbience(float fade = 2f)

// 실내(집·컨테이너) 연출 — ContainerFade가 호출
void SetAmbienceFilter(float cutoffHz, float volumeScale, float duration = 1.5f)
void ClearAmbienceFilter(float duration = 1.5f)
```

**볼륨 3단 구조** — 실제 `AudioSource.volume`은 세 값의 곱이다.

| 값 | 누가 정하나 | 기본 | 왜 분리했나 |
|----|------------|------|------------|
| 논리 볼륨(`VolA`/`VolB`) | 크로스페이드 코루틴 | 0→1 | 레이어 교체 |
| 기준 음량(`ambienceBaseVolume`) | `SoundManager` 인스펙터 | **0.5** | 클립 원본(특히 밤 풀벌레)이 커서 전역으로 낮춤 |
| 실내 덕킹(`_ambienceScale`) | `SetAmbienceFilter` | 1 (실내 **0.5**) | 집 안에서 더 작게 |

합치면 실내에서는 원본의 25%. 세 값을 합쳐 `AudioSource.volume` 하나에 쓰면
실내에 있는 동안 시간대가 바뀌어 앰비언스가 교체될 때 크로스페이드가 볼륨을 1로
되돌려 **덕킹이 풀린다** — 그래서 분리했다.

**실내 먹먹함** — BGM은 `ContainerFade`가 스피커의 `AudioLowPassFilter`를 인스펙터
참조로 직접 만진다. 앰비언스 소스는 `SoundManager`가 런타임에 만드는 자식 오브젝트라
인스펙터로 물릴 수 없어서, 소스마다 `AudioLowPassFilter`를 코드로 붙이고
위 API로 같은 컷오프(1000Hz)·같은 시간(1.5초)으로 함께 램프한다.

같은 키가 이미 그 레이어에서 재생 중이면 무시한다(씬 재진입·시간대 재통지 시 끊김 방지).

**전용 채널 원샷** — 겹치면 안 되는 반복음용.

```csharp
void PlaySFXExclusive(string handle, string key, float pitch = 1f, float volume = 1f)
```

원샷 풀은 호출마다 다른 소스를 잡으므로 **클립이 길면 소리가 쌓인다.** 발소리처럼 초당
여러 번 나는 것은 handle 전용 소스 하나를 재사용해 이전 재생을 끊고 시작한다 → 항상
1개만 울린다. `Loop`과 같은 채널 저장소(`GetOrCreateChannel`)를 쓰되 `loop = false`다.

**상태 루프** — 심장·드릴 모터·제트팩처럼 조건이 유지되는 동안 계속 울리는 소리.

```csharp
void Loop(string handle, string key, float fade = 0.15f, float pitch = 1f)
void StopLoop(string handle, float fade = 0.25f)
void SetLoopPitch(string handle, float pitch)   // 심장 박동 가속용
```

`handle`은 호출측이 정하는 식별자다(`"heartbeat"`, `"drill"`, `"jetpack"`). 같은 핸들로 다시
`Loop`을 호출하면 클립만 교체하고 페이드는 유지한다. 핸들당 전용 `AudioSource` 1개를 지연
생성해 캐시한다.

**연타 스로틀** — 같은 키가 최소 간격(기본 40ms) 안에 재호출되면 스킵한다.
`MarketUISfx`의 스크롤 스로틀과 같은 이유로, 한 프레임에 여러 번 발생하는 이벤트가
`PlayOneShot`으로 합산되며 소리가 터지는 것을 막는다.

**불변식**: 풀 소스는 `PlaySFXAt` 이후 반드시 `spatialBlend`/`pitch`를 기본값으로 되돌린
뒤 반납한다. 되돌리지 않으면 다음 2D 원샷이 엉뚱한 위치에서 들린다.

### 3-1-B. 디버그 — 어떤 소리가 나는지 확인하기

소리는 나는데 **어느 키가 울린 건지 귀로 구분이 안 되는** 상황이 잦다(비슷한 발소리
여러 종류, 표면·층에 따라 갈리는 키 등). `SoundManager` 인스펙터에 토글 두 개가 있다.

| 토글 | 출력 |
|---|---|
| `logPlays` | 재생될 때마다 `[SFX] 키 (clip: 파일명)` |
| `logSilentMisses` | **키 미등록으로 무음 처리된 호출**을 경고로 — "왜 소리가 안 나지" 추적용 |

`logSilentMisses`가 특히 유용하다. 이 시스템은 미등록 키를 조용히 넘기는 것이 원칙이라
평소엔 아무 표시가 없는데, 이걸 켜면 훅은 돌았지만 클립이 없는 경우가 드러난다.

발소리는 키보다 **왜 그 키가 골라졌는지**가 중요하므로 `FootstepPlayer.debugLog`를
따로 둔다. 출력 예:

```
[Footstep] 지상 · 오른발 → step_grass_b  [씬: UpgroundScene]
[Footstep] 지하/HardStone · 왼발 → step_hardstone  [씬: DemoUnderground]
[Footstep] 지하/Dirt · 오른발 → step_dirt  (B 미등록 → A 폴백)  [씬: DemoUnderground]
```

`[SoundManager]`는 코드 생성 오브젝트라 Hierarchy에서 `[SoundManager]`를 찾아 선택하면
인스펙터에서 토글할 수 있다(플레이 중에도 가능).

### 3-2. `SfxKeys` 상수 클래스

문자열 리터럴이 흩어지면 오타가 **무음으로 조용히 넘어가서** 디버깅이 어렵다. `CoinSfx`가
이미 `public const string Bet = "coin_bet"` 패턴을 쓰고 있으므로 전역으로 확장한다.
`Assets/Scripts/_Core/Managers/SfxKeys.cs`에 둔다.

명명 규칙은 기존 `market_ui_*` / `coin_*`와 맞춰 `<도메인>_<동작>` snake_case로 한다.

### 3-3. 신규 컴포넌트 3개

| 컴포넌트 | 위치 | 역할 | 입력 |
|---|---|---|---|
| `AmbienceDirector` | `Assets/Scripts/Audio/AmbienceDirector.cs` | 시간대·깊이에 따라 앰비언스 2레이어를 전환 | `DayCycleManager.OnDateTimeChanged`, `TileDataManager.GetTileTypeAtDepth(chunkY)` |
| `FootstepPlayer` | `Assets/Scripts/Audio/FootstepPlayer.cs` | 이동 속도로 스텝 간격을 계산하고 표면별 클립을 고른다 | `PlayerController.IsGrounded`, `rb.linearVelocity.x` |
| `HeartbeatSfx` | `Assets/Scripts/Audio/HeartbeatSfx.cs` | 스태미나 임계 이하일 때 심장 루프, 낮을수록 pitch 상승 | `StaminaManager.PlayerStats` |

**`AmbienceDirector` 전환 표**

| 상황 | Primary | Secondary |
|---|---|---|
| 지상 · 낮 (`TimeOfDay.Morning`) | `amb_surface_day_cicada` | `amb_surface_day_birds` |
| 지상 · 밤 (`TimeOfDay.Afternoon`) | `amb_surface_night_insects` | — |
| 지하 2층 (`TileType.HardStone`) | `amb_layer_wind` | — |
| 지하 3층 이하 | `amb_cave_drip` | — |
| 지하 1층 (`TileType.Dirt`), 던전 | — | — |

지상/지하 판정은 씬 이름(`UpgroundScene` / `DemoUnderground`)으로 먼저 가르고, 지하에서는
플레이어 월드 Y를 청크 Y로 변환해 `TileDataManager.GetTileTypeAtDepth`로 층을 얻는다.
층이 바뀌는 순간에만 `SetAmbience`를 부르고, 매 프레임 폴링은 0.5초 간격으로 스로틀한다.

**`FootstepPlayer` 규칙**

구동 방식이 두 가지이고 **자동 전환**된다.

1. **Animation Event (권장)** — 걷기 클립에서 발이 지면에 닿는 프레임에 `PlayFootstep()`을
   호출한다. 애니메이션과 정확히 동기화된다.
2. **속도 기반 타이머 (폴백)** — 이벤트가 아직 안 걸린 상태에서도 소리가 나게 한다.
   스텝 간격은 `baseInterval / max(speed, 0.1)`을 `[0.18s, 0.6s]`로 클램프한 값
   (`FootstepTiming.IntervalFor`, EditMode 테스트 있음).

`PlayFootstep()`이 한 번이라도 호출되면 타이머는 **영구히 꺼진다** → 이벤트를 넣는 순간
자동으로 1번으로 넘어가고 두 방식이 겹쳐 두 번 울리지 않는다. 인스펙터 토글을 켜고 끄는
것을 잊어 무음이 되거나 이중 재생되는 실수를 없애기 위한 구조다.

⚠ **Animation Event를 쓰려면 이 컴포넌트가 플레이어 루트에 있어야 한다.** Animation
Event는 **Animator가 붙은 GameObject의 컴포넌트만** 호출한다(자식·부모를 뒤지지 않는다).
이 프로젝트의 Animator는 루트에 있다(`PlayerController.anim = GetComponent<Animator>()`).
`PlayerMining.Dig()` / `AllowCombo()` / `AllowAttackCancel()` 등 기존 이벤트 수신부와
같은 조건이다. 타이머 폴백만 쓸 거라면 자식 오브젝트에 둬도 된다.

표면은 씬으로 판정한다 — 지상이면 `step_grass`, 지하면 `step_dirt`. 두 방식 모두
접지(`IsGrounded`)·하드랜딩(`isHardLanding`) 가드를 통과해야 재생된다.

**`HeartbeatSfx` 규칙**

스태미나 비율이 `threshold`(기본 0.25) 아래로 내려가면 `Loop("heartbeat", ...)`을 시작하고,
위로 올라오면 `StopLoop`. 비율이 낮을수록 `SetLoopPitch`로 1.0 → 1.35까지 올려 박동이
빨라지는 느낌을 준다. 게임오버 연출 진입 시에는 즉시 정지한다.

### 3-4. 에디터 툴 — 클립 일괄 등록

`Assets/Audio/SFX/` 폴더의 오디오 파일을 스캔해 **확장자를 뗀 파일명을 그대로 키로** 삼아
`SoundData.asset`의 `sfxClips`에 등록하는 메뉴 아이템을 만든다.

```
Tools/Sound/Rescan SFX Folder
```

- 이미 같은 키가 있으면 클립 참조만 갱신한다(중복 추가 없음).
- 폴더에 없는데 에셋에만 남은 키는 삭제하지 않고 콘솔에 경고만 띄운다(수동 등록분 보호).
- 스캔 후 `SfxKeys`에 정의됐지만 폴더에 파일이 없는 키 목록을 콘솔에 출력한다
  → **어떤 소리가 아직 안 채워졌는지 한눈에 본다.**

`SoundDataSO`는 `List<SoundAudioClip>`이라 에디터에서 `SerializedObject`로 안전하게 편집
가능하다. 툴은 `Assets/Scripts/Editor/SfxFolderImporter.cs`에 둔다.

---

## 4. 사운드 키 ↔ 훅 지점 매핑

`✓` = 훅이 이미 존재해 호출 1줄만 추가하면 되는 것.

### 4-1. 플레이어

| 키 | 소리 | 훅 지점 |
|---|---|---|
| `jump_grass` / `jump_dirt` | 점프 시작 (표면별) | ✓ `PlayerController.Jump()` → `FootstepPlayer.PlayJump()` |
| `land_grass` / `land_dirt` | 착지 (표면별) | ✓ `PlayerController.OnLanded()` → `FootstepPlayer.PlayLand()`. 호출 조건이 `isGrounded && !_prevGrounded`라 접지 전이 순간에만 |
| `player_heartbeat` | 심장 (스태미나 낮음, 루프) | `HeartbeatSfx` 신규 |
| `player_hurt` | 위험물 피격 | ✓ `PlayerStat.ApplyHazardDamage()` — 무적 체크 **뒤** |
| `step_grass` | 풀밭 걷기 (지상) | `FootstepPlayer` 신규 |
| `step_dirt` | 흙 발소리 (지하) | `FootstepPlayer` 신규 |

### 4-2. 채굴

| 키 | 소리 | 훅 지점 |
|---|---|---|
| `dig_swing` | 곡괭이 휘두르기 | ✓ `PickaxeStrategy.PerformDig()` — Animation Event `PlayerMining.Dig()`가 호출 |
| `dig_sap` | 삽 파기 | ✓ `SapStrategy.PerformSapDig()` — Animation Event `PlayerMining.SapDig()`가 호출 |
| `dig_hit` | 곡괭이 부딪힘 (명중) | `PickaxeStrategy.PerformDig()` — 지형/돌 명중 분기 |
| `dig_blocked` | 못 캐는 것 두드림, 튕김 | ✓ `IndestructibleHitFeedback.OnHitAttempt()` |
| `rock_break` | 돌 부서짐 | ✓ `DiggableRock.DestroyRock()` — `PlaySFXAt` |
| `ice_break` | 얼음층 바위 부서짐 | ✓ `FragileIceBlock.BreakIce()`, `IceBreakable.BreakObject()` — `PlaySFXAt` |
| `explosion` | 폭발 공통 | ✓ `InfinityMapManager.ExplodeTerrain()` — `PlaySFXAt` |

`explosion`을 `ExplodeTerrain` 안에 넣으면 `ScrapExplosion`, `DelayedBlast`,
`ExplosiveMineralReactor`, `DashBombRelic`이 전부 한 번에 커버된다. 개별 호출부에 흩뿌리지
않는다.

### 4-3. 도구 · 유물

| 키 | 소리 | 훅 지점 |
|---|---|---|
| `drill_on` | 드릴 켜질 때 버튼 | ✓ `ToolController.EquipTool(drillIndex)` |
| `drill_motor` | 드릴 모터 (루프) | `DrillStrategy.HandleUpdate()` — 파기·대시 중 `Loop("drill", ...)` |
| `power_down` | 배터리 소진, 전원 꺼짐 | `DrillStrategy` 배터리 0 도달 + `JetpackRelic` 연료 소진 |
| `jetpack_thrust` | 제트팩 분사 (루프) | ✓ `JetpackRelic.SetExhaust(bool)` |
| `relic_lightning` | 천둥 (번개 유물) | ✓ `LightningRelic.OnActivate()` |
| `relic_detect_ping` | 탐지기 | ✓ 이미 `DetectionPingSfx`가 이 키를 조회한다. **클립 등록만 하면 즉시 동작** |
| `relic_portal_return` | 귀환석 (일회용 포탈 귀환) | ✓ `OneWayPortalRelic.OnActiveEnd()` |
| `relic_invincible` | 무적 (포스필드) | ✓ `InvincibilityRelic.OnActivate()` |
| `relic_gravity` | 반중력 지속 (루프) | ✓ `GravityFlipRelic.OnActivate()` 시작 / `OnActiveEnd`·`OnUnequip`에서 정지 |
| `relic_activate` | 전자제품 가동음 (유물 공통) | `RelicInputHandler` 발동 지점 — 개별 유물 전용음이 없을 때의 기본값 |

**`relic_activate`는 전용 키가 있는 유물에서는 울리지 않는다.** 판별은
`RelicBehaviour.HasOwnActivationSfx` override로 한다. 현재 override하는 유물:
`LightningRelic`, `DetectionPulseRelic`, `InvincibilityRelic`.

(`JetpackRelic`은 패시브라 `ActivateSlot` 경로를 아예 안 타고, `OneWayPortalRelic`은
의도적으로 override하지 않는다 — 설치는 공통음, 귀환만 전용음.)

`OneWayPortalRelic`은 토글형이라 발동 순간이 둘이다 — **설치(`OnActivate`)는 유물 공통음
`relic_activate`**, **귀환(`OnActiveEnd`)은 `relic_portal_return`**. 귀환이 연출의 정점이고
플레이어가 실제로 순간이동하는 순간이라 전용 음원을 여기에 붙인다. 설치는 공통음으로도
피드백이 충분하다. `OnActiveEnd`는 `_portalGo == null` 방어 분기를 먼저 타므로, 소리는
**포탈이 실제로 존재해 귀환이 성사된 뒤**에 재생해야 한다 — 방어 분기로 빠지는 경우
소리만 나고 이동은 안 하는 상황이 생긴다.

### 4-4. 이동 · 전환

| 키 | 소리 | 훅 지점 |
|---|---|---|
| `elevator_move` | 엘리베이터 묵직 | ✓ `ElevatorManager.TeleportRoutine()` |
| `portal_enter` | 훅, 구멍 들어갈 때 | ✓ `ChunkEntranceBehaviour.Interact()` |

### 4-5. 경제 · UI

| 키 | 소리 | 훅 지점 |
|---|---|---|
| `ui_button` | 귀여운 버튼 클릭 | ✓ `CodeUIKit.PlaySfx()` — 코드 생성 UI 전역 기본값 |
| `ui_back` | 닫기·취소(ESC) | ✓ `CodeUI.PlayBack()` — 모든 UI의 ESC 처리 지점에서 호출 (아래 ⚠ 참고) |
| `ui_terminal_on` | 컴퓨터 켜지는 소리 | `MarketSceneController` 진입 |
| `mineral_pickup` | 광물 획득 (월드에서 줍기) | ✓ `PickupableItem.PlayPickupEffect()` — `PlaySFXAt` |
| `shop_sell` | 동전, 광물 판매 | ✓ `ShopManager.SellItem()` |
| `shop_buy` | 상점 구매 | ✓ `ShopManager.BuyItem()` |
| `upgrade_unlock` | 업그레이드 트리 노드 해금 (단발) | ✓ `UpgradeOverlayUI.OnUnlockClicked()` |
| `upgrade_tier` | 상위 계층(Tier) 해금 | ✓ `UpgradeManager.PlayTierUnlockSfx()` — 해금 경로 2곳에서 호출 |
| `equip_enhance` | 장비·유물 강화 (망치 타격 1회분) | ✓ `EquipmentUpgradeOverlayUI.PlayHammerSfx()` — 타격 연출 매 회 |

⚠ **ESC = `ui_back`은 `CodeUI.PlayBack()` 한 곳으로 통일한다.**
닫기 경로마다 `isEscape` 플래그를 새로 뚫는 대신, ESC를 감지하는 자리에서
`CodeUI.PlayBack()`을 부르면 된다. `PlayBack`은 두 가지를 알아서 처리한다:

1. **같은 프레임 중복 제거** — 오버레이 자신과 `UIStateManager`가 같은 ESC를 겹쳐
   처리하는 경로가 있어서, 프레임당 한 번만 소리를 낸다.
2. **뒤따르는 클릭음 삼키기** — 대부분의 `Close()`가 `CodeUI.PlaySfx(clickSfxName)`를
   부르므로, 그대로 두면 뒤로가기음 위에 클릭음이 겹쳐 두 번 들린다. `PlaySfx`는
   `PlayBack`이 난 프레임 동안 클릭음(`ui_click`/`ui_button`/미등록 키)만 무시한다.
   전용음이 등록된 소리(거래·강화 등)는 삼키지 않는다.

ESC가 아닌 토글 키로 닫을 때(Tab=인벤토리/창고, J=퀘스트, M=지도)는 평소 클릭음
그대로다 — 그래서 호출부가 `if (Input.GetKeyDown(KeyCode.Escape))`로 한 번 더 가른다.
`PauseOverlayUI`/`SettingsOverlayUI`는 원래부터 `backSfxName` 인스펙터 필드로
갈라 쓰던 곳이라 그 필드를 `PlayBack(backSfxName)`으로 넘긴다.
개발 전용 오버레이(`BugReportOverlayUI`, `DebugConsoleOverlayUI`)는 제외했다.

⚠ **업그레이드 트리와 장비 강화는 별개 시스템이다.** 처음 구현할 때 둘을 혼동해
강화용 음원을 업그레이드 트리에 붙였다가 분리했다.

- **업그레이드 트리** — `UpgradeOverlayUI` / `UpgradeManager.UnlockNode()`. 스킬트리형 해금
- **장비 강화** — `EquipmentUpgradeOverlayUI` / `EquipmentUpgradeStore.TryUpgrade()`. 장비 +N 레벨업

⚠ **강화음 훅 위치가 바뀌었다 (2026-08-04).** 원래 `EquipmentUpgradeStore.TryUpgrade()`에
`PlaySFXRepeat(EquipEnhance, 3, 0.13f)` 3연타를 두었으나, `EquipmentUpgradeOverlayUI`에
**이미 3타 망치 연출이 있어서** 소리가 이중으로 났다. 실제 타임라인은 이랬다:

```
0.00 / 0.28 / 0.56s  해머 연출 ×3 → hammerSfxName("ui_click", 미등록) → ui_button 폴백
~0.72s               TryUpgrade → equip_enhance ×3 (0.13s 간격)   ← 연출 끝난 뒤 몰아서
동시                 successSfxName("ui_click") → ui_button 폴백
```

즉 화면은 3번 튀는데 강화음은 그와 무관하게 나중에 따로 3연타로 터졌다.

**현재 구조**: 강화음은 `EquipmentUpgradeOverlayUI.PlayHammerSfx()`에서 **타격 1회당 1번**
울린다. 데이터 계층(`EquipmentUpgradeStore`)에는 사운드가 없다.

음정은 **의도적으로 고정**이다(pitch 1.0). 구 `PlaySFXRepeat(..., pitchStep: 0.05f)`는
타격마다 음정을 올려 상승감을 줬지만, 같은 소리 3번이 더 낫다는 판단으로 뺐다.
다시 넣고 싶으면 `PlayHammerSfx`에 타격 인덱스를 넘겨 `SoundManager.PlaySFX(key, pitch)`를
쓰면 된다 — `CodeUI.PlaySfx`에는 pitch 오버로드가 없다.

이 위치가 나은 이유:
- 타격 연출과 소리가 1:1로 붙어 어긋날 수 없다
- **유물 강화도 같은 연출을 타므로 자동으로 강화음이 붙는다** (구 구조에서는 유물 분기가
  `RelicInventory.TryUpgrade`라 강화음이 아예 없었다)

트레이드오프: 실패한 강화에서도 타격음은 난다(연출이 먼저 돌고 마지막에 적용되므로).
성공/실패는 `successSfxName`/`failSfxName`과 초록 플래시로 구분된다.

⚠ 이 오버레이의 `clickSfxName`/`successSfxName`/`failSfxName`은 아직 레거시 기본값
`"ui_click"`(미등록 키)이라 `CodeUIKit.PlaySfx`의 `ui_button` 폴백을 탄다. 오버레이가
`AddComponent`로 런타임 생성되어 인스펙터 덮어쓰기가 없으므로 항상 이 값이다.
전용음이 생기면 필드 기본값을 `SfxKeys.*` 상수로 바꿀 것.
| `cauldron_brew` | 도깨비 가마솥 | ✓ `DokkaebiCauldron.BrewRoutine()` — `PlaySFXAt` |
| `coin_tick` | 코인 결과 대기 '띠' | ⛔ **사용 안 함**. `CoinSfx.Play`가 Tick만 등록 클립을 건너뛰고 합성 비프(`MarketUISfx.Kind.Blip`)로 고정한다 — SoundManager 경로는 pitch를 못 실어서 가속감이 죽기 때문. 클립을 등록해도 안 울린다 |

### 4-6. 하루 사이클 · 결과

| 키 | 소리 | 훅 지점 |
|---|---|---|
| `sleep_snore` | 코고는 소리 | ✓ `SleepSequence.Run()` |
| `daysummary_profit` | 관중 환호 (정산 흑자) | ✓ `DaySummaryUI.PlayRowSfx()` — **'최종 손익' 줄이 공개될 때**, `report.total >= 0` |
| `daysummary_loss` | 맑은 실패·불안 (정산 적자) | ✓ `DaySummaryUI.PlayRowSfx()` — 같은 지점, `report.total < 0` |

정산 연출의 소리 구성: **각 금액 줄이 공개될 때마다 동전 소리**(`shop_sell` 재사용),
**'최종 손익' 줄에서만** 흑자/적자 결과음. 구분선은 무음. 결과음을 패널 등장 직후가 아니라
총계 줄에 붙여야 연출의 정점과 소리가 맞는다.
| `quest_complete` | 자전거 벨, 서브퀘스트 완료 | ✓ `QuestManager.CompleteQuest()` — 서브퀘스트 |
| `mission_success` | 미션 성공 (메인퀘스트·던전 클리어) | ✓ `QuestManager.CompleteQuest()` 메인 + `ChunkExitBehaviour.Interact()` |
| `game_over` | 실패, 죽음·탈출 | ✓ `GameOverHandler.TriggerGameOver()` + `EmergencyEscapeSequenceUI.Play()` |

`quest_complete`(서브)와 `mission_success`(메인·던전 클리어)의 배분은 잠정이다. 둘 다
`QuestManager.CompleteQuest()` 한 곳에서 `QuestSO`가 메인인지 서브인지로 갈린다.

### 4-7. 앰비언스

| 키 | 소리 | 트리거 |
|---|---|---|
| `amb_surface_day_cicada` | 매미 | 지상 · 낮 (Primary) |
| `amb_surface_day_birds` | 숲 새 지저귐 | 지상 · 낮 (Secondary) |
| `amb_surface_night_insects` | 밤 풀벌레 | 지상 · 밤 (Primary) |
| `amb_layer_wind` | 바람 | 지하 2층 `HardStone` (Primary) |
| `amb_cave_drip` | 동굴 물방울 | 지하 3층 이하 (Primary) |

---

## 5. 작업 순서 — 코드 구현 완료 (2026-08-02)

인프라 → 훅 순으로 진행했다. 훅 단계에서는 클립 유무와 무관하게 전부 꽂았다.

1. ✅ **믹서**: `Master.mixer`에 `Ambience` 그룹 추가 (`BGM`의 자식) — **수동 작업 미완**
2. ✅ **`SoundManager` 확장**: 원샷 풀, `PlaySFXAt`, 앰비언스 2레이어, 상태 루프, 스로틀
3. ✅ **`SfxKeys`** 상수 정의 (§4의 전체 키 39개)
4. ✅ **에디터 툴** `Tools/Sound/Rescan SFX Folder`
5. ✅ **`BGMCycleManager` 버그 수정** (믹서 그룹 누락, `maxVolume` 하드코딩)
6. ✅ **신규 컴포넌트 3개** (`AmbienceDirector`, `FootstepPlayer`, `HeartbeatSfx`)
7. ✅ **훅 삽입** — §4 전체

상세 실행 계획은 `Assets/Docs/audio/sound-system-plan.md` 참조.

### ⚠ 구현 중 발견: `SoundManager`가 `MarketScene`에만 있었다

`SoundManager`는 씬 오브젝트로 **`MarketScene` 한 곳에만** 배치돼 있었고 코드로 생성하는
경로가 없었다. `DontDestroyOnLoad` 싱글톤이라 마켓을 한 번 방문하면 이후엔 살아남지만,
**그 전까지는 지상·지하·던전 어디서도 `Instance`가 null**이었다. 즉 모든 재생 호출이
조용히 무시되는 상태였다 — 사운드가 안 나던 진짜 이유다.

**해결**: `SoundManager`에 `RuntimeInitializeOnLoadMethod(BeforeSceneLoad)` 부트스트랩을
추가하고(`TelemetryRunner`와 같은 패턴), 참조는 `Awake`에서 Resources로 자동 해석한다
(`InfinityMapManager`의 `TileVisualSettings` 로드와 같은 패턴).

- `soundData` → `Resources.Load<SoundDataSO>("SoundData")`
- `mainMixer` → `Resources.Load<AudioMixer>("Master")`
- 믹서 그룹 3종 → `mainMixer.FindMatchingGroups(name)`에서 **이름 정확 일치** 우선 선택
  (`FindMatchingGroups`는 경로 부분일치라 `"BGM"`이 자식 `"BGM/Ambience"`까지 물고 온다)

인스펙터에 값이 있으면 그것을 존중하므로 `MarketScene`의 기존 배치도 그대로 동작한다.
`AmbienceDirector`도 같은 이유로 자동 생성된다(`AfterSceneLoad`).

### 남은 수동 작업 (Unity 에디터)

1. `Master.mixer`에 `Ambience` 그룹 추가 — Audio Mixer 창 **왼쪽 `Groups` 패널**에서
   `BGM` 우클릭 → `Add child group` → 이름 `Ambience`.
   **BGM 그룹 볼륨은 건드리지 않는다** — 노출 파라미터라 런타임에 덮어써진다(§2-3 ⚠)
2. **`Assets/Sound/SoundData.asset`과 `Assets/Sound/Master.mixer`를 `Assets/Resources/`로 이동.**
   Unity에서 이동하면 GUID가 유지되므로 `MarketScene`의 기존 참조는 안 깨진다.
   이게 없으면 부트스트랩이 참조를 못 찾아 콘솔에 에러가 뜬다.
3. 플레이어 프리팹에 `FootstepPlayer` + `HeartbeatSfx` 부착 (Prefab Edit 모드에서).
   **루트에 직접 붙여도 되고, 오디오용 자식 오브젝트(예: `Player/Audio`)에 모아 붙여도
   된다** — 둘 다 `GetComponentInParent`로 참조를 찾는다(자기 자신 포함이라 루트 부착도
   동작). 부모 계층에서 `PlayerController`/`Rigidbody2D`를 못 찾으면 `FootstepPlayer`가
   에러를 찍고 스스로 비활성화하므로 조용히 죽지 않는다
4. `Assets/Audio/SFX/` 폴더 생성 → 음원 투입 → `Tools/Sound/Rescan SFX Folder`

`SoundManager`·`AmbienceDirector`는 **씬에 배치할 필요가 없다** (자동 생성).

### 구현 중 설계와 달라진 것

- **유물 공통 발동음의 훅 위치**: 설계는 `RelicInputHandler`를 지목했으나, 이 클래스는
  슬롯 번호만 알고 `RelicID`를 모른다. `RelicManager.ActivateSlot()`으로 옮기고,
  전용음 보유 여부는 `RelicBehaviour.HasOwnActivationSfx` **가상 프로퍼티**로 판별한다
  (`RelicID` 목록 하드코딩보다 이 코드베이스의 `public virtual` 패턴에 맞고, 새 유물이
  자기 소리를 가질 때 매니저를 안 건드려도 된다).
- **`JetpackRelic`은 override하지 않았다.** 패시브 유물이라 `OnActivate`/`GetCooldown`
  override가 없고 `OnUpdate`로만 동작한다 → `ActivateSlot`이 패시브 슬롯에서 조기
  return하므로 공통음 경로를 아예 안 탄다. override를 넣으면 죽은 코드다.
- **`OneWayPortalRelic`도 override하지 않았다** (의도). 토글 조기종료 분기가
  `OnActivate` 경로 전에 `return`하므로, 공통음을 `OnActivate` 성공 경로에만 두면
  **설치=공통음 / 귀환=전용음**이 자동으로 갈린다. §4-3의 배분 그대로다.
- **`UpgradeOverlayUI.unlockSfxName`의 레거시 기본값** `"ui_click"`은 어디에도 등록되지
  않은 키라 지금까지 무음이었다. 필드 기본값을 `SfxKeys.UpgradeUnlock`으로 바꾸고,
  씬·프리팹에 구워진 `"ui_click"`은 호출부에서 미지정으로 취급해 폴백시킨다.
- **`CodeUIKit.PlaySfx`가 미등록 키도 흡수한다.** 빈 이름뿐 아니라 등록되지 않은 키
  (레거시 `"ui_click"` 등)도 `SfxKeys.UiButton`으로 폴백한다 → 코드 생성 UI의 모든
  버튼이 별도 설정 없이 소리를 낸다.
- **`PlayerStat`의 스태미나 프로퍼티**는 `CurrentStamina` / `MaxStamina`가 맞았다.

---

## ⚠ 동작이 바뀌는 지점

회귀가 의심되면 여기부터 본다.

1. **`SoundManager`의 SFX 소스가 1개 → 풀 10개로 바뀐다.** 기존 `PlaySFX(string)` 호출부
   (마켓·코인·`CodeUIKit`·`WorldMapOverlay`·`SettingsOverlayUI`)는 시그니처가 그대로라
   컴파일은 깨지지 않지만, **재생 소스가 매번 달라진다.** 이전에는 단일 소스라 새 소리가
   이전 소리를 pitch까지 덮어썼는데 이제 독립적으로 울린다. 마켓 UI 효과음이 이전보다
   겹쳐 들리면 이 변경 때문이다 — `MarketUISfx`는 자체 `_source`를 쓰므로 영향받지
   않지만, `sm.PlaySFX(clipName)` 폴백 경로를 탈 때는 영향받는다.

2. **`BGMCycleManager`의 볼륨 동작은 바뀌지 않는다.** `maxVolume = 0.4f`는 그대로다.
   (구현 도중 이걸 걷어내고 믹서에 맡기려다 되돌렸다 — §2-3 ⚠ 참조. 믹서의 BGM 그룹
   볼륨은 노출 파라미터 `BGMVolume` 그 자체라 런타임에 덮어써지므로 정적 트림을
   둘 수 없다.) 지상 BGM 체감 볼륨은 이전과 동일하다.

3. **`secondarySource`가 이제 BGM 믹서 그룹을 탄다.** 이전에는 그룹이 비어 있어 BGM
   볼륨 슬라이더를 무시했다(버그). 슬라이더를 0으로 내려도 크로스페이드 후반에 소리가
   들리던 현상이 사라진다.

---

## 6. 검토했지만 하지 않은 것

- **앰비언스 전용 볼륨 슬라이더** — `SettingsOverlayUI`가 pending 값을 '적용'에서만
  커밋하는 구조라 슬라이더 추가에 `_p*` 필드·위젯·커밋 경로 3곳을 건드려야 한다. 앰비언스는
  유저 체감상 배경음이라 BGM 슬라이더에 묶어도 자연스럽고, 나중에 분리해도 믹서 계층은
  그대로 쓸 수 있다.

- **`BGMCycleManager`를 `SoundManager`로 흡수** — 크로스페이드 로직이 이미 정상 동작하고
  지상 전용이다. 층별 BGM이 생기는 시점에 통합하는 편이 낫다.

- **개별 컴포넌트의 인스펙터 `AudioClip` 필드 전면 제거** — `IceBreakable`,
  `FragileIceBlock`, `AudioCollapseEffect` 등 11개 파일이 이미 인스펙터 참조로 동작한다.
  이번 작업에서는 **키 경로를 폴백으로 추가만** 하고 기존 필드는 남긴다. 인스펙터에 클립이
  물려 있으면 그것을, 없으면 `SoundManager` 키를 쓴다. 프리팹에 이미 연결된 참조를 깨지
  않기 위해서다.

- **음원 랜덤 변주** — §2-1 참조. 한 사운드에 한 클립으로 확정한다.

---

## 7. 관련 문서

- `Assets/Docs/market-scene/design.md` — `MarketUISfx`의 3단 폴백 패턴(이 설계의 원형)
- `Assets/Docs/elevator-landing-room.md` — `ElevatorManager.TeleportRoutine` 구조
