#!/usr/bin/env python3
"""오토플레이 시뮬레이터 — 봇이 현재 트리(UpgradeTree.csv)로 처음부터 플레이한다.

무엇에 답하나
-------------
  · 면허까지 며칠 걸리나 (트리 페이스의 최종 답)
  · 다이브당 하나 리듬이 유지되나 (하루 2개 구매 / 3일 무구매 검출)
  · 봇 정책마다 경로가 갈리나 (전부 같은 순서면 선택지가 가짜다)

CSV를 직접 읽으므로 **트리를 고치면 바로 다시 잰다** — 손 수정·locked 행 포함.
하루 루프: 잠수 수입(런 모델) -> 트리 몫 저축 -> 정책대로 구매 -> 다음 날.

    python Tools/telemetry/autoplay.py                 # 정책 3종 요약
    python Tools/telemetry/autoplay.py --policy greedy --log   # 일차별 상세
    python Tools/telemetry/autoplay.py --days 60

설계: Assets/Docs/superpowers/specs/2026-08-21-upgrade-tree-autogen-design.md §8
표준 라이브러리만 쓴다.
"""

import argparse
import random
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))

import run_model as RM
import synth_tree as ST
import upgrade_tree_csv as UTC

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):
        pass


# effectType -> Stats 반영. 여기 없는 타입은 풍미(수입 무관)로 취급한다.
# (isPercentage가 true면 곱, false면 합 — CSV 규약 그대로)
EFFECT_FIELD = {
    "MiningRangeMultiplier": "mining_range",
    "InventoryWeightUp": "weight_limit",
    "MaxStaminaUp": "max_stamina",
    "ShovelStaminaReduce": "shovel_stamina_mult",
    "StaminaCostMultiplier": "stamina_cost_mult",
    "MiningSpeedMultiplier": "mining_speed",
    "MoveSpeedMultiplier": "move_speed",
    "ClimbSpeedMultiplier": "wall_climb_speed",
    "WallClimbSpeed": "wall_climb_speed",
    "MiningLevel": "mining_level",
}


def apply_node(stats, row):
    """노드 구매를 스탯에 반영한 새 Stats."""
    if row["nodeId"].startswith("Facility_Elevator"):
        return stats.copy(elevator_unlocked=True)
    field = EFFECT_FIELD.get(row["effectType"])
    if field is None:
        return stats                      # 풍미/통행권 — 수입 모델 밖
    cur = getattr(stats, field)
    new = cur * row["effectValue"] if row["isPercentage"] else cur + row["effectValue"]
    return stats.copy(**{field: new})


def model_gain(stats, row, layers, start):
    before = RM.income(stats, layers, start_depth=start).gold
    after = RM.income(apply_node(stats, row), layers, start_depth=start).gold
    return after - before


def purchasable(rows, owned):
    """선행(AND)을 전부 산 미보유 노드."""
    out = []
    for r in rows:
        if r["nodeId"] in owned:
            continue
        if all(p in owned for p in r["parentIds"]):
            out.append(r)
    return out


def pick(policy, cands, stats, layers, start, rng):
    if policy == "cheapest":
        return min(cands, key=lambda r: r["cost"])
    if policy == "random":
        return rng.choice(cands)
    # greedy: 모델 이득/가격 최대. 전부 0(풍미뿐)이면 싼 것.
    best, best_roi = None, -1.0
    for r in cands:
        roi = model_gain(stats, r, layers, start) / max(r["cost"], 1)
        if roi > best_roi:
            best, best_roi = r, roi
    if best_roi <= 0:
        return min(cands, key=lambda r: r["cost"])
    return best


