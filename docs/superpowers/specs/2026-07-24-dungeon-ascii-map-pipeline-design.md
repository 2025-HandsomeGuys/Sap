# 던전 ASCII 맵 파이프라인 설계

- 날짜: 2026-07-24
- 상태: 설계 승인 (구현 플랜 대기)
- 목적: 던전(Tilemap 기반)을 텍스트 맵으로 저작(offline authoring)한다. Claude가 텍스트로 레이아웃+오브젝트 배치를 생성하고, 에디터 임포터가 이를 Tilemap 프리팹에 스탬프한다.

---

## 1. 배경 / 컨텍스트

- 던전 = **프리팹 하나 = 던전 하나**. `DungeonOverlayController`가 먼 offset(`0,10000`)에 Instantiate하고 플레이어를 `DungeonEntryPoint`로 텔레포트한다. 씬 로드/저장 불필요.
- 던전 지오메트리는 프리팹 내부 **Unity Tilemap**(플랫포머식 구도)으로 구성된다.
- Claude는 Unity 에디터에서 직접 타일을 붓으로 칠할 수 없다(Tilemap은 좌표+타일에셋 참조로 직렬화됨). 대신 텍스트 맵 생성에 강하므로, "ASCII → Tilemap 스탬프" 파이프라인을 만든다.
- 결과물은 **고정 콘텐츠**(런타임 절차 생성 아님, option A).

### 재활용 가능한 기존 자산
- 던전 오브젝트: `DungeonEntryPoint`, `DungeonExitInteractable`, `DungeonRewardPickup`, `DungeonRock`
- 위험(주로 청크용으로 제작됨): `StalactiteTrap`(낙하), `RollingRockTrap`(굴러오는 바위), `DamageZone`/`SpikeZone`(정적 접촉 데미지), `LavaMovement`(용암)
- 타일 에셋: `Assets/Sprites/World/SpeciaChunk/LegacyTiles/` (Wall, Rock, Stone, Dirt, Ice, Magma, Bedrock 등 RuleTile/Tile)

---

## 2. 파이프라인 구조

```
텍스트 맵 파일 (.txt, Assets/DungeonMaps/*.txt)
      │  ← Claude가 생성/편집
      ▼
DungeonTilesetSO (ScriptableObject)   ← 심볼 → 타일/프리팹 매핑 테이블 (1회 세팅)
      │
      ▼
DungeonMapImporter (에디터 툴)
      │  텍스트 파싱 → 셀마다 Tilemap.SetTile() + Instantiate(프리팹) + 링크 배선
      ▼
완성된 던전 프리팹 (DungeonOverlayController가 사용)
```

**설계 원칙**: 심볼 매핑을 코드가 아닌 **에셋(SO)** 에 둔다 → 타일/함정이 늘어나도 코드 수정 없이 인스펙터에서 추가. 이후 맵 제작은 순수 텍스트 편집만으로 끝난다.

### 컴포넌트 경계
- `DungeonTilesetSO` — 순수 데이터. char→TileBase, char→(prefab, zRotation, 옵션) 매핑 두 리스트. 다른 컴포넌트에 의존하지 않음.
- `DungeonMapParser` — 순수 C#. 텍스트 문자열 → 검증된 중간 표현(그리드 3종 + 메타). 에디터/런타임 무관, 단위 테스트 가능.
- `DungeonMapImporter` (Editor) — 파서 결과 + SO를 받아 실제 Tilemap/씬 오브젝트에 스탬프. Unity 에디터 API 의존.

---

## 3. 텍스트 맵 포맷

한 셀에 "바닥 타일"과 "그 위 오브젝트"가 동시에 존재할 수 있으므로 **정렬된 3-레이어**로 분리한다.

```
# dungeon: cave_01
# size: 24x12
# cell: 1

[TILES]
WWWWWWWWWWWWWWWWWWWWWWWW
W....................W.W
W....WWWW...........W...W
...(TILES 격자, 윗줄=천장/위, 아랫줄=바닥)...

[OBJECTS]
........................
....E.........r......X..
...(OBJECTS 격자, TILES와 동일 크기)...

[LINKS]
........................
....1........1......2..
...(선택 — 연동 그룹, TILES와 동일 크기)...
```

### 규칙
- 세 블록은 **행·열 수가 동일**해야 한다. 불일치 시 임포터가 에러를 내고 중단.
- `.` = 빈 칸(아무 것도 안 함).
- `[TILES]` 문자 = TileBase. 예: `W`=Wall, `R`=Rock, `D`=Dirt, `I`=Ice, `M`=Magma.
- `[OBJECTS]` 문자 = 프리팹(§4 표).
- `[LINKS]` 숫자 = 링크 그룹 ID, `.` = 링크 없음. **선택 블록**(없어도 됨).
- 텍스트 **맨 윗줄 = 월드 위쪽**. 좌상단 셀이 원점.
- `#` 로 시작하는 줄 = 주석/메타데이터(`size`, `cell` 등).

### 좌표 규칙
- 텍스트 `(row, col)` → Tilemap cell `(col, -row)`. 좌상단 = `(0, 0)`.
- 월드 배치는 대상 `Grid`의 `cellSize`·anchor 기준. 오브젝트는 셀 중심에 배치.

---

## 4. 심볼 매핑 & 오브젝트 세트

