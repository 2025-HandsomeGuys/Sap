# 별자리 한붓그리기 퍼즐 설계

기존 「별빛 잇기」(집합 일치) 퍼즐을 **한붓그리기(Euler trail)** 퍼즐로 전환한다.
정해진 별자리 도형을 펜을 떼지 않고 각 선을 한 번씩만 그어 완성하는 방식.

관련 파일: `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/SpaceLayer/`

---

## 1. 퍼즐 규칙

- `StarPuzzleManager.targetConnections`(List&lt;StarEdge&gt;)가 세 가지 역할을 겸한다:
  1. 그려야 할 **별자리 도형** 정의
  2. 시작 시 화면에 보이는 **흐린 가이드 선**
  3. **정답** 집합 (전부 그으면 클리어)
- **펜(pen)** 개념: 현재 펜이 놓인 별에서 이어진 선만 그을 수 있다.
- **유효한 수** = "현재 펜 별과 가이드 선으로 직접 연결되어 있고 & 아직 안 그은 선".
- **무효한 수**(연결 안 된 별 / 이미 그은 선) = 무시(no-op) + 경고음. 규칙을 손으로 어길 수 없다.
- **막다른 길**: 현재 펜 별에서 그을 수 있는 선이 없는데 도형이 미완성 → **자동 전체 리셋**.
- 모든 가이드 선을 다 그으면 → `_isSolved = true`, `onPuzzleSolved.Invoke()`.

### 시작점
별도로 강제하지 않는다. 플레이어가 아무 별에서나 시작할 수 있고, 잘못된 시작점을 고르면
결국 막다른 길에 걸려 자동 리셋되므로 규칙상 자연스럽게 걸러진다.

---

## 2. 인터랙션 흐름 (`HandleButtonInteracted` 재작성)

```
버튼 눌림(targetNodeId)
 └─ _isSolved면 무시
 └─ targetNodeId가 _nodeMap에 없으면 경고 후 무시
 └─ 펜 없음(_currentNodeId == null)
 │    → 이 별을 시작점으로 펜 배치 (SetIsPen(true))
 └─ 펜 있음
      ├─ 누른 별 == 현재 펜 별
      │    → 아직 한 선도 안 그었으면 시작점 취소(펜 해제), 아니면 무시
      ├─ (현재 펜 별, 누른 별) 선이 유효?
      │    ├─ 유효 → 선 점등(SetDrawn), 펜을 누른 별로 이동, 클리어 체크
      │    │           → 미클리어면 막다른 길 체크
      │    └─ 무효 → 무시 + 경고음
```

### 막다른 길 처리
펜 이동 후, `drawnCount < targetCount` 이고 현재 펜 별에서 그을 수 있는
미사용 가이드 선이 하나도 없으면:
1. 짧은 실패 연출(별/선 깜빡임 or 사운드) — 구현 시 딜레이 값 결정
2. `ResetAllConnections()` 호출로 전체 초기화

---

## 3. 파일별 변경

| 파일 | 변경 내용 |
|------|-----------|
| `StarPuzzleManager.cs` | **핵심 재작성**. 2단계 임의 페어링 → 펜 연속 방식. `_firstSelectedNodeId` → `_currentNodeId`(펜). `Start()`에서 가이드 선 일괄 생성. 막다른 길 감지·자동 리셋. 에디터용 한붓그리기 가능성 검증 경고 |
| `StarConnectionLine.cs` | `SetDrawn(bool)` 추가 — 흐린 가이드(dim) ↔ 점등(lit) 상태 전환(색·굵기). `Init` 유지 |
| `StarNodeTile.cs` | **펜 위치 색상**(`penColor`) 추가. `SetIsPen(bool)`. 우선순위 pen > connected > idle. 기존 `selected` 상태는 pen으로 대체 |
| `StarEdge.cs` | 변경 없음 (양방향 무순서 Edge 그대로 사용) |
| `StarButtonTile.cs` | 변경 없음 |

### `StarPuzzleManager` 내부 상태 변화
- 제거: `_firstSelectedNodeId`
- 추가: `_currentNodeId`(펜 위치), 인접 리스트(`Dictionary<string, List<StarEdge>>` 또는 매 판정 시 `targetConnections` 순회), `_drawnEdges`(HashSet&lt;StarEdge&gt;)
- 가이드 선: `Start()`에서 `targetConnections` 전부에 대해 `StarConnectionLine` 생성 후 dim 상태로. `StarEdge → StarConnectionLine` 매핑 보관하여 그을 때 해당 선만 `SetDrawn(true)`.

---

## 4. 안전장치 (재미 보장)

