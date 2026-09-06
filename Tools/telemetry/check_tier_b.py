#!/usr/bin/env python3
"""티어 B 원칙 체커 — 손으로 맞춘 트리를 런 모델이 채점한다.

  P4   지배 노드 없음      — 모든 구간에서 효율 1등인 노드가 있으면 FAIL
  P7   병목이 돌아간다     — 같은 축을 5연속 사게 되면 FAIL
  P10  함정 없음           — 어떤 상태에서도 한계효용이 0 이하인 노드가 있으면 FAIL
  P11  첫 5구매가 갈린다   — 플레이 성향을 바꿔도 같은 순서면 FAIL

왜 텔레메트리가 아니라 모델인가
--------------------------------
이 넷은 전부 **반사실**을 요구한다 — "안 샀으면 어땠나". 혼자 플레이하는 사람의
구매 로그에는 그게 없고, 트리를 만든 본인이 매번 같은 것을 사므로 구매 분포는
데이터가 아니라 취향의 재확인이다. charter §1.

⚠ **가격은 이 스크립트가 정하지 않는다.** 가격은 사람이 플레이하며 맞춘 값이고
   `UpgradeTree.csv`가 정본이다(charter §1). 여기서는 그 가격을 **채점만** 한다 —
   출력은 "가격을 이렇게 바꿔라"가 아니라 "이 노드가 원칙 N을 어긴다"이다.
   `reprice_template.py`를 부르지 않는다.

⚠ **P0가 PASS일 때만 의미가 있다.** 여기 판정은 전부 런 모델 위에 서 있으므로,
   모델이 현재 빌드를 설명하지 못하면 조용히 틀린 답이 나온다.
   `check_principles.py`를 먼저 돌릴 것.

사용법:
    python check_tier_b.py [--steps 20] [--variants 12]

표준 라이브러리만 쓴다.
"""

import argparse
import collections
import random
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import run_model as RM
import upgrade_tree_csv as TREE

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):
        pass

REPO = Path(__file__).resolve().parents[2]
TREE_PATH = REPO / "Assets/GameData/UpgradeData/UpgradeTree.csv"
TILE_DATA = REPO / "Assets/StreamingAssets/tileData.json"
PRICE_DATA = REPO / "Assets/StreamingAssets/priceData.json"
PLAYER_SO = REPO / "Assets/GameData/PlayerData/DefalutPlayerSO.asset"

# 같은 축을 몇 번 연속으로 사면 '순환이 죽었다'고 볼 것인가 (charter §2).
MAX_SAME_AXIS_RUN = 5

# P4 — 어떤 노드가 '지배'하려면 자기가 살 수 있었던 구간 중 몇 %에서 1등이어야 하나.
# 100%로 잡으면 한 번만 2등이어도 빠져나가고, 낮게 잡으면 초반 최저가 노드가
# 전부 걸린다. 살 수 있는 내내 1등이면 사실상 "이것부터 사는 게 정답"인 트리다.
DOMINANCE_RATIO = 0.9

# P4 판정에 필요한 최소 등장 횟수. 두세 구간에서만 1등인 것을 지배라 부를 수 없다.
DOMINANCE_MIN_STATES = 4


# ==========================================================================
# 노드 -> 스탯 변화
#
# ⚠ upgrade_catalog.py 를 쓰지 않는다. 그쪽 effect_type 은 **곱연산** 변종
#   (MiningRangeMultiplier 등)인데 실제 트리는 2026-08-24부터 **합연산** 변종
#   (MiningRangeUp 등)을 쓴다 — "층마다 계속 사는 노드라 곱연산은 배율이 폭주한다"는
#   이유로 바꾼 것이다(UpgradeEffectSO.cs 주석). 카탈로그가 그 변경을 안 따라갔다.
#   여기서는 **트리 CSV의 effectType을 정본으로** 삼는다.
# ==========================================================================

