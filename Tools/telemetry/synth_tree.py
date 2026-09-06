#!/usr/bin/env python3
"""업그레이드 트리를 병목에서 합성한다. UpgradeTree.csv를 뽑는다.

무엇을 하나
-----------
런 모델(run_model)의 한계효용을 따라 "지금 무엇이 모자란가"를 보고 다음 노드를
고른다. 노드 목록을 사람이 정하지 않는다 — 병목이 정한다.

  1. 지금 스탯에서 각 재료의 한계효용을 잰다
  2. 가격 대비 효율이 가장 높은 것을 척추의 다음 칸에 놓는다
  3. 그 시점 2~3위를 곁가지로 옆에 놓는다 (안 사도 진행되는 선택지)
  4. 스탯에 반영하고 1번으로. 목표 다이브 수를 채우면 면허를 놓고 다음 층으로

순환을 강제하지 않는다
----------------------
"직전과 같은 축은 건너뛴다"는 규칙을 **일부러 넣지 않았다**(marginal.best 주석 참고).
min()이 이미 순환을 만든다. 강제하면 초반 가방 연속 구매 구간에서 교착한다.

가격은 어디서 오나
------------------
그 시점의 **잠수 1회 수입 x margin**이다. 별도 사다리를 만들지 않는다.
수입이 오르면 가격도 따라 오르므로 "다이브마다 하나 사진다"가 자동으로 유지된다.
가격을 따로 매기면 모델과 어긋나 리듬이 무너진다(지금 트리가 그 상태다).

죽은 효과는 재료에서 빠진다
---------------------------
check_effect_wiring으로 걸러서, 배선 안 된 효과로 트리를 채우지 않는다.
효과를 배선하면 다음 합성부터 자동으로 재료에 들어온다.

    python Tools/telemetry/synth_tree.py                    # 리포트만
    python Tools/telemetry/synth_tree.py --write            # CSV로 저장
    python Tools/telemetry/synth_tree.py --dives 9,14,18,20 # 층별 목표 다이브 수

설계: Assets/Docs/superpowers/specs/2026-08-21-upgrade-tree-autogen-design.md §7
표준 라이브러리만 쓴다.
"""

import argparse
import csv
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))

import run_model as RM
import marginal as MG
import upgrade_catalog as CAT
import check_effect_wiring as WIRE
import upgrade_tree_csv as UTC

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):
        pass

# 가격 = 그 시점 잠수 1회 수입 x TREE_SHARE x margin.
# margin: 1.0에 가까울수록 "한 다이브에 딱 하나", 낮추면 두 개씩 사진다.
DEFAULT_MARGIN = 0.92

# 트리가 가져가는 수입 몫. 골드 sink는 트리만이 아니다 —
# 장비 해금·유물 해금/강화·소모품(포션)이 같은 지갑을 두고 경쟁하고,
# 설계 문서도 층 예산을 나눈다(mineral-price-design.md §7.2: 2층 198,400 중 트리 120,000).
#
# ⚠ 2026-08-22: 0.65 -> 0.9로 올림. 첫 땅에서는 장비·유물·주식이 아직
#   안 열려 있어 수입이 사실상 전부 트리로 간다(사용자 확인). 0.65로 두면
#   가격이 실제 저축 속도보다 싸서 하루에 두세 개씩 사진다.
#   T1 이후 장비·유물 sink가 살아나면 다시 낮춰야 한다 — 그때 autoplay로 재확인할 것.
TREE_SHARE = 0.9

# 면허(층 게이트) 가격 배수. 척추 한 칸보다 확실히 무거워야 관문으로 느껴진다.
LICENSE_MARGIN = 2.4

# 가격 반올림 단위
STEP = 10

# 곁가지를 몇 칸마다 놓나
BRANCH_EVERY = 3

