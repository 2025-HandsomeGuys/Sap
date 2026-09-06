# T0 세로 스파인 재배치 + 엘리베이터 필수 편입 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** T0 업그레이드 트리를 필수 13노드 세로 스파인으로 재배치하고, 엘리베이터·단말기를 필수 경로에 편입해 1층 체류를 11일 → 14일로 늘린다.

**Architecture:** 변경은 사실상 `UpgradeTreeGenerator.cs`의 데이터 테이블 한 곳에 모여 있다. 노드 정의(비용·좌표·선행)를 고치고 Unity 에디터 메뉴로 `.asset`을 재생성하면 런타임이 그대로 따라온다. 게임플레이 코드는 건드리지 않는다. 검증은 EditMode 테스트(`UpgradeTreeCostTests`)가 총액·최소경로·구매리듬·레이아웃 규칙을 전부 잡는다.

**Tech Stack:** Unity 2D / C# / NUnit(EditMode) / Python 3(정적 검증 스크립트, 표준 라이브러리만)

**Spec:** [Assets/Docs/superpowers/specs/2026-08-15-t0-spine-and-elevator-design.md](../specs/2026-08-15-t0-spine-and-elevator-design.md)

## Global Constraints

- **이 프로젝트는 UVCS(구 Plastic SCM)를 쓴다. `git` 명령을 쓰지 않는다.** 표준 플랜의 "Commit" 스텝은 전부 **체크포인트**(정적 검증 실행 + 결과 확인)로 대체한다.
- **Unity Test Runner 실행은 사람이 직접 한다.** 에이전트는 테스트 파일을 작성·수정만 하고 `mcp__mcp-unity__run_tests` 등을 호출하지 않는다. 각 태스크의 자동 검증은 **Python 정적 검증**으로 하고, Unity 실행은 Task 7에 사람 작업으로 모아 둔다.
- **`Tools/Upgrade/Upgrade Tree Generator` 실행도 사람이 한다** (Unity 에디터 메뉴). 에이전트는 생성기 소스만 고친다.
- 수입 모델 상수는 **`base 60 / growth 64`** 고정(선행 스펙 §4.3-2 실측값). 가격을 바꿀 일이 생기면 이 상수부터 재적합한다.
- 노드의 **효과 종류·값·isPercentage는 이번 작업에서 일절 바꾸지 않는다.** 바뀌는 것은 비용·좌표·선행·티어뿐이다.
- 파일 편집은 Bash(`python`/`sed`)로 한다. 긴 마크다운·C# 블록처럼 인용부호가 얽히는 경우에만 편집 도구로 넘어간다.

## 확정 수치 (모든 태스크가 이 표를 기준으로 한다)

**T0 필수 13노드** — 최소 경로 **5,830G**, 14다이브

| # | nodeId | 비용 | 선행 | uiPosition |
|---|---|---|---|---|
| 1 | `MiningRange_T0_01` | 60 | — | (0, -350) |
| 2 | `ShovelStamina_T0_01` | 110 | `MiningRange_T0_01` | (-350, -200) |
| 3 | `MiningSpeed_T0_01` | 170 | `MiningRange_T0_01` | (0, -200) |
| 4 | `ToolRange_T0_01` | 230 | `MiningRange_T0_01` | (350, -200) |
| 5 | `MaxStamina_T0_01` | 290 | `ShovelStamina_T0_01` | (-350, -50) |
| 6 | `StaminaCost_T0_01` | 350 | `MiningSpeed_T0_01` | (0, -50) |
| 7 | `MaxStaminaMultiplier_T0_01` | 410 | `ToolRange_T0_01` | (350, -50) |
| 8 | `PickaxeUnlock_T0_01` | 470 | `MaxStamina_T0_01`, `StaminaCost_T0_01`, `MaxStaminaMultiplier_T0_01` | (0, 100) |
| 9 | `Facility_Elevator_T0` | 530 | `PickaxeUnlock_T0_01` | (0, 250) |
| 10 | `PickaxeDamage_T0_01` | 590 | `Facility_Elevator_T0` | (0, 400) |
| 11 | `MiningCooldown_T0_01` | 640 | `PickaxeDamage_T0_01` | (0, 550) |
| 12 | `Facility_Computer_T0` | 700 | `MiningCooldown_T0_01` | (0, 700) |
| 13 | `MiningLevel_T0_Final` | 1280 | `Facility_Computer_T0` | (0, 850) |

**T0 곁가지 5노드** — 합 **1,420G** (선행·좌표 불변, 비용만 변경)

| nodeId | 현재 | 변경 |
|---|---|---|
| `MoveSpeed_T0_01` | 170 | **230** |
| `VisionRadius_T0_01` | 230 | **300** |
| `ClimbSpeed_T0_01` | 170 | **230** |
| `FallDamage_T0_01` | 230 | **330** |
| `WallClimbSpeed_T0_01` | 230 | **330** |

**T0 총액 7,250G** (시설 포함) · **T1 총액 119,900G** (시설 제외, 불변)

---

## File Structure