# effectType -> (Stats 필드, 축)
# 값은 전부 합연산이다. ShovelStaminaReduce/StaminaCostReduce 는 CSV에 음수가 적혀
# 있어서(기준 1.0에 더한다) 같은 규칙으로 처리된다.
EFFECT_MAP = {
    "MiningRangeUp":      ("mining_range", "yield"),
    "MiningSpeedUp":      ("mining_speed", "time"),
    "InventoryWeightUp":  ("weight_limit", "weight"),
    "MaxStaminaUp":       ("max_stamina", "stamina"),
    "ShovelStaminaReduce": ("shovel_stamina_mult", "stamina"),
    "StaminaCostReduce":  ("stamina_cost_mult", "stamina"),
    "WallClimbSpeed":     ("wall_climb_speed", "stamina"),
    "MoveSpeedUp":        ("move_speed", "time"),
    "MiningLevel":        ("mining_level", "gate"),
}

# 모델이 표현하지 못하는 effectType. 여기 있는 노드는 P4·P10 판정에서 빠진다 —
# **"판정 불가"이지 "문제 없음"이 아니다.** 배선 여부는 check_effect_wiring.py 담당.
UNMODELED_NOTE = {
    "None": "시설·해금 노드(통행권). 스탯을 안 움직이는 것이 정상",
    "MineralExtraDropChance": "추가 드랍 확률 — 모델에 없음",
    "RareMineralChance": "희귀 확률 — 모델에 없음",
    "InventorySlotUp": "슬롯 — 모델의 slots가 미구현",
    "NapCount": "낮잠 횟수 — 하루 단위라 잠수 모델 밖",
    "RelicSlotUp": "유물 슬롯 — 모델에 없음",
    "PickaxeStaminaReduce": "곡괭이 — 모델은 삽만 다룬다",
    "PickaxeDamageUp": "곡괭이 — 모델은 삽만 다룬다",
    "JumpForceUp": "점프력 — 수입에 영향 경로 없음",
    "DrillBatteryCapacity": "드릴 — 모델에 없음",
    "DrillBatteryRegen": "드릴 — 모델에 없음",
    "DrillDrainReduce": "드릴 — 모델에 없음",
    "DrillRadiusUp": "드릴 — 모델에 없음",
}


def apply_node(stats, node):
    """노드 하나를 산 뒤의 스탯. 표현할 수 없으면 None."""
    et = node["effectType"]

    if et == "MineralPriceUp":
        # 한 노드가 광물 여러 개의 판매가를 올릴 수 있다(';' 구분).
        targets = [t.strip() for t in (node["targetMineral"] or "").split(";") if t.strip()]
        if not targets:
            return None
        bonus = dict(stats.mineral_price_bonus)
        for t in targets:
            bonus[t] = bonus.get(t, 0.0) + node["effectValue"]
        return stats.copy(mineral_price_bonus=bonus)

    entry = EFFECT_MAP.get(et)
    if entry is None:
        return None
    field, _axis = entry
    return stats.copy(**{field: getattr(stats, field) + node["effectValue"]})


def node_axis(node):
    if node["effectType"] == "MineralPriceUp":
        return "price"
    entry = EFFECT_MAP.get(node["effectType"])
    return entry[1] if entry else "-"


def is_modelable(node):
    return node["effectType"] == "MineralPriceUp" or node["effectType"] in EFFECT_MAP


# ==========================================================================
# 상태 진행
# ==========================================================================

def available(nodes, owned):
    """선행이 전부 충족되고 아직 안 산 노드. 선행은 AND다(UpgradeManager.CanUnlock)."""
    return [n for n in nodes
            if n["nodeId"] not in owned
            and all(p in owned for p in n["parentIds"])]


def gain_of(stats, node, layers, start_depth):
    """이 노드를 사면 잠수 1회 수입이 얼마나 오르나. 표현 불가면 None."""
    after = apply_node(stats, node)
    if after is None:
        return None
    before = RM.income(stats, layers, start_depth=start_depth).gold
    return RM.income(after, layers, start_depth=start_depth).gold - before


