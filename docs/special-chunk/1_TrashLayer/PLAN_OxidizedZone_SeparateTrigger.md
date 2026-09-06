# Plan: OxidizedZone — 배경 오브젝트 분리 구조 (프리팹 방식)
@tags: special-chunk, oxidized-zone, trigger, plan, prefab, zone-effect

## 목표
OxidizedZone 특수 청크에서 지형 충돌용 PolygonCollider2D와
존 감지용 트리거를 **자식 오브젝트로 분리**하되,
모든 구성을 **프리팹에서 완성**한다 (런타임 생성 없음).

## 현재 문제
- `ZoneEffectTrigger`가 `Collider2D`를 요구 → BoxCollider2D를 청크 루트에 붙임
- 청크 루트에는 지형 충돌용 `PolygonCollider2D`도 필요
- 두 Collider가 같은 GameObject → `GetComponent<Collider2D>()`가 첫 번째만 반환 → 혼선

## 변경 내용
**코드 변경 없음** — 프리팹 구조만 변경.

| 대상 | 변경 |
|------|------|
| `OxidizedZone.cs` | 변경 없음 |
| `ZoneEffectTrigger.cs` | 변경 없음 |
| OxidizedZone 프리팹 | 구조 재편 (에디터 작업) |

## 목표 프리팹 구조

```
OxidizedZoneChunk (Root)
├─ TerrainChunk
├─ PolygonCollider2D (isTrigger=false)   ← 지형 물리
└─ ZoneTrigger (자식 오브젝트)
   ├─ BoxCollider2D (isTrigger=true)     ← 크기·위치 에디터에서 직접 조절
   ├─ ZoneEffectTrigger
   └─ OxidizedZone
```

## 에디터 작업 단계
1. OxidizedZone 프리팹 열기
2. 루트에서 BoxCollider2D + ZoneEffectTrigger + OxidizedZone 제거
3. 자식 GameObject `"ZoneTrigger"` 추가
4. `ZoneTrigger`에 BoxCollider2D(isTrigger=true) + ZoneEffectTrigger + OxidizedZone 부착
5. BoxCollider2D 크기를 청크 영역에 맞게 Inspector에서 설정 (기본 10×10)
6. 프리팹 저장

## 주의사항
- 런타임에서 자식 오브젝트를 `new GameObject()` / `AddComponent<T>()`로 생성하지 않는다 (CLAUDE.md 원칙)
- 크기·위치는 에디터에서 시각적으로 확인하며 조정
- `isStaticSpecialChunk = true` 유지
- PolygonCollider2D는 TerrainChunk 내부에서 자동 관리 → 건드리지 않음