# 같은 재료를 한 층에서 최대 몇 번까지 쓰나.
# 이 상한이 없으면 축마다 효율 1등 하나가 영원히 이겨서 나머지 재료가 죽는다
# (스태미나 재료 4종 중 stamina_max만 8번 뽑히던 상태).
# 상한에 닿으면 같은 축의 다음 재료로 자연히 넘어가고, 그게 곧
# "가방 I -> II -> III 다음엔 다른 것"이라는 트리의 익숙한 모양이다.
MAX_REPEATS_PER_TIER = 4

# ──────────────────────────────────────────────────────────────────────────
# 통행권 노드 — 한계효용으로 고르지 않는다. 반드시 들어가야 한다.
#
# 이 노드들은 스탯을 올리는 게 아니라 **기능을 여는 열쇠**이고, 게임 코드와
# 설정이 id 문자열로 직접 붙잡고 있다. 합성기가 빠뜨리면 에러 없이
# 곡괭이·드릴·지도·시설이 영원히 잠긴다.
#   check_node_refs.py 로 참조처를 확인할 수 있다.
#
# at = 그 층에서 몇 번째 칸 뒤에 놓을지 (0이면 층 시작)
# 문구와 effectType은 기존 트리에서 그대로 가져왔다 — 새로 짓지 않는다.
# ──────────────────────────────────────────────────────────────────────────
REQUIRED_UNLOCKS = [
    dict(node_id="Facility_Map_T0", tier=0, at=2, branch=False,
         effect_type="None", effect_value=0, pct=False,
         name="지도 장비 구입",
         desc="휴대용 지도 장비를 들여 미니맵과 전체 지도(M)를 볼 수 있게 됩니다."),
    dict(node_id="PickaxeUnlock_T0_01", tier=0, at=4, branch=False,
         effect_type="None", effect_value=0, pct=False,
         name="곡괭이 입수",
         desc="곡괭이를 쓸 수 있게 됩니다. 단단한 돌을 캐 희귀 광물을 얻습니다."),
    dict(node_id="Facility_Computer_T0", tier=0, at=6, branch=False,
         effect_type="None", effect_value=0, pct=False,
         name="단말기 개통",
         desc="거래소 단말기가 켜져 주식과 코인을 거래할 수 있게 됩니다."),
    # 엘리베이터 해금 — 발견이 아니라 이 노드를 사야 탈 수 있다(2026-08-22 확정).
    # 판 시작 깊이를 정류장으로 내려 주는, 사실상 '재하강 배터리 절약' 업그레이드.
    # ⚠ ElevatorPrefab의 WorldInteractable.unlockNodeId가 현재 비어 있어
    #   이 노드를 넣어도 실제 게임은 안 잠긴다 — 프리팹 배선이 별도로 필요하다.
    dict(node_id="Facility_Elevator_T0", tier=0, at=3, branch=False,
         effect_type="None", effect_value=0, pct=False,
         name="엘리베이터 가동",
         desc="지하 엘리베이터를 가동합니다. 발견한 정류장에서 바로 내려갈 수 있게 됩니다."),
    dict(node_id="RelicUnlock_ElevatorTracker_T0", tier=0, at=5, branch=True,
         effect_type="None", effect_value=0, pct=False,
         name="엘리베이터 신호기 입수",
         desc="유물 '엘리베이터 신호기'를 얻습니다. 가보지 않은 엘리베이터도 지도에 표시됩니다."),
    dict(node_id="DrillCapacity_T1_01", tier=1, at=2, branch=False,
         effect_type="DrillBatteryCapacity", effect_value=50, pct=False,
         name="드릴 입수",
         desc="드릴을 쓸 수 있게 됩니다. 최대 배터리 용량 +50."),
]

# UI 격자
SPINE_X = 0
BRANCH_DX = 350
ROW_DY = 150


def round_price(v):
    return max(STEP, int(round(v / STEP)) * STEP)


def live_materials(verbose=False):
    """배선된 효과만 남긴 재료 목록."""
    mapping = WIRE.load_map()
    live = set()
    for name in WIRE.load_enum():
        stat = mapping.get(name, "__none__")
        if WIRE.consumers(stat, effect_name=name):
            live.add(name)

    kept, dropped = [], []
    for m in CAT.MATERIALS:
        (kept if m.effect_type in live else dropped).append(m)
    if dropped and verbose:
        print("[배선 안 됨 — 재료에서 뺌] %s"
              % ", ".join(m.effect_type for m in dropped))
    return kept, dropped