def walk(nodes, layers, steps, noise=0.0, rng=None):
    """효율(증가÷가격) 순으로 계속 사면서 상태를 진행시킨다.

    표현 불가 노드도 **사기는 한다** — 안 사면 그 뒤 선행이 안 열려 트리의 절반이
    영원히 잠긴다. 다만 효율 계산에서는 0으로 두고, 다른 살 것이 없을 때만 집는다.

    noise > 0 이면 효율에 곱셈 잡음을 준다. 완벽하지 않은 플레이어를 흉내내
    P11(빌드 다양성)에서 성향을 가르는 데 쓴다.
    """
    stats = RM.base_stats(str(PLAYER_SO))
    owned, order, reached = set(), [], 0.0
    ranks = []          # 구간별 (효율 내림차순 노드id 목록)

    for _ in range(steps):
        start = RM.elevator_start(stats, reached, RM.reachable_depth(stats, layers))
        cands = available(nodes, owned)
        if not cands:
            break

        scored, fallback = [], []
        for n in cands:
            g = gain_of(stats, n, layers, start)
            if g is None:
                fallback.append(n)
                continue
            cost = n["cost"]
            roi = (g / cost) if cost > 0 else 0.0
            if noise and rng:
                roi *= rng.uniform(1.0 - noise, 1.0 + noise)
            scored.append((roi, g, n))

        scored.sort(key=lambda t: -t[0])
        ranks.append([n["nodeId"] for roi, _g, n in scored if roi > 0])

        pick = None
        for roi, _g, n in scored:
            if roi > 0:
                pick = n
                break
        if pick is None:
            # 수입을 올리는 것이 없다 = 통행권을 살 차례다(가장 싼 것부터).
            pool = fallback or [n for _roi, _g, n in scored]
            if not pool:
                break
            pick = min(pool, key=lambda n: n["cost"])

        nxt = apply_node(stats, pick)
        if nxt is not None:
            stats = nxt
        owned.add(pick["nodeId"])
        order.append(pick)
        r = RM.income(stats, layers, start_depth=start)
        reached = max(reached, r.detail.get("depth", reached))

    return order, ranks, stats


# ==========================================================================
# 원칙
# ==========================================================================

def check_p4(ranks, by_id):
    """P4 — 어떤 노드가 살 수 있는 내내 효율 1등이면 지배다."""
    print("=" * 74)
    print("P4 — 지배 노드 없음")
    print("=" * 74)

    seen, first = collections.Counter(), collections.Counter()
    for r in ranks:
        for nid in r:
            seen[nid] += 1
        if r:
            first[r[0]] += 1

    rows = []
    for nid, n_seen in seen.items():
        if n_seen < DOMINANCE_MIN_STATES:
            continue
        ratio = first[nid] / n_seen
        if ratio >= DOMINANCE_RATIO:
            rows.append((nid, first[nid], n_seen, ratio))

    print("  검사한 구간 %d개 · 후보로 등장한 노드 %d개" % (len(ranks), len(seen)))
    print("  ⚠ 후보는 **모델이 점수를 매길 수 있는 노드**뿐이다. 사각지대 노드는")
    print("    효율 0이라 순위에 안 들어온다 — 그쪽에 지배 노드가 있어도 여기선 안 보인다.")
    if not rows:
        print("  ✔ PASS — 살 수 있는 내내 1등인 노드가 없다.")
        return True

    print("  ✖ FAIL — 지배 노드 %d개 (등장 구간의 %d%% 이상에서 효율 1등):"
          % (len(rows), int(DOMINANCE_RATIO * 100)))
    print()
    print("     %-30s %8s %8s %7s %8s"
          % ("노드", "1등", "등장", "비율", "가격"))
    print("     " + "-" * 66)
    for nid, f, s_, ratio in sorted(rows, key=lambda t: -t[3]):
        print("     %-30s %8d %8d %6.0f%% %8d"
              % (nid, f, s_, ratio * 100, by_id[nid]["cost"]))
    print()
    print("  지배 노드가 있으면 '무엇을 살까'가 질문이 아니게 된다 — 순서가 정해진다.")
    print("  가격을 올리거나, 효과를 그 축이 병목일 때만 듣게 바꾼다.")
    return False


