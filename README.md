# Sap

> Unity 2D 픽셀 지형 파괴 채굴 로그라이크

땅을 **픽셀 단위로** 파내려가며 광물을 캐고, 번 돈을 시장에 굴려 장비를 키우고,
더 깊은 땅으로 내려가는 게임. 지형은 스프라이트 타일이 아니라 청크별 1000×1000 픽셀
버퍼로 관리되며, 파기·콜라이더·조명이 Burst Job 파이프라인으로 매 프레임 갱신된다.

<!-- 스크린샷: docs/images/ 에 파일을 넣고 아래 주석을 해제 -->
<!--
| 지하 채굴 | 지상 정착지 | 마켓 단말기 |
|---|---|---|
| ![지하](docs/images/underground.png) | ![지상](docs/images/surface.png) | ![마켓](docs/images/market.png) |
-->

| | |
|---|---|
| **엔진** | Unity 6000.3.2f1 (URP 2D) |
| **언어** | C# (Burst / Job System / NativeArray) |
| **규모** | 스크립트 약 750개 / 약 13.7만 줄 |
| **팀** | 2025-HandsomeGuys |

---

## 코어 루프

```
   [채광] ──여윳돈──▶ [주식] ──복리 이익──▶ [업그레이드/장비]
   바닥 수입          기하급수 엔진           = 더 강해짐
     ▲                                            │
     └──────── 채광 수입↑ (더 깊이·비싼 광물) ◀──────┘

   [코인] ── 급할 때의 지름길(−EV 잭팟) ──▶ 목표 금액 즉시 도달 시도
```

채광이 바닥 수입, 주식이 자본 배수기, 코인이 −EV 지름길로 서로 역할이 갈린다.
설계 근거: [three-system-loop.md](Assets/Docs/economy/three-system-loop.md)

---

## 기술 하이라이트

### 픽셀 단위 파괴 지형

타일맵을 쓰지 않는다. 청크 하나가 1000×1000 픽셀 버퍼이고, 파기는 그 버퍼를 직접
수정한 뒤 **텍스처·콜라이더·조명·거리장**을 각각 다른 주기로 재생성한다.

```
InfinityMapManager (LateUpdate 일괄 처리)
  └─ ProcessDirtyChunksAsync — 6단계 Job 파이프라인
       Step 1  Init 잡 스케줄
       Step 2  Round 1 Visual 잡 스케줄
       Step 3  Round 1 완료 대기 + 조명 합성
       Step 4  이웃 경계 동기화 → Round 2 스케줄
       Step 5  Round 2 완료 대기
       Step 6  텍스처 GPU 업로드
```

콜라이더 갱신은 이 비주얼 경로에서 **의도적으로 분리**돼 있다. 합쳐 두면 드릴로
연속 파기할 때 콜라이더가 영구 차단되는 버그가 난다 —
[collider-visual-decoupling.md](Assets/Docs/collider-visual-decoupling.md)

### 성능 작업 (프로파일러 실측)

| 작업 | 결과 |
|---|---|
| chamfer strip 스코핑 | 터레인 경로 **60ms+ → 1.33ms** (~45×↓) |
| Job 파이프라인 낭비 제거 | 경계 드릴 sync point **~19ms → 0.6~0.9ms**, 워크아이템 5.5M → 50k |
| 청크 로딩 GC 할당 제거 | 청크당 매니지드 할당 **1~6MB → 0** |
| `ChunkData` 메모리 | `CurrentPixels` 제거로 **청크당 4MB 절감** |

전체 이력·미처리 항목·반복해서 걸린 함정: [performance/README.md](Assets/Docs/performance/README.md)

### 절차적 던전 생성

형태 마스크(ASCII) → 미로 배선 → 방 조립 → 입출구 배치 → 공동 메우기 → 슬롯 채우기
순으로 생성한다. 에디터 도구(`Tools/Dungeon/Map Importer`)로 마스크를 직접 그린다 —
[maze-and-shape-design.md](Assets/Docs/dungeon-generation/maze-and-shape-design.md)

### 경제 시뮬레이션

