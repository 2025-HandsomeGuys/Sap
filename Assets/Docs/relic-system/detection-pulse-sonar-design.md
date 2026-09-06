# 탐지파동 유물 — 소나 속도 + 미니맵 발견 알림 설계

작성일: 2026-07-19

기존 `DetectionPulseRelic`의 두 가지 개선:

1. 파동 링이 레벨에 따라 속도가 달라지는 문제 → **고정 속도**로 전환
2. 발견한 특수청크가 미니맵 시야 밖이면 아무 피드백이 없는 문제 → **발견 순간 3초 방향 알림**(반짝임 + "띵")

---

## 1. 현재 동작과 문제

| 위치 | 현재 | 문제 |
|------|------|------|
| `DetectionPulseRelic.cs:16` | `pulseDuration = 0.8f` 고정 | 링이 `0.2 → maxR`을 항상 0.8초에 주파. **레벨이 올라 반경이 커질수록 링이 빨라진다** — 소나 느낌이 깨짐 |
| `UndergroundMinimap.cs:390` | `if (local.magnitude > radiusPx) continue;` | 탐지 반경(4~6청크 = 40~60유닛)이 미니맵 시야보다 넓어, 발견해도 **미니맵에 아무것도 안 뜨는 경우가 대부분** |

---

## 2. 소나 고정 속도

`DetectionPulseRelic`의 `pulseDuration`을 제거하고 `pulseSpeed`(유닛/초)로 대체한다.

```csharp
[SerializeField] private float pulseSpeed = 35f;   // 유닛/초
...
float duration = Mathf.Max(0.3f, maxR / pulseSpeed);
```

- 링 확장 루프 자체는 변경 없음 (`t / duration`으로 보간)
- 레벨 무관하게 링 속도 일정 → 레벨3(60유닛) ≈ 1.7초, 레벨1(40유닛) ≈ 1.1초
- `0.3f` 하한은 `maxR`이 비정상적으로 작을 때의 방어
- `maxR`은 탐지 반경뿐 아니라 **발견된 가장 먼 청크까지의 거리**로도 늘어난다(기존 로직 유지). 고정 속도에서는 이게 오히려 자연스럽다 — 멀리 있는 것을 잡은 파동일수록 더 오래 퍼진다

**기본값 35는 체감용 초기치.** 인스펙터에서 조정한다.

---

## 3. 알림 채널 — `DetectedChunkStore.OnPinged`

```csharp
/// <summary>파동 링이 이 좌표를 훑고 지나간 순간. 순수 연출 신호.</summary>
public event Action<Vector2Int> OnPinged;

public void RaisePinged(Vector2Int coord) => OnPinged?.Invoke(coord);
```

기존 `OnChanged`(데이터 변경 알림)와 **의도적으로 분리**한다:

- `OnChanged` — 세이브 로드/`Apply()`에서도 발행됨. 여기에 연출을 붙이면 게임 시작 시 전부 울린다
- `OnPinged` — 파동 발동 시에만, 좌표별로 1회

발행 지점은 `PulseVFX`의 기존 핑 발사 분기(`DetectionPulseRelic.cs:112-114`). 링 반경이 그 청크 거리를 넘어서는 순간이므로 **가까운 것부터 순차로** 발행된다.

### 결정: 영구 마커는 소나보다 먼저 떠도 그대로 둔다

`OnActivate`는 `Report()`를 **전부 먼저 돌린 뒤** `PulseVFX`를 시작한다(`DetectionPulseRelic.cs:42-59`). 따라서 미니맵 시야 안의 특수청크는 링이 훑기 전에 영구 마커가 먼저 나타나고, 이후에 "띵"과 반짝임이 따라온다. "소나가 훑고 지나가며 하나씩 드러난다"는 연출이 시야 안에서만 어긋난다.

**의도적으로 수정하지 않는다.** 검토한 대안:

- `Report()`를 핑 타이밍까지 미루면 — 파동 도중 씬 전환·사망 시 발견 기록이 유실된다. 연출을 위해 데이터를 위험에 빠뜨리는 교환이라 받지 않는다
- 미니맵이 "핑 전에는 안 그림" 상태를 따로 들면 — 데이터는 안전하지만 미니맵 상태가 하나 늘어난다. 이득 대비 복잡도가 크다

시야 밖(=탐지 반경상 대부분의 경우)은 이번에 추가하는 3초 알림이 소나 타이밍대로 돌기 때문에 의도한 연출은 유지된다. **데이터 기록(`Report`)과 연출(`OnPinged`)의 분리를 유지하는 쪽을 택한다.**

---

## 4. 소리 — `DetectionPingSfx` (신규)

`Assets/Scripts/Gameplay/Relics/Behaviours/DetectionPingSfx.cs`. 정적 헬퍼.

### 재생 경로

- `DontDestroyOnLoad` 오브젝트에 **AudioSource 1개**를 지연 생성
- **캐시한 AudioSource는 매 호출 null 체크 후 필요 시 재생성한다.** Enter Play Mode Options로 도메인 리로드를 끄면 static 참조가 파괴된 오브젝트를 가리킨 채 살아남는다 (Unity의 "가짜 null")
- **`outputAudioMixerGroup = SoundManager.Instance.sfxGroup` 필수.**
  이 프로젝트의 볼륨은 `AudioMixer` 파라미터로 제어된다(`SoundManager.cs:30-51`, `SetMixerVolume`). 라우팅을 빠뜨리면 **마스터/SFX 볼륨을 0으로 내려도 이 소리만 계속 울린다.**
  `SoundManager.Instance`가 아직 없으면 재생을 스킵한다(경고 없이).

### 음원

사인파 "띵"을 `AudioClip.Create`로 최초 1회 합성해 캐시한다(지수 감쇠 엔벨로프, ~0.25초). 에셋 불필요 — `MarketUISfx`와 동일한 접근.

`SoundManager.HasSFX("relic_detect_ping")`이 true면 그 클립을 우선 사용한다. 나중에 `SoundDataSO`에 클립만 등록하면 자동 교체된다.

### 겹침 방지 (3중)

여러 청크를 동시에 발견해도 소리가 합산되어 커지지 않아야 한다.

1. **단일 보이스 재리트리거** — `PlayOneShot`을 쓰지 않는다. `Stop()` → `pitch` 설정 → `Play()`. 한 소스가 한 번에 하나만 내므로 **진폭 합산이 구조적으로 불가능**
2. **최소 간격 0.12초** — 직전 재생과의 간격이 그보다 짧으면 재생을 건너뛴다. 같은 프레임에 여러 좌표가 발견되어도 "띵" 1회로 흡수된다
3. **인덱스 볼륨 감쇠** — 한 파동 내 순번 `i`에 대해 `volume = Mathf.Max(0.35f, 1f - i * 0.12f)`. 하한을 두어 뒤쪽 핑이 완전히 묻히지 않게 한다

### 순번과 피치

순번마다 피치를 +0.06 올린다(최대 1.5) → "띵 딩 딩↗" 상승감.

**`BeginSequence()`를 `PulseVFX` 시작 시 호출해 순번을 0으로 리셋한다.** 리셋하지 않으면 순번이 발동을 넘어 누적되어 두 번째 발동부터 항상 최고음이 된다.

### 호출 위치

**relic 쪽(`PulseVFX`)에서 직접 호출한다.** 미니맵이 비활성이거나 씬에 없어도 소리는 나야 하므로, 미니맵 이벤트 핸들러에 두지 않는다.

---

## 5. 미니맵 반짝임 — `UndergroundMinimap`

### 구독 라이프사이클

현재 이 컴포넌트에는 `OnEnable`/`OnDisable`이 **없다.** 새로 추가한다.

```csharp
void OnEnable()  => DetectedChunkStore.Instance.OnPinged += OnDetectPinged;
void OnDisable() => DetectedChunkStore.Instance.OnPinged -= OnDetectPinged;
```