class UnlockMaterial:
    """통행권 노드용 재료 껍데기. 스탯을 안 움직이므로 apply는 그대로 돌려준다."""

    __slots__ = ("key", "effect_type", "axis", "label", "field", "mode",
                 "amount", "desc")

    def __init__(self, spec):
        self.key = spec["node_id"]
        self.effect_type = spec["effect_type"]
        self.axis = "unlock"
        self.label = spec["name"]
        self.desc = spec["desc"]
        self.field = None
        self.mode = "add"
        self.amount = spec["effect_value"]

    def apply(self, stats):
        return stats

    @property
    def effect_value(self):
        return self.amount

    @property
    def is_percentage(self):
        return False


class Node:
    __slots__ = ("node_id", "tier", "material", "cost", "parents",
                 "ui", "is_branch", "gain", "bottleneck")

    def __init__(self, node_id, tier, material, cost, parents, ui,
                 is_branch, gain, bottleneck):
        self.node_id = node_id
        self.tier = tier
        self.material = material
        self.cost = cost
        self.parents = parents
        self.ui = ui
        self.is_branch = is_branch
        self.gain = gain
        self.bottleneck = bottleneck


def apply_unlock(stats, spec):
    """통행권 노드가 시뮬 상태에 미치는 효과.

    엘리베이터: 다음 판부터 정류장에서 시작한다(elevator_unlocked).
    드릴: 별도 배터리 풀인데 모델에 아직 드릴 축이 없어 효과 없음(추후).
    """
    if spec["node_id"].startswith("Facility_Elevator"):
        return stats.copy(elevator_unlocked=True)
    return stats