| 파일 | 책임 | 변경 |
|---|---|---|
| `Assets/Scripts/Editor/UpgradeTreeGenerator.cs` | 트리 전체의 단일 진실 공급원(노드 정의 테이블 + 에셋 생성) | Task 1~4 |
| `Assets/Tests/EditMode/UpgradeTreeCostTests.cs` | 총액·최소경로·구매리듬·구조 규칙 고정 | Task 5~6 |
| `Assets/GameData/UpgradeData/{Node,Effect}/*.asset` | 생성 결과물 | Task 7(사람) |
| 씬의 작업대 `WorldInteractable.unlockNodeId` | 시설 해금 문자열 참조 | Task 7(사람) |

생성기 파일이 이미 700줄 가까이 되지만 **분할하지 않는다.** 노드 테이블은 한 곳에 모여 있어야 사다리와 선행을 한눈에 검산할 수 있고, 이 구조가 기존 문서·테스트의 전제다.

---

## Task 1: T0 필수 노드 비용·좌표·선행 재배치

**Files:**
- Modify: `Assets/Scripts/Editor/UpgradeTreeGenerator.cs` (T0 블록, 약 355~500행)

**Interfaces:**
- Consumes: 없음(첫 태스크)
- Produces: T0 필수 13노드가 위 확정 표와 일치하는 상태. Task 5·6의 테스트 기대값이 이 값에 의존한다.

- [ ] **Step 1: 검증 스크립트를 먼저 만든다 (실패하는 테스트 역할)**

`Tools/upgrade/verify_t0_layout.py`를 만든다. 생성기 소스를 정규식으로 파싱해 확정 표와 대조한다.

```python
#!/usr/bin/env python3
"""UpgradeTreeGenerator.cs의 T0 노드 정의를 확정 표와 대조한다.

Unity를 띄우지 않고 돌릴 수 있는 정적 검증이다. EditMode 테스트가
같은 것을 에셋 기준으로 다시 잡지만, 그건 사람이 Unity에서 실행해야 한다.
"""
import io, re, sys

SRC = "Assets/Scripts/Editor/UpgradeTreeGenerator.cs"

# nodeId -> (비용, x, y, 선행 집합)
REQUIRED = {
    "MiningRange_T0_01":          (60,     0, -350, set()),
    "ShovelStamina_T0_01":        (110, -350, -200, {"MiningRange_T0_01"}),
    "MiningSpeed_T0_01":          (170,    0, -200, {"MiningRange_T0_01"}),
    "ToolRange_T0_01":            (230,  350, -200, {"MiningRange_T0_01"}),
    "MaxStamina_T0_01":           (290, -350,  -50, {"ShovelStamina_T0_01"}),
    "StaminaCost_T0_01":          (350,    0,  -50, {"MiningSpeed_T0_01"}),
    "MaxStaminaMultiplier_T0_01": (410,  350,  -50, {"ToolRange_T0_01"}),
    "PickaxeUnlock_T0_01":        (470,    0,  100,
        {"MaxStamina_T0_01", "StaminaCost_T0_01", "MaxStaminaMultiplier_T0_01"}),
    "Facility_Elevator_T0":       (530,    0,  250, {"PickaxeUnlock_T0_01"}),
    "PickaxeDamage_T0_01":        (590,    0,  400, {"Facility_Elevator_T0"}),
    "MiningCooldown_T0_01":       (640,    0,  550, {"PickaxeDamage_T0_01"}),
    "Facility_Computer_T0":       (700,    0,  700, {"MiningCooldown_T0_01"}),
    "MiningLevel_T0_Final":      (1280,    0,  850, {"Facility_Computer_T0"}),
}

SIDE = {
    "MoveSpeed_T0_01":      230,
    "VisionRadius_T0_01":   300,
    "ClimbSpeed_T0_01":     230,
    "FallDamage_T0_01":     330,
    "WallClimbSpeed_T0_01": 330,
}

NODE_RE = re.compile(
    r'"(?P<id>\w+)",\s*"[^"]*",\s*"[^"]*",\s*(?P<tier>\d),\s*(?P<cost>\d+),\s*'
    r'new Vector2\((?P<x>[-\d.]+)f?,\s*(?P<y>[-\d.]+)f?\),\s*'
    r'new string\[\]\s*\{(?P<parents>[^}]*)\}',
    re.S)


def parse(src):
    out = {}
    for m in NODE_RE.finditer(src):
        parents = set(re.findall(r'"(\w+)"', m.group("parents")))
        out[m.group("id")] = (
            int(m.group("cost")),
            int(float(m.group("x"))),
            int(float(m.group("y"))),
            parents,
            int(m.group("tier")),
        )
    return out


def main():
    nodes = parse(io.open(SRC, encoding="utf-8").read())
    errors = []

    for nid, (cost, x, y, parents) in REQUIRED.items():
        if nid not in nodes:
            errors.append(f"{nid}: 트리에 없다")
            continue
        acost, ax, ay, aparents, atier = nodes[nid]
        if atier != 0:      errors.append(f"{nid}: tier {atier} (기대 0)")
        if acost != cost:   errors.append(f"{nid}: 비용 {acost} (기대 {cost})")
        if (ax, ay) != (x, y):
            errors.append(f"{nid}: 좌표 ({ax},{ay}) (기대 ({x},{y}))")
        if aparents != parents:
            errors.append(f"{nid}: 선행 {sorted(aparents)} (기대 {sorted(parents)})")

    for nid, cost in SIDE.items():
        if nid not in nodes:
            errors.append(f"{nid}: 트리에 없다")
        elif nodes[nid][0] != cost:
            errors.append(f"{nid}: 비용 {nodes[nid][0]} (기대 {cost})")

    req_total = sum(v[0] for v in REQUIRED.values())
    side_total = sum(SIDE.values())
    if req_total != 5830:  errors.append(f"필수 합 {req_total} (기대 5830)")
    if side_total != 1420: errors.append(f"곁가지 합 {side_total} (기대 1420)")

    # 구매 리듬: 매 다이브 하나씩, 두 번째는 모자란다
    ladder = sorted(v[0] for nid, v in REQUIRED.items() if nid != "MiningLevel_T0_Final")
    wallet = 0.0
    for k, price in enumerate(ladder):
        wallet += 60 + 64 * k
        if wallet < price:
            errors.append(f"다이브 {k+1}: 하나도 못 산다 (지갑 {wallet:.0f} < {price})")
        wallet -= price
        nxt = ladder[k+1] if k + 1 < len(ladder) else REQUIRED["MiningLevel_T0_Final"][0]
        if wallet >= nxt:
            errors.append(f"다이브 {k+1}: 두 개가 사진다 (잔액 {wallet:.0f} >= {nxt})")

    # 필수 경로에 '중앙이 빈 행'이 없어야 한다
    rows = {}
    for nid, (_, x, y, _) in REQUIRED.items():
        rows.setdefault(y, []).append(x)
    for y, xs in sorted(rows.items()):
        if len(xs) >= 2 and 0 not in xs:
            errors.append(f"y={y}: 필수 노드 {len(xs)}개인데 중앙(x=0)이 비었다")

    if errors:
        print("실패:")
        for e in errors:
            print("  -", e)
        return 1
    print(f"통과 — 필수 {len(REQUIRED)}노드 {req_total}G / 곁가지 {side_total}G / 총 {req_total+side_total}G")
    return 0


if __name__ == "__main__":
    sys.exit(main())
```