def check_p7(order):
    """P7 — 같은 축을 연속으로 사게 되면 병목 순환이 죽은 것이다.

    ⚠ 이것은 **관측 지표**다. 생성기 제약("직전과 같은 축 금지")으로 넣었다가
      초반 가방 연속 구매 구간에서 교착했다(charter §2). min()이 이미 순환을
      만들고, 강제하면 돕는 게 아니라 방해한다.
    """
    print()
    print("=" * 74)
    print("P7 — 병목이 돌아간다")
    print("=" * 74)

    axes = [node_axis(n) for n in order]
    runs, cur, cur_len = [], None, 0
    for i, a in enumerate(axes):
        if a == cur:
            cur_len += 1
        else:
            if cur_len >= MAX_SAME_AXIS_RUN:
                runs.append((cur, i - cur_len, cur_len))
            cur, cur_len = a, 1
    if cur_len >= MAX_SAME_AXIS_RUN:
        runs.append((cur, len(axes) - cur_len, cur_len))

    print("  구매 순서(축): " + " ".join(axes))
    print()
    if not runs:
        print("  ✔ PASS — 같은 축 %d연속 이상이 없다." % MAX_SAME_AXIS_RUN)
        return True
    print("  ✖ FAIL — 같은 축 연속 구간 %d개:" % len(runs))
    for axis, at, length in runs:
        names = [order[i]["nodeId"] for i in range(at, at + length)]
        print("     %-8s %d번째부터 %d연속: %s" % (axis, at + 1, length, ", ".join(names)))
    return False


def stress_states(layers):
    """각 축이 병목이 되도록 일부러 비튼 상태들.

    ⚠ 진행 구간(greedy walk)만 훑으면 그 구간에서 병목이 아닌 축이 전부 0으로 나와
      멀쩡한 노드를 '함정'으로 오판한다. 실제로 첫 실행에서 무게 노드가 그렇게
      걸렸다 — 배터리가 계속 병목이라 가방을 키워도 수입이 안 올랐다.
      한계효용은 **그 축이 병목인 상태에서** 재야 한다.
    """
    base = RM.base_stats(str(PLAYER_SO))
    out = []
    # 배터리를 크게 → 무게가 병목이 된다
    out.append((base.copy(max_stamina=200.0), 0.0))
    out.append((base.copy(max_stamina=400.0, weight_limit=4.0), 0.0))
    # 배터리를 작게 → 스태미나가 병목
    out.append((base.copy(max_stamina=12.0), 0.0))
    # 깊은 층 안에서의 상태 — **엘리베이터 시작 깊이로 데려가야 한다.**
    #
    # ⚠ 배터리만 키워서는 못 간다. 최대 스태미나 400이어도 한 판에 파는 깊이는
    #   월드 107이라 Ice층(월드 200~390)에 닿지 않는다. 그러면 그 층 광물의
    #   판매가 노드가 "어떤 상태에서도 0"으로 나와 **함정으로 오진된다.**
    #   실제 게임에서는 엘리베이터로 그 층에서 시작하므로, 모델도 그렇게 둔다.
    for L in layers:
        band_top = -L.start_depth
        for frac in (0.25, 0.6):
            start = band_top + (200.0 * frac)      # 층 두께 200(=청크 20) 기준
            out.append((base.copy(mining_level=L.tier, max_stamina=120.0,
                                  weight_limit=40.0, elevator_unlocked=True),
                        start))
    return out