def synth(layers, stats, dives_per_tier, margin=DEFAULT_MARGIN, materials=None):
    """트리를 합성한다. (노드 목록, 최종 스탯)

    구조: 축마다 세로 라인을 만들고 면허가 끝을 AND로 모은다.
    구매 순서는 한계효용 시뮬레이션이 정하고, 축별로 나눠 담는다.

    시뮬은 판 진행을 따라간다 — 판마다 도달 깊이가 쌓이고, 엘리베이터를
    사면 다음 판 시작점이 정류장으로 내려간다(elevator_start).
    """
    mats = materials if materials is not None else CAT.CHOOSABLE
    choosable = [m for m in mats if m.axis != CAT.AXIS_GATE]
    gate = next((m for m in mats if m.axis == CAT.AXIS_GATE), None)

    nodes = []
    cur = stats
    entry = None          # 이 층으로 들어오는 노드(직전 면허)
    reached = 0.0         # 지금까지 도달한 최대 깊이 (엘베 정류장 계산용)
    last_income = 0.0     # 벽(수입 0)에 막히기 직전의 수입 — 면허 가격의 기준

    for tier, target in enumerate(dives_per_tier):
        if tier >= len(layers):
            break
        seq, used = {}, {}
        picks = []        # (material, price, gain, bottleneck)

        for step in range(target):
            start = RM.elevator_start(cur, reached, RM.reachable_depth(cur, layers))
            r_now = RM.income(cur, layers, start_depth=start)
            if r_now.gold <= 0:
                print("  [중단] T%d에서 수입이 0이다 (병목 %s)" % (tier, r_now.bottleneck))
                break
            last_income = r_now.gold
            price = round_price(r_now.gold * TREE_SHARE * margin)

            def cost_of(_m, _p=price):
                return _p

            avail = [x for x in choosable
                     if used.get(x.key, 0) < MAX_REPEATS_PER_TIER]
            m, gain, _ = MG.best(cur, layers, cost_of, start, materials=avail)
            if m is None:
                print("  [중단] T%d에서 더 살 것이 없다 (한계효용 전부 0)" % tier)
                break
            used[m.key] = used.get(m.key, 0) + 1
            picks.append((m, price, gain, r_now.bottleneck))
            cur = m.apply(cur)

            # 통행권 노드의 시뮬 효과 (엘리베이터 = 시작점 램프)
            for spec in REQUIRED_UNLOCKS:
                if spec["tier"] == tier and spec["at"] == step + 1:
                    cur = apply_unlock(cur, spec)

            r_after = RM.income(cur, layers, start_depth=start)
            reached = max(reached, r_after.detail.get("depth", reached))

        # ── 축별 라인으로 나눠 담는다 ────────────────────────────────────
        lanes = {}
        for m, price, gain, bn in picks:
            lanes.setdefault(m.axis, []).append((m, price, gain, bn))

        unlocks = [s for s in REQUIRED_UNLOCKS if s["tier"] == tier]
        if unlocks:
            lanes["unlock"] = [(UnlockMaterial(s), 0, 0.0, "unlock") for s in unlocks]

        order = [a for a in ("battery", "stamina", "weight", "time", "yield", "unlock")
                 if a in lanes]
        lane_tips = []
        for li, axis in enumerate(order):
            x = (li - (len(order) - 1) / 2.0) * BRANCH_DX
            prev = entry
            for row, (m, price, gain, bn) in enumerate(lanes[axis]):
                if axis == "unlock":
                    price = round_price(
                        (picks[min(row * 2, len(picks) - 1)][1] if picks else 100))
                seq[m.key] = seq.get(m.key, 0) + 1
                nid = ("%s_T%d_%02d" % (CAT.node_prefix(m), tier, seq[m.key])
                       if axis != "unlock" else m.key)
                nodes.append(Node(nid, tier, m, price,
                                  [prev] if prev else [],
                                  (x, (row + 1) * ROW_DY), False, gain, bn))
                prev = nid
            if prev and prev != entry:
                lane_tips.append(prev)

        # ── 면허 — 모든 라인의 끝을 AND로 모은다 ─────────────────────────
        if gate is not None and tier + 1 < len(dives_per_tier):
            # 가격은 벽에 막히기 **직전**의 수입 기준. 막힌 시점은 수입이 0이라
            # (정류장 = 면허 천장 → 팔 새 땅이 없음) 그걸 쓰면 면허가 공짜가 된다.
            lic = "MiningLevel_T%d_Final" % tier
            depth = max((len(v) for v in lanes.values()), default=0)
            nodes.append(Node(lic, tier, gate,
                              round_price(last_income * TREE_SHARE * LICENSE_MARGIN),
                              lane_tips, (0, (depth + 1) * ROW_DY),
                              False, 0.0, "gate"))
            entry = lic
            cur = gate.apply(cur)

    return nodes, cur


def shape(nodes, layers):
    """트리를 축 라인별로 그린다."""
    print("\n트리 모양 — 축마다 세로 라인, 면허가 끝을 모은다\n")
    tiers = sorted(set(n.tier for n in nodes))
    for tier in tiers:
        rows = [n for n in nodes if n.tier == tier]
        name = layers[tier].name if tier < len(layers) else "?"
        print("  " + "=" * 62)
        print("   T%d  %s" % (tier, name))
        print("  " + "=" * 62)
        lanes = {}
        gate_node = None
        for n in rows:
            if n.material.axis == CAT.AXIS_GATE:
                gate_node = n
                continue
            lanes.setdefault(n.material.axis, []).append(n)
        for axis in sorted(lanes):
            print("   [%s]" % axis)
            for n in lanes[axis]:
                print("      | %-24s %6dG  %+.1fG"
                      % (n.material.label, n.cost, n.gain))
        if gate_node:
            print("   [면허]")
            print("      O== %-23s %6dG   선행 %d개 라인의 끝"
                  % (gate_node.material.label, gate_node.cost,
                     len(gate_node.parents)))
    total = sum(n.cost for n in nodes)
    print("\n   전체 %dG / 노드 %d개" % (total, len(nodes)))