- [ ] **Step 2: 실패를 확인한다**

Run: `python Tools/upgrade/verify_t0_layout.py`
Expected: FAIL — 비용·좌표·선행이 아직 옛 값이라 다수 항목이 어긋난다고 나온다.

- [ ] **Step 3: 필수 노드 비용을 확정 표로 바꾼다**

```bash
PYTHONIOENCODING=utf-8 python - <<'PYEOF'
import io, re
p = 'Assets/Scripts/Editor/UpgradeTreeGenerator.cs'
s = io.open(p, encoding='utf-8').read()
# (nodeId, 기존비용, 신규비용)
for nid, old, new in [
    ("MiningRange_T0_01", 60, 60),
    ("ShovelStamina_T0_01", 110, 110),
    ("MiningSpeed_T0_01", 170, 170),
    ("ToolRange_T0_01", 230, 230),
    ("MaxStamina_T0_01", 290, 290),
    ("StaminaCost_T0_01", 350, 350),
    ("MaxStaminaMultiplier_T0_01", 410, 410),
    ("PickaxeUnlock_T0_01", 470, 470),
    ("PickaxeDamage_T0_01", 530, 590),
    ("MiningCooldown_T0_01", 590, 640),
    ("MiningLevel_T0_Final", 910, 1280),
    ("Facility_Elevator_T0", 900, 530),
    ("Facility_Computer_T0", 400, 700),
]:
    if old == new:
        continue
    pat = re.compile(r'("' + re.escape(nid) + r'",\s*"[^"]*",\s*"[^"]*",\s*0,\s*)' + str(old) + r'(,)')
    s, n = pat.subn(lambda m: m.group(1) + str(new) + m.group(2), s)
    assert n == 1, f'{nid}: {n}건 매치'
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('필수 노드 비용 교체 완료')
PYEOF
```

- [ ] **Step 4: 필수 노드 좌표·선행을 바꾼다**

바뀌는 것은 9~13번 다섯 개다(1~8번은 좌표·선행이 현행 그대로).

```bash
PYTHONIOENCODING=utf-8 python - <<'PYEOF'
import io, re
p = 'Assets/Scripts/Editor/UpgradeTreeGenerator.cs'
s = io.open(p, encoding='utf-8').read()

# nodeId -> (새 Vector2, 새 선행 배열 문자열)
CHANGES = {
    "Facility_Elevator_T0":  ("new Vector2(0f, 250f)",  '"PickaxeUnlock_T0_01"'),
    "PickaxeDamage_T0_01":   ("new Vector2(0f, 400f)",  '"Facility_Elevator_T0"'),
    "MiningCooldown_T0_01":  ("new Vector2(0f, 550f)",  '"PickaxeDamage_T0_01"'),
    "Facility_Computer_T0":  ("new Vector2(0f, 700f)",  '"MiningCooldown_T0_01"'),
    "MiningLevel_T0_Final":  ("new Vector2(0f, 850f)",  '"Facility_Computer_T0"'),
}

for nid, (vec, parents) in CHANGES.items():
    pat = re.compile(
        r'("' + re.escape(nid) + r'",\s*"[^"]*",\s*"[^"]*",\s*0,\s*\d+,\s*)'
        r'new Vector2\([^)]*\)'
        r'(,\s*\n\s*new string\[\]\s*\{)[^}]*(\})', re.S)
    s, n = pat.subn(lambda m: m.group(1) + vec + m.group(2) + ' ' + parents + ' ' + m.group(3), s)
    assert n == 1, f'{nid}: {n}건 매치'

io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('필수 노드 좌표·선행 교체 완료')
PYEOF
```