95종목 주식 시장(가우시안 로그워크 + 뉴스 체인 13종 + 시간-램프 평균회귀)과
코인 도박 미니게임(승률 50% 고정, 손익 크기 비대칭으로 EV 음수)을 별도 엔진으로 구현.
밸런스 결정은 전부 문서로 남아 있다 — [Assets/Docs/economy/](Assets/Docs/economy/)

---

## 개발·QA 인프라

포트폴리오 관점에서 이 프로젝트가 공들인 부분.

| 도구 | 설명 |
|---|---|
| **F12 버그 리포트** | 스크린샷 + 게임 상태 + 로그 + 세이브를 한 번에 덤프. 캡처가 오버레이보다 먼저 돌아야 하는 순서 제약이 설계의 핵심 — [문서](Assets/Docs/bug-report-system.md) |
| **디버그 콘솔 + QA 픽스처** | `` ` `` 콘솔, 시간 배속, 세이브 픽스처 즉시 로드 — [문서](Assets/Docs/qa/debug-console-and-fixtures.md) |
| **지형 이음매 감시기** | "청크 사이로 몸이 빠지는" 재현 불가 버그 전용 상시 관찰기. 탐지 4종 + 사고 시 지형/콜라이더 21×21 지도 자동 덤프 — [문서](Assets/Docs/qa/terrain-seam-watchdog.md) |
| **텔레메트리** | 로컬 JSONL 기록 → Python으로 밸런스 CSV 추출 — [문서](Assets/Docs/telemetry/design.md) |
| **EditMode 테스트** | 업그레이드 트리 비용·경로 검증, 앰비언스 선택 로직 등 순수 로직 분리 후 테스트 |

---

## 프로젝트 구조

```
Assets/Scripts/
├── _Core/          매니저·데이터·인터페이스 (GameManager, SaveManager, ChunkData)
├── Gameplay/
│   ├── Terrain/    청크 시스템 — 생성·파기·콜라이더·비주얼
│   └── Dungeon/    절차적 던전 생성
├── Render/         조명·시야·배경
├── Systems/        퀘스트·튜토리얼
├── UI/             플레이어·인벤토리·상점·업그레이드·지도
├── Stock/ Coin/    경제 시뮬레이션
└── Utils/          디버그 콘솔·버그 리포트·진단
Assets/Docs/        설계 결정 문서 (110+ 편)
```

---

## 실행 방법

1. **Unity 6000.3.2f1** 설치 (Unity Hub 권장)
2. 클론 후 Unity Hub에서 프로젝트 열기
3. `Assets/Scenes/Demo/MainMenuScene.unity` 실행

> ⚠️ **폰트는 저장소에 포함되어 있지 않다.** (`Assets/Font/` — 재배포 라이선스 문제로 제외)
> 폰트 없이 실행하면 텍스트가 기본 폰트로 대체되거나 깨진다.

---

## 설계 문서

이 프로젝트는 **UVCS(Unity Version Control)** 로 개발되어 커밋 히스토리에 맥락이 남지
않는다. 그래서 "왜 이렇게 만들었는가"를 전부 `Assets/Docs/` 에 문서로 남겼다.
주요 문서:

- [performance/README.md](Assets/Docs/performance/README.md) — 성능 최적화 전체 이력 + 미처리 항목 + 함정
- [collider-visual-decoupling.md](Assets/Docs/collider-visual-decoupling.md) — 콜라이더/비주얼 분리 배경과 버그 분석
- [job-pipeline-waste-removal.md](Assets/Docs/job-pipeline-waste-removal.md) — Job 중복 제거 설계
- [player-terrain-sorting.md](Assets/Docs/player-terrain-sorting.md) — 플레이어를 지형 뒤로 보내는 정렬 설계
- [rock-prefab-and-hit-animation.md](Assets/Docs/rock-prefab-and-hit-animation.md) — Animator가 배치 회전을 덮는 문제 해결
- [dungeon-generation/](Assets/Docs/dungeon-generation/) — 던전 생성
- [economy/](Assets/Docs/economy/) — 경제 밸런스 결정 전체
- [SYSTEMS.md](SYSTEMS.md) — 구현된 시스템 전체 목록
