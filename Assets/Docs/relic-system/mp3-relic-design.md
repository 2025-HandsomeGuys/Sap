# mp3 유물 설계

`adding-a-relic.md` 가이드 기반. RelicID `Mp3 = 4023` (4022 OverloadBattery 다음).

## 1. 정체성

기획: **비트. 머리 위에 바 같이 비트를 알 수 있는 게 생기고, 비트에 맞춰 삽·곡괭이질할 시 파는 범위가 증가.**

- 타입: **패시브**. 인디케이터는 상시 표시, 부스트는 **온비트 멜리 스윙**에만.
- 이득: 비트에 맞춘 삽·곡괭이 스윙의 **파기 반경 배수↑**.
- 인디케이터: 머리 위 어프로치 링이 매 비트마다 타깃 링으로 수축 → 겹치는 순간이 온비트.
- 레벨 스케일: 온비트 반경 배수↑ + 타이밍 허용창↑(레벨↑=관대).
- `maxLevel = 3`. BPM 고정(기본 120).

## 2. 동작 — 기존 훅만 사용(게임 코드 침습 0)

`RelicBehaviour`의 두 기존 훅으로 완결. **게임 코드 신규 훅 불필요.**

1. **`OnDigSwing()`** — 삽·곡괭이 스윙 순간 통지. 드릴 대시는 `RaiseDigSwing`을 발행하지 않으므로
   이 훅 도달 자체가 "멜리 스윙" 게이팅이 된다. 이 순간 인디케이터에 온비트 여부를 질의해
   `_swingOnBeat` 플래그에 기록(+ 성공 플래시).
2. **`ModifyDigParameters(ref p)`** — `_swingOnBeat`이면 `p.RadiusMultiplier`에 레벨별 배수를 곱하고
   플래그를 소비(이번 스윙 1회만).

### 타이밍 근거 (set→consume 정확성)
`SapStrategy.FireMining` / `PickaxeStrategy`는 `RaiseDigSwing()`을 **`GetCurrentDigParameters()` 직전**에
동기 호출한다(코드 주석: "파라미터 조회 전에 발행"). 두 호출 사이에 다른 코드가 끼지 않으므로
`OnDigSwing`에서 세팅한 플래그를 바로 다음 `ModifyDigParameters`가 소비 → 같은 스윙에 정확히 대응.
`DigRangePreview` 등이 매 프레임 `GetCurrentDigParameters`를 불러도, 플래그는 스윙 순간에만 true라
프리뷰가 잘못 소비할 여지가 없다(프리뷰는 기본 반경으로 표시).

## 3. 비트 인디케이터 (`Mp3BeatIndicator`, 프리팹 불필요)

`DashBombRelic`이 런타임에 `LineRenderer`를 만드는 패턴과 동일하게, 코드로 링 2개를 생성.

- 비트 클럭(BPM·시작시각)의 **단일 소스 = 이 뷰**. 유물은 스윙 순간 `IsOnBeat()`만 질의.
- **타깃 링**(고정 반경) = 온비트 히트존. **어프로치 링**이 다음 비트까지 남은 비율만큼 커졌다가
  비트 순간 타깃 링에 겹침(리듬게임 approach-circle).
- 색: 평상시 청록, 비트 경계 펄스(노랑), 온비트 파기 성공 시 초록 플래시.
- 위치: `ctx.mining.headBone`(없으면 `ctx.player`)을 **월드 위치로만 추적**(부모로 붙이지 않아 회전 무시),
  `headOffset`(기본 y+1.7)만큼 위.
- `OnUnequip`에서 GameObject 파괴 + 생성한 머티리얼 정리.

## 4. 레벨 파라미터 (초기값)

```
bpm                  = 120
radiusBoostPerLevel  = { 1.6f, 1.9f, 2.3f }    // 온비트 반경 배수
hitWindowPerLevel    = { 0.12f, 0.15f, 0.18f } // 타이밍 허용(초), 레벨↑=관대
headOffset           = (0, 1.7, 0)
```

## 5. 수정 파일

- **신규**:
  - `Assets/Scripts/Gameplay/Relics/Behaviours/Mp3Relic.cs`
  - `Assets/Scripts/Gameplay/Relics/Behaviours/Mp3BeatIndicator.cs`
- **게임 코드 훅**: **없음**(기존 `OnDigSwing` + `ModifyDigParameters`로 완결).
- **공유(순차 편집)**:
  - `Data/RelicID.cs` — `Mp3 = 4023`
  - `Editor/RelicSliceAssetGenerator.cs` — 등록 1줄 + `db.allRelics` 추가
  - `Gameplay/Relics/Debug/RelicDebugGranter.cs` — `KeypadDivide` grant+equip, slot 0

## 6. 엣지 케이스

| 케이스 | 처리 |
|--------|------|
| 드릴 대시 | `RaiseDigSwing` 미발행 → `OnDigSwing` 미도달 → 부스트 없음(멜리 전용) |
| 오프비트 스윙 | `_swingOnBeat=false` → 반경 배수 미적용 |
| 프리뷰의 매프레임 파라미터 조회 | 플래그는 스윙 순간만 true → 오소비 없음 |
| headBone 미할당 | `ctx.player`로 폴백 |
| 유물 해제 | 인디케이터 GameObject·머티리얼 정리 |

## 7. 검증 (사용자 인게임)

1. `Tools > Relic > Generate Slice Assets` 재실행 → `Mp3.asset` + DB 갱신.
2. `KeypadDivide`로 장착 → 머리 위 링이 비트마다 수축·펄스하는지 확인.
3. 삽·곡괭이로 링이 겹치는 순간 파기 → 초록 플래시 + 파는 범위가 넓어지는지 확인.
4. 오프비트로 파기 → 평소 범위(부스트 없음) 확인.
5. `KeypadDivide` 재입력 Lv↑ → 허용창·반경 배수 증가 체감.
6. 드릴 대시는 범위 변화 없음(멜리 전용) 확인.