def visible_fields(layers, states):
    """모델의 income()이 실제로 반응하는 Stats 필드만 골라낸다.

    ⚠ 이게 왜 필요한가: `income()`은 배터리·범위·무게·면허·판매가만 쓴다.
      채굴 속도·이동 속도·벽타기·스태미나 소모율은 **수식에 등장하지 않는다**
      (time_budget을 안 넘기므로 시간축이 죽고, climb_fraction_of_travel 다이얼은
      income()이 아예 안 읽는다). 그 필드를 올리는 노드는 한계효용이 언제나 0이다.

      그것을 '함정'이라 부르면 **트리를 탓하는 오진**이다 — 모델이 못 보는 것뿐이다.
      그래서 노드를 재기 전에 필드부터 검사해 둘을 가른다.
    """
    probes = {
        "mining_range": 3.0, "max_stamina": 500.0, "weight_limit": 500.0,
        "shovel_stamina_mult": 0.05, "mining_speed": 20.0, "move_speed": 20.0,
        "wall_climb_speed": 20.0, "stamina_cost_mult": 0.05,
        "stamina_per_sec": 0.05, "mining_level": 4.0,
    }
    seen = set()
    for field, val in probes.items():
        for stats, start in states:
            b = RM.income(stats, layers, start_depth=start).gold
            a = RM.income(stats.copy(**{field: val}), layers, start_depth=start).gold
            if abs(a - b) > 1e-9:
                seen.add(field)
                break
    return seen


def check_p10(nodes, layers, states, visible):
    """P10 — 어떤 상태에서도 한계효용이 0 이하인 노드는 함정이다.

    ⚠ **한 상태에서만 재면 안 된다.** 모델이 min()으로 짜여 있어 병목이 아닌 축은
      한계효용이 자동으로 0이 된다. 아무 상태에서나 재면 멀쩡한 노드가 전부
      '죽은 재료'로 오판된다. 그래서 진행 구간 전체를 훑어 **최대값**을 본다 —
      한 번이라도 값이 나오면 함정이 아니다.
    """
    print()
    print("=" * 74)
    print("P10 — 함정 없음")
    print("=" * 74)

    def field_of(n):
        if n["effectType"] == "MineralPriceUp":
            return "mineral_price_bonus"
        e = EFFECT_MAP.get(n["effectType"])
        return e[0] if e else None

    # 판매가는 probes로 못 재므로(광물별 dict) 별도 취급 — 값이 실제로 붙는지는
    # 그 광물이 도달 구간에 나오느냐의 문제라 아래 상태 훑기가 판정한다.
    def is_visible(n):
        f = field_of(n)
        return f == "mineral_price_bonus" or f in visible

    # 게이트(면허)는 '못 보는 축'이 아니다 — 천장에 닿았을 때만 값을 하는 통행권이라
    # 검사 구간에서 안 걸리는 것이 정상이다. 이걸 사각지대와 같이 묶으면
    # "모델이 면허를 무시한다"는 틀린 인상을 준다.
    gates = [n for n in nodes if node_axis(n) == "gate"]
    gate_ids = {n["nodeId"] for n in gates}

    modelable = [n for n in nodes
                 if is_modelable(n) and is_visible(n) and n["nodeId"] not in gate_ids]
    blind = [n for n in nodes if is_modelable(n) and not is_visible(n)
             and n["nodeId"] not in gate_ids]
    dead, unmodeled = [], collections.Counter()
    for n in nodes:
        if not is_modelable(n):
            unmodeled[n["effectType"]] += 1

    for n in modelable:
        best = None
        for stats, start in states:
            g = gain_of(stats, n, layers, start)
            if g is not None and (best is None or g > best):
                best = g
        if best is not None and best <= 0:
            dead.append((n, best))

    print("  판정 대상 %d/%d노드 · 검사한 상태 %d개 (진행 구간 + 축별 스트레스)"
          % (len(modelable), len(nodes), len(states)))

    if dead:
        print("  ✖ FAIL — 어떤 상태에서도 수입이 안 오르는 노드 %d개:" % len(dead))
        print()
        print("     %-30s %-22s %8s %10s" % ("노드", "효과", "가격", "최대증가"))
        print("     " + "-" * 74)
        for n, g in sorted(dead, key=lambda t: -t[0]["cost"]):
            print("     %-30s %-22s %8d %10.2f"
                  % (n["nodeId"], n["effectType"], n["cost"], g))
    else:
        print("  ✔ PASS — 표현 가능한 노드는 전부 어느 구간에선가 값을 한다.")

    if blind:
        print()
        print("  ⚠ 모델이 못 보는 축 %d개 — **함정이 아니라 사각지대다.**" % len(blind))
        print("    income()이 이 필드를 수식에 안 쓴다(시간축이 죽어 있고,")
        print("    벽타기 다이얼은 income()이 읽지 않는다). 트리 탓이 아니다.")
        print()
        by_field = collections.defaultdict(list)
        for n in blind:
            by_field[field_of(n)].append(n)
        for f, ns in sorted(by_field.items(), key=lambda kv: -len(kv[1])):
            print("     %-22s %2d개  %s" % (f, len(ns),
                  ", ".join(x["nodeId"] for x in ns[:4])
                  + (" ..." if len(ns) > 4 else "")))

    if gates:
        print()
        print("  · 게이트 %d개는 판정에서 뺀다 — 천장에 닿을 때만 값을 하는 통행권이다:"
              % len(gates))
        print("     " + ", ".join(n["nodeId"] for n in gates))

    if unmodeled:
        print()
        print("  ⚠ 판정 불가 %d개 — 모델이 표현하지 못하는 효과다."
              % sum(unmodeled.values()))
        print("    **'문제 없음'이 아니라 '못 봤다'는 뜻이다.** 배선 여부는")
        print("    check_effect_wiring.py 가 본다.")
        print()
        for et, cnt in unmodeled.most_common():
            print("     %-26s %2d개  %s" % (et, cnt, UNMODELED_NOTE.get(et, "")))

    return (not dead) if modelable else None