- [ ] **Step 5: 검증 통과를 확인한다**

Run: `python Tools/upgrade/verify_t0_layout.py`
Expected: 곁가지 5개 비용만 실패로 남는다(Task 2에서 처리). 필수 13노드 항목·구매 리듬·중앙 빈 행은 전부 통과.

- [ ] **Step 6: 체크포인트**

생성기 T0 블록을 눈으로 훑어 시설 노드 주석의 "비용·선행·좌표는 전부 임시값이다 / T0 예산 테스트에서 의도적으로 제외돼 있다"가 더 이상 사실이 아님을 확인한다. 주석 갱신은 Task 4 Step 4에서 한다.

---

## Task 2: T0 곁가지 5노드 가격

**Files:**
- Modify: `Assets/Scripts/Editor/UpgradeTreeGenerator.cs` (곁가지 블록)

**Interfaces:**
- Consumes: Task 1의 필수 사다리(잔액 곡선이 곁가지 가격의 근거)
- Produces: 곁가지 합 1,420G. Task 5의 T0 총액 기대값 7,250G가 여기에 의존한다.

- [ ] **Step 1: 곁가지 비용을 바꾼다**

```bash
PYTHONIOENCODING=utf-8 python - <<'PYEOF'
import io, re
p = 'Assets/Scripts/Editor/UpgradeTreeGenerator.cs'
s = io.open(p, encoding='utf-8').read()
for nid, old, new in [
    ("MoveSpeed_T0_01", 170, 230),
    ("VisionRadius_T0_01", 230, 300),
    ("ClimbSpeed_T0_01", 170, 230),
    ("FallDamage_T0_01", 230, 330),
    ("WallClimbSpeed_T0_01", 230, 330),
]:
    pat = re.compile(r'("' + re.escape(nid) + r'",\s*"[^"]*",\s*"[^"]*",\s*0,\s*)' + str(old) + r'(,)')
    s, n = pat.subn(lambda m: m.group(1) + str(new) + m.group(2), s)
    assert n == 1, f'{nid}: {n}건 매치'
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('곁가지 비용 교체 완료')
PYEOF
```

- [ ] **Step 2: 검증 전체 통과를 확인한다**

Run: `python Tools/upgrade/verify_t0_layout.py`
Expected: PASS — `통과 — 필수 13노드 5830G / 곁가지 1420G / 총 7250G`

---

## Task 3: 작업대 T1 이관

**Files:**
- Modify: `Assets/Scripts/Editor/UpgradeTreeGenerator.cs` (시설 블록)

**Interfaces:**
- Consumes: 없음
- Produces: `Facility_Workbench_T1`(tier 1, 5,000G, 선행 `MiningSpeed_T1_01`, 좌표 (-600, 650)). Task 4가 이 y를 +450 한다. Task 5의 `FacilityUnlockNodes_ExistInTree` id 목록이 이 이름에 의존한다.

**주의:** 좌표는 **평행이동 전 기준(-600, 650)**으로 넣는다. Task 4의 일괄 +450이 T1 전체에 적용되면서 최종 (-600, 1100)이 된다. 여기서 1100을 넣으면 Task 4가 1550으로 밀어 버린다.

- [ ] **Step 1: 작업대 정의를 교체한다**

```bash
PYTHONIOENCODING=utf-8 python - <<'PYEOF'
import io
p = 'Assets/Scripts/Editor/UpgradeTreeGenerator.cs'
s = io.open(p, encoding='utf-8').read()

old = '''            "Facility_Workbench_T0", "작업대 설치", "정착지에 작업대가 놓여 장비와 유물을 강화할 수 있게 됩니다.", 0, 250, new Vector2(-1050f, -200f),
            new string[] { "MiningRange_T0_01" }, UpgradeEffectType.None, 0f, false'''
new = '''            "Facility_Workbench_T1", "작업대 설치", "정착지에 작업대가 놓여 장비와 유물을 강화할 수 있게 됩니다.", 1, 5000, new Vector2(-600f, 650f),
            new string[] { "MiningSpeed_T1_01" }, UpgradeEffectType.None, 0f, false'''
assert s.count(old) == 1, s.count(old)
io.open(p, 'w', encoding='utf-8', newline='').write(s.replace(old, new))
print('작업대 T1 이관 완료')
PYEOF
```

- [ ] **Step 2: 정의 순서를 확인한다**

작업대 정의가 여전히 T0 시설 블록 안에 있다. `GetUpgradePlanData()`는 리스트에 담는 순서와 무관하게 `tier` 필드로 티어를 판정하므로 **동작에는 문제가 없다.** 다만 읽는 사람이 헷갈리므로 정의를 T1 블록으로 물리적으로 옮기고, 남은 자리에 한 줄 주석을 남긴다.