`DetectedChunkStore`는 앱 수명 싱글톤이다. 해제하지 않으면 씬 전환 후 파괴된 미니맵이 이벤트에 매달려 `MissingReferenceException`을 던진다. 마켓 씬이 Additive로 열리는 구조라 실제로 밟을 확률이 높다.

### 알림 상태

```csharp
private readonly Dictionary<Vector2Int, float> _announceUntil = new();
private const float AnnounceDuration = 3f;
```

`OnDetectPinged(coord)` → `_announceUntil[coord] = Time.time + AnnounceDuration`.

만료 항목은 정리해야 한다. 안 하면 세션 내내 좌표가 쌓인다. 단, **`foreach` 도중 `Dictionary.Remove`를 호출하면 `InvalidOperationException`이 난다** — 그것도 알림이 끝나는 정확히 그 순간에만 터져 재현이 어렵다.

→ 만료 키를 재사용 `List<Vector2Int>` 필드에 모았다가 **순회가 끝난 뒤 일괄 제거**한다. 매 프레임 도는 경로이므로 리스트는 필드로 캐시해 GC를 피한다.

### 표시 규칙

알림 3초 동안, **시야 안/밖 판정은 매 프레임 재계산한다.** 발견 시점에 고정하지 않으므로, 플레이어가 움직이면 가장자리 마커 ↔ 영구 마커로 자연스럽게 전환된다.

| 상황 | 처리 |
|------|------|
| 시야 **안** (`local.magnitude <= radiusPx`) | 기존 영구 마커를 그대로 사용. 스케일·알파에 sin 펄스를 얹는다. 새 오브젝트를 만들지 않는다 |
| 시야 **밖** | 별도 풀에서 임시 마커를 꺼내 `local.normalized * radiusPx * 0.92f` 위치에 배치(방향 클램프). 같은 sin 펄스. 만료 시 페이드아웃 후 반납 |

가장자리 마커 풀도 **생성 시 `_generated`에 등록한다.** 기존 코드가 생성한 오브젝트를 `_generated`로 일괄 정리하고 있어, 빠뜨리면 미니맵 재빌드 때 유령 마커가 남는다.

기존 `continue` 스킵 로직은 유지한다 — 영구 마커는 지금처럼 시야 안에서만 보이고, 가장자리 마커는 **발견 순간 3초짜리 임시 표시**로만 존재한다.

### 스케일 리셋 (필수)

영구 마커 풀은 dictionary 순회 순서대로 인덱스가 배정되므로, **같은 `RectTransform`이 프레임마다 다른 좌표를 담당할 수 있다.** 반짝임을 `localScale`로 주면 알림이 끝난 뒤 그 오브젝트를 물려받은 마커가 부푼 상태로 남는다.

→ 매 프레임 스케일을 **명시적으로** 세팅한다. 알림 중이면 펄스값, 아니면 `Vector3.one`.

---

## 6. 범위 밖

- `WorldMapOverlay`(전체지도) — 변경 없음
- 탐지 반경·쿨타임·예측 로직 — 변경 없음
- `ctx.runner == null`이면 VFX와 소리 모두 나지 않는다. runner는 유물 시스템이 항상 주입하는 값이므로 별도 폴백을 두지 않는다

---

## 7. 검증

Unity Test Runner 자동화 대상이 아니다(연출·UI). 플레이 모드에서 사람이 확인한다.

1. 레벨 1 / 레벨 3에서 각각 발동 → **링 확산 속도가 눈으로 같아 보이는지**
2. 특수청크 여러 개가 한 번에 잡히는 지점에서 발동 → **소리가 순차로 나고, 겹쳐 커지지 않는지**
3. 미니맵 시야 밖 특수청크 발견 → **가장자리에 방향 마커가 3초 뜨고 사라지는지**
4. 알림 중 플레이어를 그 방향으로 이동 → 가장자리 마커가 영구 마커로 전환되는지
5. 설정에서 SFX 볼륨 0 → **"띵"이 같이 꺼지는지**
6. 알림 직후 다른 씬(마켓 등) 진입 → 예외가 나지 않는지
7. 발동 2회 연속 → 두 번째도 낮은 음부터 시작하는지