def to_csv_rows(nodes):
    out = []
    for n in nodes:
        m = n.material
        out.append({
            "nodeId": n.node_id,
            "tier": n.tier,
            "effectType": m.effect_type,
            "effectValue": ("%g" % m.effect_value),
            "isPercentage": "true" if m.is_percentage else "false",
            "parentIds": ";".join(n.parents),
            "cost": n.cost,
            "uiX": int(n.ui[0]),
            "uiY": int(n.ui[1]),
            "displayNameKey": (m.label if hasattr(m, "desc")
                               else "%s %s" % (m.label, n.node_id.split("_")[-1])),
            "descriptionKey": getattr(m, "desc", m.label),
            "locked": "false",
            "targetMineral": "",
        })
    return out


def merge_locked(generated, csv_path):
    """기존 CSV에서 locked=true인 행은 그대로 살린다.

    사람이 손으로 확정한 행을 합성기가 덮어쓰면, 자동 생성 초안이 매번
    수동 튜닝을 날려서 결국 아무도 안 쓰게 된다.
    """
    p = Path(csv_path)
    if not p.exists():
        return generated, 0
    try:
        old = UTC.load(csv_path)
    except Exception:
        return generated, 0

    locked = {r["nodeId"]: r for r in old if r["locked"]}
    if not locked:
        return generated, 0

    kept = [r for r in generated if r["nodeId"] not in locked]
    for nid, r in locked.items():
        kept.append({
            "nodeId": nid, "tier": r["tier"], "effectType": r["effectType"],
            "effectValue": ("%g" % r["effectValue"]),
            "isPercentage": "true" if r["isPercentage"] else "false",
            "parentIds": ";".join(r["parentIds"]), "cost": r["cost"],
            "uiX": int(r["uiX"]), "uiY": int(r["uiY"]),
            "displayNameKey": r["displayNameKey"],
            "descriptionKey": r["descriptionKey"], "locked": "true",
            "targetMineral": r.get("targetMineral", ""),
        })
    return kept, len(locked)


FIELDS = ["nodeId", "tier", "effectType", "effectValue", "isPercentage",
          "parentIds", "cost", "uiX", "uiY", "displayNameKey",
          "descriptionKey", "locked", "targetMineral"]


def write_csv(rows, path):
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, fieldnames=FIELDS, quoting=csv.QUOTE_MINIMAL)
        w.writeheader()
        for r in rows:
            w.writerow({k: r[k] for k in FIELDS})


def main():
    ap = argparse.ArgumentParser(description="병목에서 업그레이드 트리를 합성한다")
    ap.add_argument("--dives", default="9,14",
                    help="층별 목표 다이브 수 (쉼표 구분). 기본 9,14")
    ap.add_argument("--margin", type=float, default=DEFAULT_MARGIN)
    ap.add_argument("--shape", action="store_true", help="트리를 세로 그림으로 본다")
    ap.add_argument("--write", action="store_true", help="UpgradeTree.csv로 저장")
    ap.add_argument("--out", default=UTC.DEFAULT_PATH)
    a = ap.parse_args()

    dives = [int(x) for x in a.dives.split(",") if x.strip()]
    layers = RM.load_layers()
    stats = RM.base_stats()

    mats, dropped = live_materials(verbose=True)
    print("재료 %d종 사용 (배선 안 된 %d종 제외)\n" % (len(mats), len(dropped)))

    nodes, final = synth(layers, stats, dives, a.margin, mats)
    if a.shape:
        shape(nodes, layers)
    else:
        report(nodes, layers, stats)

    if a.write:
        rows = to_csv_rows(nodes)
        rows, n_locked = merge_locked(rows, a.out)
        write_csv(rows, a.out)
        print("\n저장: %s (%d행%s)"
              % (a.out, len(rows),
                 ", locked 보존 %d행" % n_locked if n_locked else ""))
        print("이어서 Unity에서 Tools > Upgrade > Upgrade Tree Generator 를 돌릴 것.")
    else:
        print("\n(--write 를 주면 %s 에 저장한다)" % a.out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