def run(rows, policy, max_days=60, seed=1, log=False):
    layers = RM.load_layers()
    stats = RM.base_stats()
    rng = random.Random(seed)
    gold = 0.0
    reached = 0.0
    owned = set()
    history = []            # (day, income, bought[])
    license_day = None
    license_ids = {r["nodeId"] for r in rows if r["effectType"] == "MiningLevel"}

    for day in range(1, max_days + 1):
        start = RM.elevator_start(stats, reached, RM.reachable_depth(stats, layers))
        r = RM.income(stats, layers, start_depth=start)
        income = r.gold
        gold += income * ST.TREE_SHARE   # 나머지 몫은 장비·포션 등 다른 sink
        reached = max(reached, r.detail.get("depth", reached))

        bought = []
        while True:
            cands = [c for c in purchasable(rows, owned) if c["cost"] <= gold]
            if not cands:
                break
            c = pick(policy, cands, stats, layers, start, rng)
            gold -= c["cost"]
            owned.add(c["nodeId"])
            stats = apply_node(stats, c)
            bought.append(c)
            if c["nodeId"] in license_ids and license_day is None:
                license_day = day

        history.append((day, income, start, bought, gold))
        if log:
            names = ", ".join("%s(%dG)" % (b["displayNameKey"], b["cost"]) for b in bought)
            print("  d%-3d 시작깊이 %4.0f  수입 %4.0fG  잔액 %5.0fG  %s"
                  % (day, start, income, gold, names or "-"))
        if len(owned) == len(rows):
            break

    return dict(policy=policy, history=history, owned=owned,
                license_day=license_day, days=len(history),
                order=[b["nodeId"] for _, _, _, bs, _ in history for b in bs])


def rhythm_report(res, n_nodes):
    multi = [(d, len(bs)) for d, _, _, bs, _ in res["history"] if len(bs) >= 2]
    streak, worst = 0, 0
    for _, _, _, bs, _ in res["history"]:
        streak = 0 if bs else streak + 1
        worst = max(worst, streak)
    return multi, worst


def main():
    ap = argparse.ArgumentParser(description="트리 오토플레이 (봇 진행 시뮬)")
    ap.add_argument("--csv", default=UTC.DEFAULT_PATH)
    ap.add_argument("--days", type=int, default=60)
    ap.add_argument("--policy", choices=["greedy", "cheapest", "random", "all"],
                    default="all")
    ap.add_argument("--seed", type=int, default=1)
    ap.add_argument("--log", action="store_true", help="일차별 상세 출력")
    a = ap.parse_args()

    rows = UTC.load(a.csv)
    print("트리: %s (%d노드)  TREE_SHARE %.2f\n" % (a.csv, len(rows), ST.TREE_SHARE))

    policies = ["greedy", "cheapest", "random"] if a.policy == "all" else [a.policy]
    results = []
    for pol in policies:
        if a.log and len(policies) == 1:
            print("[%s]" % pol)
        res = run(rows, pol, a.days, a.seed, log=a.log and len(policies) == 1)
        results.append(res)
        multi, worst = rhythm_report(res, len(rows))
        done = len(res["owned"])
        print("[%-8s] 면허 %s일차 / 트리 완주 %s / 하루2개+ %d번 / 최장 무구매 %d일"
              % (pol,
                 res["license_day"] if res["license_day"] else "미달성",
                 ("%d일" % res["days"]) if done == len(rows) else
                 ("%d/%d개에서 %d일 종료" % (done, len(rows), res["days"])),
                 len(multi), worst))

    # 지배 경로 검사 — 정책별 구매 순서가 얼마나 같은가
    if len(results) >= 2:
        base = results[0]["order"]
        for other in results[1:]:
            same = sum(1 for x, y in zip(base, other["order"]) if x == y)
            n = max(len(base), 1)
            print("\n구매 순서 일치 (%s vs %s): %d/%d (%.0f%%)"
                  % (results[0]["policy"], other["policy"], same, n, 100.0 * same / n),
                  end="")
            if same / n > 0.9:
                print("   <-- 90%+ 일치: 선택지가 사실상 없다", end="")
        print()
    return 0


if __name__ == "__main__":
    sys.exit(main())