Run: `grep -n "Facility_Workbench_T1" Assets/Scripts/Editor/UpgradeTreeGenerator.cs`
Expected: 한 줄만 나온다.

- [ ] **Step 3: 정의를 T1 블록으로 물리적으로 옮긴다**

아래 스크립트는 작업대 정의를 T0 시설 블록에서 잘라내어 `InventoryWeight_T1_01` 뒤에 붙이고, 원래 자리에는 안내 주석만 남긴다.

```bash
PYTHONIOENCODING=utf-8 python - <<'MOVE_WB'
import io
p = 'Assets/Scripts/Editor/UpgradeTreeGenerator.cs'
s = io.open(p, encoding='utf-8').read()

block = '''        list.Add(new UpgradeNodeData(
            "Facility_Workbench_T1", "작업대 설치", "정착지에 작업대가 놓여 장비와 유물을 강화할 수 있게 됩니다.", 1, 5000, new Vector2(-600f, 650f),
            new string[] { "MiningSpeed_T1_01" }, UpgradeEffectType.None, 0f, false
        ));
'''
note = '''        // 작업대(Facility_Workbench_T1)는 T1로 올라갔다 — 정의는 TIER 1 블록에 있다.
        // 강화할 장비가 쌓인 뒤에 의미가 있는 시설이라 2층에 둔다(설계 §2.4).
'''
assert s.count(block) == 1, f'원본 블록 {s.count(block)}건'
s = s.replace(block, note)

# InventoryWeight_T1_01 정의 뒤에 삽입한다
anchor = '''            new string[] { "MiningSpeed_T1_01" }, UpgradeEffectType.InventoryWeightUp, 30f, false
        ));
'''
assert s.count(anchor) == 1, f'앵커 {s.count(anchor)}건'
s = s.replace(anchor, anchor + chr(10) + block)

io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('작업대 정의를 TIER 1 블록으로 이동 완료')
MOVE_WB
```

- [ ] **Step 4: 티어별 노드 수를 확인한다**

```bash
PYTHONIOENCODING=utf-8 python -c "
import re,io
s=io.open('Assets/Scripts/Editor/UpgradeTreeGenerator.cs',encoding='utf-8').read()
pat=re.compile(r'\"(\w+)\",\s*\"[^\"]*\",\s*\"[^\"]*\",\s*(\d),\s*(\d+),')
from collections import Counter
c=Counter(int(m.group(2)) for m in pat.finditer(s))
print('티어별 노드 수:',dict(sorted(c.items())),'총',sum(c.values()))
"
```

Expected: `{0: 18, 1: 16, 2: 10} 총 44`

---

## Task 4: T1·T2 평행이동 +450

**Files:**
- Modify: `Assets/Scripts/Editor/UpgradeTreeGenerator.cs` (T1·T2 블록 전체 + T0 시설 주석)

**Interfaces:**
- Consumes: Task 3의 작업대(T1, y=650)
- Produces: T1 y 950~1400, T2 y 1650~1950. Task 6의 `TierYRanges_DoNotOverlap`이 이 값을 잡는다.

- [ ] **Step 1: tier 1·2 노드의 y를 일괄 +450 한다**

```bash
PYTHONIOENCODING=utf-8 python - <<'PYEOF'
import io, re
p = 'Assets/Scripts/Editor/UpgradeTreeGenerator.cs'
s = io.open(p, encoding='utf-8').read()

PAT = re.compile(
    r'("(\w+)",\s*"[^"]*",\s*"[^"]*",\s*([12]),\s*\d+,\s*new Vector2\([-\d.]+f?,\s*)([-\d.]+)(f?\))')

moved = []
def bump(m):
    y = float(m.group(4)) + 450
    moved.append((m.group(2), float(m.group(4)), y))
    return m.group(1) + f'{y:g}' + m.group(5)

s = PAT.sub(bump, s)
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print(f'{len(moved)}개 노드 이동')
ys = [y for _, _, y in moved]
print('이동 후 y 범위:', min(ys), '~', max(ys))
PYEOF
```

Expected: `26개 노드 이동` (T1 16 + T2 10), y 범위 `950.0 ~ 1950.0`

- [ ] **Step 2: 티어별 y 범위가 겹치지 않는지 확인한다**

```bash
PYTHONIOENCODING=utf-8 python -c "
import re,io
s=io.open('Assets/Scripts/Editor/UpgradeTreeGenerator.cs',encoding='utf-8').read()
pat=re.compile(r'\"(\w+)\",\s*\"[^\"]*\",\s*\"[^\"]*\",\s*(\d),\s*\d+,\s*new Vector2\([-\d.]+f?,\s*([-\d.]+)f?\)')
band={}
for m in pat.finditer(s):
    t=int(m.group(2)); y=float(m.group(3))
    lo,hi=band.get(t,(y,y)); band[t]=(min(lo,y),max(hi,y))
prev=None; ok=True
for t in sorted(band):
    lo,hi=band[t]; print(f'T{t}: {lo:g} ~ {hi:g}')
    if prev is not None and lo<=prev: print(f'  겹침! T{t} 시작 {lo:g} <= 이전 끝 {prev:g}'); ok=False
    prev=hi
print('결과:','통과' if ok else '실패')
"
```