- `Start()`에서 도형의 **홀수 차수 꼭짓점 개수** 계산:
  - 0개 또는 2개 → 한붓그리기 가능 (OK)
  - 그 외 → `Debug.LogWarning`로 "이 도형은 한붓그리기 불가" 경고. 디자이너가 풀 수 없는
    도형을 배치하는 실수를 초기에 잡는다.
- 기존 `ResetAllConnections()` / 리셋 레버 = **수동 리셋**으로 그대로 유지.
  구현: `StarResetLever`(InteractableBlockBase 상속) — `puzzleManager` 참조 1개를 인스펙터에 꽂으면
  E 상호작용 시 `puzzleManager.ResetAllConnections()` 호출. 별도 오브젝트로 씬에 배치.

---

## 5. 유지되는 것

- 버튼 → 별 매핑, `OnButtonInteracted` 이벤트 구독/해제
- `_isSolved` 잠금 (풀린 후 입력 무시)
- `targetConnections` 인스펙터 할당 방식
- `onPuzzleSolved` UnityEvent

---

## 6. 미결(구현 단계에서 결정)

- 막다른 길 자동 리셋 전 실패 연출 종류/딜레이
- 경고음(무효한 수) 사운드 소스 연결 방식
- 펜 이동 순서를 저장/표시할지 여부 (현재 설계엔 불필요 — 판정에 순서 기록 안 씀)

---

## 7. 이터레이션 2 — "걸어서 잇기" + 플레이어 추적 고무줄 선

초기 설계는 지상 버튼(입력)과 하늘 별(시각)이 좌표 분리였다. 이터레이션 2에서 이를
**하나의 물리 정점**으로 통합하고, 진행 중인 선이 플레이어를 실시간 추적하도록 바꾼다.
**로직 코어 `StarStrokeState`는 전혀 바뀌지 않는다** — 변경은 Unity 시각/입력 계층뿐.

### 7.1 새 UX
- 정점(별) = 플레이어가 걸어가 E로 상호작용하는 **하나의 오브젝트**. 선·정점·플레이어가 같은 공간.
- 정점A에서 E → 그 정점에서 선이 나와 **플레이어를 매 프레임 따라옴**(고무줄 프리뷰).
- 정점B에 가서 E → (유효하면) A-B 사이 선 **고정·점등**, 펜이 B로 이동, 새 고무줄이 B에서 플레이어를 따라옴.
- 무효 상호작용 → 무시(고무줄은 계속 현재 펜에서 플레이어 추적). 막다른 길 → 자동 리셋(고무줄·고정선 초기화).
- 도형 힌트인 **흐린 가이드 선은 유지**(정점 간 연결 관계 표시).

### 7.2 정점=버튼 통합
`StarButtonTile`(이미 `InteractableBlockBase` 상속 = 근접 E 상호작용)을 정점으로 확장:
- `targetNodeId` → `nodeId`(이 정점 자신의 id)로 의미 변경. 상호작용 시 자기 `nodeId` 발신(`OnButtonInteracted` 시그니처 유지).
- `StarNodeTile`이 갖던 펜/연결 시각 상태를 흡수: `SetIsPen(bool)`, `SetConnected(bool)`, `penColor`, `connectedColor` 추가.
- `UpdateVisuals()` override — 우선순위 **pen > connected > nearby(근접) > idle**. (base의 idle/nearby 색 활용)
- `StarNodeTile`은 이 퍼즐에서 **미사용**(파일은 유지).

### 7.3 고무줄 프리뷰 선 (신규)
매니저가 LineRenderer 1개(`previewLine`, 인스펙터 할당)를 관리:
- `Update()`에서 펜 활성(`_state.CurrentNodeId != null`)이고 미완성이면 `enabled=true`, `pos0=펜 정점 위치`, `pos1=플레이어 위치`.
- 펜 없음/완성/리셋 → `enabled=false`.
- 플레이어 Transform은 `Start()`에서 `"Player"` 태그로 탐색.

### 7.4 매니저 변경 요약
- `_nodeMap` 값 타입 `StarNodeTile` → `StarButtonTile`(정점). 등록+구독 루프 하나로 통합.
- `SetPen`/`SetConnected`가 정점(StarButtonTile) 메서드 호출.
- 고정 선·가이드 선 위치 소스 = 정점의 world position(플레이어와 같은 공간).
- `Update()` 추가(고무줄 갱신). Drew/리셋/완성 시 프리뷰 상태 반영.

### 7.5 불변
- `StarStrokeState`·`StarStrokeStateTests`·`StarConnectionLine`·`StarEdge` 변경 없음.
- 공개 표면 `targetConnections`/`onPuzzleSolved`/`ResetAllConnections()` 유지.

---

## 8. 이터레이션 3 — 곡괭이(IDiggable) 상호작용