def early_coverage(nodes, visible, steps=5):
    """초반 각 단계에서 '모델이 점수를 매길 수 있는 갈래'의 비율.

    ⚠ P11을 판정하기 전에 반드시 봐야 한다. 모델이 못 보는 노드는 효율 0이라
      항상 꼴찌로 밀리므로, 갈래가 넷이어도 **둘만 보이면 둘 중에서만 고른다.**
      그 상태로 "순서가 안 갈린다"고 하면 트리를 탓하는 오진이다 —
      실제로 첫 실행에서 그렇게 나왔다(2단계 갈래 4개 중 Facility_Map(None 효과)과
      MiningSpeed(사각지대)를 0점 처리해 '선택지 2개'로 셌다).

    반환: [(전체 갈래, 점수 매길 수 있는 갈래), ...]
    """
    def scorable(n):
        if n["effectType"] == "MineralPriceUp":
            return True
        e = EFFECT_MAP.get(n["effectType"])
        return bool(e) and e[0] in visible

    owned, out = set(), []
    for _ in range(steps):
        cands = available(nodes, owned)
        if not cands:
            break
        out.append((len(cands), sum(1 for n in cands if scorable(n))))
        owned.add(min(cands, key=lambda n: n["cost"])["nodeId"])
    return out


def check_p11(nodes, layers, visible, steps, variants, seed=12345):
    """P11 — 성향을 바꾸면 첫 5구매가 갈리는가.

    자연 플레이로는 표본이 안 모인다(혼자 하고, 최적 경로를 안다). 대신 효율에
    잡음을 준 '완벽하지 않은 플레이어'를 여러 명 돌려 첫 5구매를 비교한다.
    잡음을 줘도 순서가 같다면, 그 트리는 사실상 일직선이다.

    ⚠ 단, **모델이 초반 갈래의 대부분을 볼 수 있을 때만** 판정한다(early_coverage).
    """
    print()
    print("=" * 74)
    print("P11 — 첫 5구매가 갈린다")
    print("=" * 74)

    seqs = []
    for i in range(variants):
        rng = random.Random(seed + i)
        order, _r, _s = walk(nodes, layers, steps, noise=0.35, rng=rng)
        seqs.append(tuple(n["nodeId"] for n in order[:5]))

    uniq = collections.Counter(seqs)
    ratio = len(uniq) / len(seqs) if seqs else 0.0
    print("  성향 %d명(효율에 ±35%% 잡음) · 서로 다른 첫5 순서 %d개 (%.0f%%)"
          % (len(seqs), len(uniq), ratio * 100))
    print()
    for s, cnt in uniq.most_common(5):
        print("     %2d명  %s" % (cnt, " → ".join(x.replace("_T0", "") for x in s)))

    # 왜 안 갈리는지를 같이 낸다. 가격 때문인지 **선행 구조** 때문인지가 갈린다 —
    # 고를 것이 애초에 없으면 가격을 아무리 만져도 순서는 안 바뀐다.
    cov = early_coverage(nodes, visible)
    print()
    print("  단계별 갈래 (트리가 준 것 / 모델이 점수를 매길 수 있는 것):")
    print("     " + "  ".join("%d단계 %d/%d" % (i + 1, a, b)
                              for i, (a, b) in enumerate(cov)))

    total = sum(a for a, _b in cov)
    scored = sum(b for _a, b in cov)
    frac = (scored / total) if total else 0.0

    print()
    if frac < 0.7:
        print("  판정 불가 — 초반 갈래의 %.0f%%만 모델이 점수를 매길 수 있다." % (frac * 100))
        print("     못 보는 노드는 효율 0이라 항상 꼴찌로 밀린다. 그 상태의 '순서가")
        print("     같다'는 **트리가 아니라 모델의 사각지대**를 재는 것이다.")
        print("     사각지대(§7)를 줄이기 전에는 이 원칙을 판정하지 않는다.")
        return None

    if ratio < 0.2:
        print("  ✖ FAIL — 성향이 달라도 같은 순서로 산다.")
        print("     가격이 아니라 선행 구조를 볼 것 — 위 갈래 수가 근거다.")
        return False
    print("  ✔ PASS")
    return True


