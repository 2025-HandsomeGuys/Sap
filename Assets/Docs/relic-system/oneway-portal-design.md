# 일회용 포탈 (OneWayPortal, RelicID 4027) — 설계

액티브 유물. 발동 시 현재 위치에 포탈을 설치해두고, 재발동 시 그 포탈로 귀환한다.
포탈은 1회 귀환용 — 귀환하면 소멸하고 쿨다운이 시작된다.

## 결정 사항

| 항목 | 결정 |
|------|------|
| 사이클 | 설치 → (자유 행동, 시간 무제한) → 재발동 귀환 → 포탈 소멸 + 쿨다운 → Ready |
| 쿨다운 | Lv1/2/3 = 540/420/300초 (9/7/5분). **설치 중에는 쿨 없음** — 쿨은 귀환 시점부터 |
| 레벨 강화 | 쿨다운 감소만 |
| 포탈 수명 | 귀환 전까지 무제한. 씬 전환(지상 복귀·수면) 시 자연 소멸 — 액티브 상태는 세이브에 저장하지 않으므로 리셋(쿨다운도 리셋) |
| 세이브 | 포탈 위치 비저장 (지하 세션 한정 아이템) |
| 던전 가드 | `DungeonOverlayController.IsInDungeon`이면 설치·귀환 모두 발동 불가 (던전 탈출 악용/상태 파손 방지) |

## 구현 방식 — 토글형 액티브 (프레임워크 수정 0줄)

GravityFlip에서 검증된 토글 경로 재사용:

- `IsToggle => true`, `GetDuration() => float.MaxValue` (무한 지속)
- **1차 발동** `OnActivate()`: 플레이어 현재 위치에 포탈 비주얼 생성, 위치 기억. Active 상태 진입
- **2차 발동**: `RelicManager.ActivateSlot`의 토글 분기(`CancelToCooldown`) → `OnActiveEnd()` 호출
- `OnActiveEnd()`: `player.position = 포탈위치` + `Rigidbody2D.linearVelocity = zero`(낙하속도 이월 방지) + 포탈 비주얼 파괴. 쿨다운은 상태기계가 자동 시작
- `CanActivate()`: 던전 안이면 false (발동 소모 없음)
- `OnUnequip()`: 포탈 비주얼만 정리, 귀환 없음

주의: duration이 무한이라 자연 만료(`activeEnded` → `OnActiveEnd`) 경로는 실질적으로 안 탄다.
종료 진입점은 토글 재입력과 OnUnequip뿐.

귀환 텔레포트는 `ElevatorManager.TeleportPlayer`와 동일하게 `player.position` 직접 세팅 —
InfinityMapManager가 플레이어 주변 청크를 자동 스트리밍하므로 별도 로딩 처리 불필요(검증된 경로).

## 비주얼 (전부 코드 생성, 에셋 불필요)

- **포탈**: LineRenderer 타원 링(세로 긴 타원) + 내부 회전 스월. DrillDrone/Steroid류 코드 생성 패턴.
  설치 시 스케일 팝 등장, 대기 중 느린 회전·펄스
- **귀환**: 출발 지점·도착 지점 양쪽에 짧은 플래시 링(Steroid ShockwaveRing 패턴)
- 비주얼 루트는 behaviour가 직접 생성·파괴 (`ctx.runner` 스폰 대행 불필요한 단순 오브젝트)

## 파일

| 파일 | 작업 |
|------|------|
| `Assets/Scripts/Gameplay/Relics/Behaviours/OneWayPortalRelic.cs` | 신규 |
| `Assets/Scripts/Gameplay/Relics/Data/RelicID.cs` | `OneWayPortal = 4027` 추가 |
| `Assets/Scripts/Editor/RelicSliceAssetGenerator.cs` | `CreateRelic` 1줄 + db 리스트 추가 |
| `Assets/Scripts/Gameplay/Relics/Debug/RelicDebugGranter.cs` | `KeyCode.Home` 바인딩 추가 |

게임 코드(플레이어·지형) 침습 없음.

## 스코프 제외 (후속)

- 미니맵/전체지도 포탈 마커
- 포탈 세이브 영속화
- 설치 취소(귀환 없이 회수) 입력

## 사람 잔여 작업 (Unity)

컴파일 → `Tools > Relic > Generate Slice Assets` 재실행 → Home 키 인게임 검증 → UVCS 체크인