**DungeonTilesetSO 구조**
```
tileMappings:   [ char symbol, TileBase tile ]
objectMappings: [ char symbol, GameObject prefab, float zRotation, (옵션 필드) ]
```

### 기본 오브젝트 세트 (기존 재활용)

| 심볼 | 프리팹 | 비고 |
|---|---|---|
| `E` | DungeonEntryPoint | 던전당 정확히 1개 (검증) |
| `X` | DungeonExitInteractable | 출구 |
| `$` | DungeonRewardPickup | 보상 |
| `r` | DungeonRock | 캐는 바위 |
| `^` | SpikeZone / DamageZone | 바닥 정적 가시 |
| `v` | StalactiteTrap | 위에서 떨어지는 낙하 함정 |
| `O` | RollingRockTrap | 굴러오는 바위 |
| `L` | LavaMovement | 용암 |

### 신규 함정 (구현 대상, 5종 전부)

| 심볼 | 이름 | 클래스(신규) | 동작 | 연동 |
|---|---|---|---|---|
| `_` | 무너지는 발판 | `CollapsingPlatform` | 밟으면 ~0.3초 뒤 낙하, 잠시 후 복구 | 불필요 |
| `!` | 개폐식 가시 | `RetractingSpike` | 주기적으로 바닥에서 튀어나옴(타이밍 점프). 압력판 연동 가능 | 선택 |
| `>` | 다트 발사기 | `DartTrap` | 벽 부착, 앞으로 투사체 발사(주기 or 압력판 트리거) | 선택 |
| `#` | 압쇄 블록 | `CrusherBlock` | 위/아래 왕복, 끼면 즉사 | 불필요 |
| `P` | 압력판 | `PressurePlate` | 밟으면 연동 그룹의 대상(문/다트/가시) 활성 | **필수** |
| `G` | 게이트(문) | `DungeonGate` | 압력판 연동 시 열림/닫힘 | **필수** |

> 심볼 문자는 예시. 실제 문자는 SO 세팅 시 확정하며 `#`(주석 접두)·`.`(빈칸)과 충돌하지 않게 배정한다. (예: 압쇄 블록 심볼을 `#` 대신 `C` 등으로 재배정 — 구현 플랜에서 확정)

---

## 5. 연동(LINKS) 규칙

- `[LINKS]`의 **숫자 = 링크 그룹 ID**. 같은 그룹의 소스(`P` 압력판)와 타겟(`G` 문 / `>` 다트 / `!` 가시)이 연결된다.
- 1:N 지원 — 같은 숫자를 여러 칸에 두면 한 소스가 여러 타겟 제어.
- 임포터가 그룹별로 소스→타겟 **참조를 직렬화**(인스펙터 연결)한다.
- `[LINKS]` 미존재 시: 다트·개폐가시는 각자의 주기로만 동작, 문·압력판은 배치는 되나 연동 없음(경고 로그).

---

## 6. 임포터 동작 순서

1. 텍스트 로드 → 메타(`# size`, `# cell`) 파싱.
2. TILES/OBJECTS/(LINKS) 블록 **행·열 수 일치 검증** — 불일치 시 에러 후 중단.
3. 대상 Tilemap 초기화(옵션: 기존 clear).
4. TILES 순회 → `tilemap.SetTile(cellPos, tile)`.
5. OBJECTS 순회 → 셀 월드좌표에 프리팹 Instantiate(부모 = 던전 루트).
6. LINKS 순회 → 그룹별 소스↔타겟 참조 배선(직렬화).
7. 검증: `E`(입구) 정확히 1개, 미매핑 심볼 경고 로그, 링크 그룹에 소스/타겟 누락 시 경고.

### 출력 대상
- 실행 시 **현재 열려있는 던전 프리팹(또는 지정한 Grid 오브젝트)** 에 스탬프 → 저장하면 완성.
- 새 프리팹 자동 생성은 옵션으로 추후.

---

## 7. 에러 처리 / 검증

- 블록 크기 불일치, 미정의 심볼, 입구 개수 오류, 고아 링크 그룹 → 파서/임포터가 명확한 메시지로 보고.
- 미매핑 심볼은 치명 오류 아님(경고 + 스킵)로 두어 반복 저작을 방해하지 않는다.
- 파서는 순수 C#이므로 EditMode 단위 테스트로 검증(테스트 실행은 사람이 수행 — 프로젝트 규칙).

---

## 8. 범위 밖 (YAGNI / 추후)

- 런타임 절차 생성(option B) — 이번 범위 아님.
- 새 던전 프리팹 자동 생성/에셋화 — 옵션으로 추후.
- 다층(배경/장식 여러 Tilemap 레이어) — 지금은 단일 지오메트리 Tilemap. 필요 시 `[TILES_BG]` 등 블록 추가로 확장 가능.
- AI 이미지→타일 변환 — 범위 밖.

---

## 9. 열린 항목 (구현 플랜에서 확정)

- 신규 함정 심볼 문자 최종 배정(주석/빈칸과 비충돌).
- 대상 Grid `cellSize`와 `# cell` 메타의 관계(1셀=몇 유닛) 확정.
- 신규 함정 6종(플레이트/게이트 포함) 각각의 파라미터·비주얼 소스.

---

## 버전 관리 참고
이 프로젝트는 UVCS(Unity Version Control)를 사용한다. spec 문서 커밋은 사용자가 UVCS로 수행한다(Claude는 git 명령을 사용하지 않음).