Expected:
```
T0: -350 ~ 850
T1: 950 ~ 1400
T2: 1650 ~ 1950
결과: 통과
```

- [ ] **Step 3: T0 재배치 근거 주석을 갱신한다**

시설 블록 위의 낡은 주석(`⚠ 비용·선행·좌표는 전부 임시값이다 … T0 예산 테스트에서 의도적으로 제외돼 있다`)을 아래로 교체한다.

```csharp
        // ⚠ 엘리베이터·단말기는 2026-08-15부터 필수 경로의 정식 노드다.
        //   가격이 실측 사다리(base 60 / growth 64) 위에 있으므로 T0 예산 테스트에
        //   '포함'된다 — 종전의 제외 규칙은 T1로 올라간 작업대 쪽으로 옮겨 갔다.
        //   설계: Assets/Docs/superpowers/specs/2026-08-15-t0-spine-and-elevator-design.md §4.2
```

- [ ] **Step 4: 전체 정적 검증을 다시 돌린다**

Run: `python Tools/upgrade/verify_t0_layout.py`
Expected: PASS (T1·T2 이동은 T0에 영향이 없어야 한다)

---

## ⚠ Task 5·6은 Claude가 실행하지 않는다

`Assets/Tests/EditMode/UpgradeTreeCostTests.cs`는 **사람만 고친다** (`CLAUDE.md` §테스트).
데이터를 바꾸면서 기대값을 같이 고치면 "테스트가 통과하도록 테스트를 고친" 꼴이 되어
트리를 지키는 장치가 사라지기 때문이다.

아래 Task 5·6은 **사람이 판단해 적용할 때를 위한 참고안**이다. 그대로 두면
2026-08-15 데이터 변경 이후 다음 6개가 실패한 채로 남는다 — 데이터가 깨진 것이 아니라
기대값이 옛 값이라서다.

| 테스트 | 기대(옛) | 실제(현재) |
|---|---|---|
| `Tier0_TotalCost_MatchesLayer1Budget` | 5,150 | 6,020 (시설 제외 기준) |
| `Tier1_TotalCost_MatchesLayer2Budget` | 120,000 | 124,900 |
| `MiningLicenses_HaveExplicitCosts` | 910 | 1,280 |
| `Tier0_MinimumPathToLicense_CostsFortyOneTwenty` | 4,120 | 5,830 |
| `FacilityUnlockNodes_ExistInTree` | `Facility_Workbench_T0` | `_T1`로 이동 |
| `FacilityUnlockNodes_AreNotOnLicensePath` | 시설이 경로 밖 | 엘베·단말기가 경로 안 |

나머지는 통과한다. 특히 `Tier0_EveryDiveAffordsExactlyOneNode`(구매 리듬)와
`PickaxeUnlock_IsPrerequisiteOfPickaxeNodes`는 새 배치에서도 그대로 맞다.

---

## Task 5 (참고안): 기존 테스트 기대값 갱신

**Files:**
- Modify: `Assets/Tests/EditMode/UpgradeTreeCostTests.cs`

**Interfaces:**
- Consumes: Task 1~4의 확정 수치
- Produces: 갱신된 기대값. Task 6이 같은 파일에 테스트를 추가한다.

- [ ] **Step 1: T0 예산 테스트 — 시설 포함으로 바꾸고 7,250G**

`Tier0_TotalCost_MatchesLayer1Budget`의 본문을 아래로 교체한다.

```csharp
        // 2026-08-15 재산정 + 구조 개편. 엘리베이터·단말기가 필수 경로로 편입되면서
        // 시설을 '포함'해 센다 — 그 둘의 가격이 실측 사다리 위에 있기 때문이다.
        // 사다리 밖 임시값이라 빼야 하는 쪽은 이제 T1의 작업대다(아래 Tier1 테스트).
        // 설계: 2026-08-15-t0-spine-and-elevator-design.md §4.2
        Assert.AreEqual(7250, SumTier(LoadNodes(), 0), 150);
```

- [ ] **Step 2: T1 예산 테스트 — 시설 제외로 바꾼다**

`Tier1_TotalCost_MatchesLayer2Budget`의 본문을 아래로 교체한다.

```csharp
        // 2층 예산 198,400G 중 트리 몫 120,000G (설계 §7.2)
        // 작업대(Facility_Workbench_T1)는 실측 사다리 밖 임시값이라 제외한다.
        Assert.AreEqual(120000, SumTierExcludingFacilities(LoadNodes(), 1), 1000);
```

- [ ] **Step 3: 면허 비용**

`MiningLicenses_HaveExplicitCosts`의 T0 면허 줄을 바꾼다.

```csharp
        Assert.AreEqual(1280,  nodes["MiningLevel_T0_Final"].cost, "면허 I");
```

- [ ] **Step 4: 최소 경로 — 메서드명과 값**

메서드명 `Tier0_MinimumPathToLicense_CostsFortyOneTwenty` → `Tier0_MinimumPathToLicense_CostsFiftyEightThirty`, 마지막 단언을 바꾼다.

```csharp
        Assert.AreEqual(5830, sum, $"필수 노드 {required.Count}개");
```

