# 발광 광물돌 — 에디터 셋업 가이드

코드(Task 1~4)는 완료됨. 이 문서는 **사람이 Unity 에디터에서** 머티리얼·프리팹을 만들고 특수청크에 배치하는 절차다.

관련 설계: [design.md](design.md) · 구현 플랜: [plan.md](plan.md)

---

## 1. 스파클 파티클 + Additive 머티리얼 자동 생성 ⭐

메뉴 한 번이면 파티클 프리팹과 Additive 머티리얼이 코드로 생성된다. (수동 제작 불필요)

```
메뉴: Tools > Mineral Rock > Create Sparkle VFX
```

생성 결과:
- `Assets/Prefabs/VFX/MineralSparkle.prefab` — 스파클(트윙클) 파티클
- `Assets/Prefabs/VFX/MineralSparkleAdditive.mat` — URP Particles/Unlit + **Additive** 블렌딩 (자체발광)
- `Assets/Prefabs/VFX/MineralGlowRadial.png` — 소프트 원형 텍스처 (자동 생성)

> **어둠 위에 그려짐**: 스파클 렌더러는 기본 정렬레이어 **Default / Order in Layer 1001**(어둠 오버레이 999 위)로 설정돼, 어두운 동굴에서 빛나 보인다. (이게 "어둠 속 반짝임"의 핵심)
> **Shape/Size 조정**: 스파클 Box(0.6×0.6)는 돌 크기에 맞춰 인스펙터에서 미세조정.
> 발광색은 런타임에 `MineralRockGlow.glowColor`가 `startColor`로 덮어쓰므로 파티클 색은 흰색으로 둬도 된다.

---

## 2. 광물돌 프리팹 생성

### 루트 GameObject
- `SpriteRenderer`
  - Sprite: 광물돌 스프라이트
  - Sorting Layer: **`ground`**, Order in Layer: **-1** (지형 텍스처 뒤 → 파진 구멍으로만 노출)
- `PolygonCollider2D`
- **Rigidbody2D 없음** (CLAUDE.md 제약 #9)

### 부착 컴포넌트 (5개)
| 컴포넌트 | 설정 |
|---|---|
| `DamageStagedVisuals` | (균열 단계 스프라이트는 기존 바위와 동일하게) |
| `RockBreakVFX` | Fragment Sets 등 기존 바위 방식대로 |
| `DiggableRock` | **`preExposed = true`**, **`minExposedPixels = 0`** (제약 #9). HP는 Inspector 무시 — JSON에서 주입됨. preExposed=true면 렌더러가 계속 켜져 묻힌 부분은 지형(order 0)에 가려지고 튀어나온 부분만 보임 |
| `MineralRock` | **`ore`** ← 이 돌이 드롭할 `MineralSO` 할당 (예: Copper SO) |
| `MineralRockGlow` | **`sparkleSystem`** ← MineralSparkle, **`glowColor`** ← 광물 색 (`glowSystem`은 비워둠 — 선택 헤일로용) |

> `MineralRock`은 `[RequireComponent(typeof(DiggableRock))]`이라 DiggableRock이 없으면 부착 시 자동 추가/경고된다.

### 자식 ParticleSystem (스파클)
1번에서 생성한 `MineralSparkle.prefab`을 광물돌 루트의 **자식으로 드래그**(돌 중심 부근에 배치).
파티클 설정(loop·rate·트윙클 커브·Additive·정렬 Default/1001)은 이미 코드로 구성돼 있으니 그대로 쓰면 된다.

루트 `MineralRockGlow`의 `sparkleSystem`에 이 `MineralSparkle`을 연결. (`glowSystem`은 비워둠)

필요 시 스파클 Box 크기만 돌에 맞게 조정.

---

## 3. 특수청크에 배치 (CLAUDE.md 제약 #8)

> ⚠️ 씬 인스턴스에 드래그 후 "Apply to Prefab" 하면 local position이 틀어진다. 반드시 아래 방식.

1. Project 창에서 대상 **특수청크 프리팹을 더블클릭** → Prefab Edit 모드 진입
2. 광물돌 프리팹을 특수청크의 **자식으로 배치**
3. local position 유효 범위 확인: **X(0~10), Y(0~10)** (청크 1칸 = 10유닛)

---

## 4. JSON 값 설정 (`Assets/StreamingAssets/specialChunkSettings.json`)

`mineralRock` 섹션에서 광물별 HP·드롭 개수 조정 (재빌드 불필요):

```json
"mineralRock": {
  "defaultMaxHp": 8.0,
  "defaultMinDrop": 2,
  "defaultMaxDrop": 4,
  "overrides": [
    { "mineralID": "Copper", "maxHp": 6.0,  "minDrop": 2, "maxDrop": 4 },
    { "mineralID": "Iron",   "maxHp": 10.0, "minDrop": 1, "maxDrop": 3 }
  ]
}
```

- `mineralID`는 `MineralSO.mineralID`(enum)의 이름과 **정확히 일치**해야 한다 (예: `Copper`).
- `overrides`에 없는 광물은 `default*` 값이 적용된다.

---

## 5. 수동 검증 체크리스트

- [ ] (preExposed=true 잼바위) 배치 즉시 렌더러가 보이고 스파클이 발광한다 (땅에 묻힌 부분은 지형에 가려짐)
- [ ] 어두운 동굴에서 광물돌이 파티클로 발광하며 도드라진다
- [ ] 곡괭이/드릴로 채굴 시 JSON의 `maxHp`만큼 버티고 파괴된다
- [ ] 파괴 시 지정 광물이 `[minDrop, maxDrop]` 범위 개수로 드롭된다
- [ ] (preExposed=false로 테스트 시) 묻혀있는 동안 발광 OFF → 인접 파기로 노출되면 발광 ON
- [ ] `specialChunkSettings.json`의 maxHp/minDrop/maxDrop을 바꾸고 재실행하면 반영된다
- [ ] EditMode 테스트(`MineralRockSettingsTests`) 4개 PASS (Unity Test Runner에서 직접 실행)