# ==========================================================================

def main():
    ap = argparse.ArgumentParser(description="티어 B 원칙 체커 (P4/P7/P10/P11)")
    ap.add_argument("--steps", type=int, default=20, help="진행 구간 수 (기본 20)")
    ap.add_argument("--variants", type=int, default=12, help="P11 성향 수 (기본 12)")
    a = ap.parse_args()

    nodes = [n for n in TREE.load(str(TREE_PATH)) if not n["locked"]]
    by_id = {n["nodeId"]: n for n in nodes}
    layers = RM.load_layers(str(TILE_DATA), str(PRICE_DATA))

    print("트리 %d노드 · 모델 표현 가능 %d노드"
          % (len(nodes), sum(1 for n in nodes if is_modelable(n))))
    print("⚠ 가격은 사람이 맞춘 값이다. 이 스크립트는 채점만 한다 (charter §1).")
    print()

    order, ranks, _final = walk(nodes, layers, a.steps)

    # P10 이 훑을 상태들 — 진행하면서 스탯이 변하므로 병목도 바뀐다.
    states, stats, reached = [], RM.base_stats(str(PLAYER_SO)), 0.0
    for n in order:
        start = RM.elevator_start(stats, reached, RM.reachable_depth(stats, layers))
        states.append((stats, start))
        nxt = apply_node(stats, n)
        if nxt is not None:
            stats = nxt
        reached = max(reached, RM.income(stats, layers, start_depth=start)
                      .detail.get("depth", reached))

    states += stress_states(layers)
    visible = visible_fields(layers, states)

    r4 = check_p4(ranks, by_id)
    r7 = check_p7(order)
    r10 = check_p10(nodes, layers, states, visible)
    r11 = check_p11(nodes, layers, visible, a.steps, a.variants)

    print()
    print("=" * 74)
    print("요약 (티어 B)")
    print("=" * 74)
    for key, label, v in (("P4", "지배 노드 없음", r4), ("P7", "병목이 돌아간다", r7),
                          ("P10", "함정 없음", r10), ("P11", "첫 5구매가 갈린다", r11)):
        print("  %-4s %-20s %s" % (key, label,
              "PASS" if v is True else ("FAIL" if v is False else "판정불가")))
    print()
    print("  ⚠ 이 판정은 전부 런 모델 위에 서 있다. check_principles.py 의 P0가")
    print("    PASS일 때만 믿을 것.")

    return 1 if False in (r4, r7, r10, r11) else 0


if __name__ == "__main__":
    sys.exit(main())