E키 근접 상호작용을 **곡괭이 타격**으로 교체. 정점·리셋 레버를 `InteractableBlockBase`(E키) →
`IDiggable`(곡괭이) 기반으로 재작성. **매니저·로직 코어는 변경 없음**(정점의 `OnButtonInteracted`·
`SetIsPen`·`SetConnected`·`nodeId` 공개 API 그대로).

### 8.1 동작
- 곡괭이 장착 → 마우스로 정점 조준 → 좌클릭(채굴). `PickaxeStrategy.PerformDig()`가
  `OverlapCircleAll` → `GetComponent<IDiggable>()` → `Dig(center, damage, toolIndex=2)` 호출.
- `StarButtonTile.Dig(toolIndex==2)` → 히트 쿨다운(0.2s) 후 `OnButtonInteracted(nodeId, null)` 발신.
  **HP·파괴 없음** — 정점은 스위치일 뿐 안 부서진다.
- `StarResetLever.Dig(toolIndex==2)` → `puzzleManager.ResetAllConnections()`.
- 각 정점/레버는 **같은 GameObject에 Collider2D** 필요(Digger가 `GetComponent<IDiggable>`로 탐색).
- 색 우선순위 pen > connected > idle (근접 nearby 상태 제거).

### 8.2 스태미나 면제 (구현됨)
- 마커 인터페이스 `IPuzzleDiggable : IDiggable`(`_Core/Interfaces/`) 도입. `StarButtonTile`·
  `StarResetLever`가 이를 구현.
- `PickaxeStrategy.PerformDig`에서 `diggable is IPuzzleDiggable`이면 **스태미나 차감(`UseStamina`)과
  MaxStamina 감소(`hitAnyRock` 제외 → `AddDiggingReduction` 미호출)를 건너뛰고** 타격(`Dig`)만 전달.
  → 퍼즐 풀이는 스태미나 비용 없음.
- 이 경로만 처리(정점은 지형 월드에 있어 `PickaxeStrategy`가 유일한 타격 경로). Digger의 던전 전용
  `TryDigRockOnly`는 퍼즐 미대상이라 미처리.

### 8.3 잔여 주의
- `PerformDig`는 히트한 모든 IDiggable에 Dig를 돌림(첫 히트에서 return 안 함) → 한 스윙 반경에
  정점 2개가 겹치면 둘 다 발화. 정점 간격을 스윙 반경보다 넓게 배치해 회피.

---

## 9. 이터레이션 4 — 부분 취소(되감기, Rewound)

그리는 도중 **이미 지나온 정점을 다시 치면 그 지점까지 선을 되돌린다**(부분 취소).
로직 코어에 순서 추적을 추가해 구현. **그리기 우선** — 되감기는 새 선을 그을 수 없을 때만 발동.

### 9.1 규칙 (우선순위)
`TryPress(nodeId)` 순서:
1. 미등장 별 → Ignored
2. 펜 없음 → Started
3. 현재 펜 재선택 → (그은 선 0개면 CancelledStart, 아니면 Ignored)
4. **그리기 우선**: `(펜, nodeId)`가 미사용 유효 선이면 → Drew (지나온 별이어도 긋기)
5. **되감기**: 못 긋고 nodeId가 지나온 정점이면 → 그 지점까지 되돌림 → Rewound
6. 그 외 → Ignored

→ 4가 5보다 앞서므로 시작점으로 돌아와 닫는 도형(삼각형·사각형 등)이 정상 완성됨.

### 9.2 구현
- `StarStrokeState`: `CurrentNodeId`를 순서 리스트 `_path`(펜이 지나온 정점)의 마지막 원소로 파생.
  인접 쌍 `(_path[i], _path[i+1])`이 곧 그은 선. 되감기 = `_path.LastIndexOf(nodeId)` 지점 이후
  선들을 `_drawn`에서 제거하고 `_path` 절단. **여러 번 방문한 정점은 가장 최근 방문 지점까지만** 되돌림.
- `MoveKind.Rewound` 추가. `DrawnEdges`·`Path` 노출.
- `StarPuzzleManager`: `Rewound` 케이스에서 `RefreshVisualsFromState()` — 상태 기준으로 모든 선
  `SetDrawn`, 정점 연결색·펜 재계산. 프리뷰 선은 `Update()`가 새 펜을 따라감.
- 테스트: `Rewind_ToEarlierVertex`, `Rewind_ToStart`, `RevisitViaUndrawnEdge_PrefersDraw`,
  `Rewind_ThenContinue`.

### 9.3 곡괭이 연동
곡괭이로 이미 연결된 별을 치면 `StarButtonTile` → `OnButtonInteracted` → `TryPress` → `Rewound`.
별도 입력 없음(같은 타격 인터페이스로 되감기까지 처리).