- [ ] **Step 5: 시설 존재 테스트 — 작업대 id**

`FacilityUnlockNodes_ExistInTree`의 id 배열을 바꾼다.

```csharp
        foreach (string id in new[] { "Facility_Workbench_T1", "Facility_Computer_T0", "Facility_Elevator_T0" })
```

- [ ] **Step 6: 시설-면허 경로 테스트 — 전제를 뒤집는다**

`FacilityUnlockNodes_AreNotOnLicensePath`를 통째로 아래로 교체한다.

```csharp
    [Test]
    public void FacilityUnlockNodes_AreOnCorrectLicensePaths()
    {
        // 2026-08-15부터 뒤집혔다. 종전에는 "시설을 면허 선행으로 묶으면 필수 경로
        // 비용이 바뀌어 다이브 리듬이 깨진다"는 이유로 시설을 곁가지에 묶어 뒀는데,
        // 엘리베이터·단말기는 이제 사다리 위에서 값이 매겨진 필수 노드다.
        // 엘리베이터가 곁가지면 1층 하층(100m)이 안 열려 후반부가 통째로 안 플레이된다.
        var nodes = LoadNodes();

        var t0Path = new HashSet<string>();
        CollectWithParents(nodes["MiningLevel_T0_Final"], t0Path);
        Assert.IsTrue(t0Path.Contains("Facility_Elevator_T0"),
            "T0 면허 경로에 엘리베이터가 없다 — 1층 하층이 안 열린다");
        Assert.IsTrue(t0Path.Contains("Facility_Computer_T0"),
            "T0 면허 경로에 단말기가 없다");

        // 작업대는 T1 곁가지다 — 면허 II 경로에 들어가면 T1 예산이 어긋난다.
        var t1Path = new HashSet<string>();
        CollectWithParents(nodes["MiningLevel_T1_Final"], t1Path);
        Assert.IsFalse(t1Path.Contains("Facility_Workbench_T1"),
            "면허 II 경로에 작업대가 들어 있다");
    }
```

- [ ] **Step 7: 컴파일 가능성을 눈으로 확인한다**

Run: `grep -n "Facility_Workbench_T0" Assets/Tests/EditMode/UpgradeTreeCostTests.cs`
Expected: 결과 없음 (옛 id가 테스트에 남아 있지 않다)

---

## Task 6 (참고안): 신규 테스트 2개

**Files:**
- Modify: `Assets/Tests/EditMode/UpgradeTreeCostTests.cs`

**Interfaces:**
- Consumes: Task 5가 갱신한 파일, `LoadNodes()`·`CollectWithParents()` 기존 헬퍼
- Produces: 레이아웃 규칙을 데이터로 고정하는 테스트 2개

- [ ] **Step 1: 중앙 빈 행 금지 테스트를 추가한다**

`ShovelStaminaNodes_UseMultiplierConvention` 뒤에 넣는다.

```csharp
    [Test]
    public void Tier0_RequiredPath_HasNoEmptyCenterRow()
    {
        // 세로선이 노드를 거치지 않고 지나가면, 그 행의 노드들이 필수인데도
        // 곁가지처럼 매달려 보인다. 실제로 곡괭이 숙련 행(±350만 있고 중앙이 빔)이
        // 그 상태였고, 플레이어가 배치를 이상하게 여겼다.
        // 규칙: 필수 노드가 2개 이상인 행은 x=0에 노드가 있어야 한다.
        var nodes = LoadNodes();
        var required = new HashSet<string>();
        CollectWithParents(nodes["MiningLevel_T0_Final"], required);

        var rows = new Dictionary<float, List<float>>();
        foreach (string id in required)
        {
            var pos = nodes[id].uiPosition;
            if (!rows.ContainsKey(pos.y)) rows[pos.y] = new List<float>();
            rows[pos.y].Add(pos.x);
        }

        foreach (var row in rows)
        {
            if (row.Value.Count < 2) continue;
            Assert.IsTrue(row.Value.Exists(x => Mathf.Approximately(x, 0f)),
                $"y={row.Key}: 필수 노드 {row.Value.Count}개인데 중앙(x=0)이 비었다 " +
                "— 세로선이 노드를 관통해 곁가지처럼 보인다");
        }
    }
```

- [ ] **Step 2: 티어 y 범위 겹침 금지 테스트를 추가한다**

```csharp
    [Test]
    public void TierYRanges_DoNotOverlap()
    {
        // 티어 배경과 잠금 오버레이는 고정 대역이 아니라 '그 티어 노드들의 실제 Y 범위'로
        // 크기가 정해진다(UpgradeUI의 minY/maxY 루프). 범위가 겹치면 배경 사각형이
        // 포개져 잠긴 티어가 잠기지 않은 것처럼 보인다.
        var nodes = LoadNodes();
        var lo = new Dictionary<int, float>();
        var hi = new Dictionary<int, float>();

        foreach (var n in nodes.Values)
        {
            float y = n.uiPosition.y;
            if (!lo.ContainsKey(n.tier)) { lo[n.tier] = y; hi[n.tier] = y; }
            lo[n.tier] = Mathf.Min(lo[n.tier], y);
            hi[n.tier] = Mathf.Max(hi[n.tier], y);
        }

        var tiers = new List<int>(lo.Keys);
        tiers.Sort();
        for (int i = 1; i < tiers.Count; i++)
        {
            Assert.Greater(lo[tiers[i]], hi[tiers[i - 1]],
                $"T{tiers[i]} 시작 y({lo[tiers[i]]})가 T{tiers[i - 1]} 끝 y({hi[tiers[i - 1]]})보다 아래다 — 티어 배경이 겹친다");
        }
    }
```

- [ ] **Step 3: using 확인**

두 테스트가 `Mathf`·`List<float>`·`Dictionary<float, List<float>>`를 쓴다. 파일 상단에 `using UnityEngine;`과 `using System.Collections.Generic;`이 있는지 확인하고 없으면 추가한다.

Run: `head -12 Assets/Tests/EditMode/UpgradeTreeCostTests.cs`
Expected: 두 using이 모두 보인다.

---

## Task 7: 에셋 재생성과 최종 검증 (사람 작업)

**Files:**
- Regenerate: `Assets/GameData/UpgradeData/{Node,Effect}/*.asset`
- Modify: 씬의 작업대 오브젝트 `WorldInteractable.unlockNodeId`

**Interfaces:**
- Consumes: Task 1~6 전부
- Produces: 최종 상태

- [ ] **Step 1: 생성기 실행**

Unity 에디터에서 `Tools/Upgrade/Upgrade Tree Generator`를 연다.
**`전체 삭제 후 재생성`은 끈 채로**(기본값) 실행한다.

작업대 nodeId가 `Facility_Workbench_T0` → `_T1`로 바뀌지만, 생성기가 증분 모드에서도 고아 에셋을 지우므로(`DeleteOrphans`) 옛 에셋은 자동 정리된다. 토글을 켜면 GUID가 새로 발급되어 **인스펙터에서 지정한 아이콘 참조가 전부 끊긴다.**

- [ ] **Step 2: 씬의 작업대 인스펙터를 고친다**

씬에 배치된 작업대 오브젝트를 찾아 `WorldInteractable.unlockNodeId`를 `Facility_Workbench_T0` → `Facility_Workbench_T1`로 바꾼다.

문자열 참조라 컴파일러가 잡지 못한다. 놓치면 그 작업대가 영구 잠금이 되고, 실행 시 `WorldInteractable.WarnIfUnknownUnlockNode`가 경고를 띄운다.

- [ ] **Step 3: EditMode 테스트 실행**

Unity Test Runner에서 `UpgradeTreeCostTests` 전체를 돌린다.

기대값:

| 테스트 | 기대 |
|---|---|
| `NodeCount_IsFortyFour` | 44 |
| `Tier0_TotalCost_MatchesLayer1Budget` | 7,250G |
| `Tier1_TotalCost_MatchesLayer2Budget` | 120,000G (시설 제외) |
| `MiningLicenses_HaveExplicitCosts` | 면허 I 1,280G |
| `Tier0_MinimumPathToLicense_CostsFiftyEightThirty` | 5,830G / 13노드 |
| `Tier0_EveryDiveAffordsExactlyOneNode` | 통과 |
| `Tier0_RequiredPath_HasNoEmptyCenterRow` | 통과 |
| `TierYRanges_DoNotOverlap` | 통과 |
| `FacilityUnlockNodes_ExistInTree` | 통과 |
| `FacilityUnlockNodes_AreOnCorrectLicensePaths` | 통과 |
| `PickaxeUnlock_IsPrerequisiteOfPickaxeNodes` | 통과 |
| `UpgradeTreeLineRoutingTests` 전체 | 통과 |

- [ ] **Step 4: 업그레이드 화면을 눈으로 확인한다**

게임을 실행해 업그레이드 트리를 연다.

- 루트부터 면허까지 세로선이 **노드로만** 이어지는가 (선 옆에 매달린 필수 노드가 없는가)
- 엘리베이터가 곡괭이 바로 위에 있는가
- T0/T1/T2 배경이 겹치지 않는가
- x=-1050 열이 비었는가 (시설이 스파인으로 옮겨 갔으므로)

- [ ] **Step 5: 새 슬롯으로 밸런스 확인**

기존 세이브는 옛 가격으로 결제된 상태라 밸런스를 볼 수 없다. **새 슬롯**으로 시작한다.

`day_settled` 텔레메트리가 이제 기록되므로(선행 작업에서 `SleepSequence`로 이전), 몇 일 플레이한 뒤 다음으로 실측 재적합이 가능하다.

```bash
python Tools/telemetry/export_csv.py --out <출력디렉토리>
python Tools/telemetry/fit_upgrade_prices.py --csv-dir <출력디렉토리> --nodes 12
```

---

## 완료 기준

- `python Tools/upgrade/verify_t0_layout.py` 통과
- 티어 y 범위 겹침 없음 (Task 4 Step 2)
- 티어별 노드 수 `{0: 18, 1: 16, 2: 10}`, 총 44
- Unity EditMode 테스트 — 위 6개는 기대값이 옛 값이라 실패한다(사람이 갱신 여부 판단)
- 업그레이드 화면 육안 확인 (사람 확인)
