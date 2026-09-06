#!/usr/bin/env python3
"""한계효용 — 지금 무엇을 사면 잠수 1회 수입이 가장 오르나.

run_model.income()이 min()으로 짜여 있어서, **병목이 아닌 축은 한계효용이
자동으로 0**이 된다. 순환을 만들기 위한 별도 규칙이 필요 없다는 뜻이다.

2026-08-22 깊이 기반 모델로 개편: 층 하나가 아니라 지층 전체(layers)를 받고,
엘리베이터 시작 깊이(start_depth)가 상태로 들어온다.

  python Tools/telemetry/marginal.py                  # 시작 스탯에서
  python Tools/telemetry/marginal.py --late           # 면허 I + 엘베 이후 상태에서
  python Tools/telemetry/marginal.py --walk 12        # 계속 사며 병목이 도는 것을 본다

설계: Assets/Docs/superpowers/specs/2026-08-21-upgrade-tree-autogen-design.md §7.1
표준 라이브러리만 쓴다.
"""

import argparse
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))

import run_model as RM
import upgrade_catalog as CAT

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):
        pass


def marginal(stats, material, layers, start_depth=0.0):
    """이 재료를 하나 샀을 때 잠수 1회 수입이 얼마나 오르나."""
    before = RM.income(stats, layers, start_depth=start_depth).gold
    after = RM.income(material.apply(stats), layers, start_depth=start_depth).gold
    return after - before


def rank(stats, layers, start_depth=0.0, materials=None):
    """한계효용 내림차순. (재료, 증가분) 목록."""
    mats = materials if materials is not None else CAT.CHOOSABLE
    scored = [(m, marginal(stats, m, layers, start_depth)) for m in mats]
    scored.sort(key=lambda x: -x[1])
    return scored


def best(stats, layers, cost_of=None, start_depth=0.0, materials=None):
    """다음에 살 것 하나. (재료, 증가분, 효율)

    **효율(증가 / 가격)로 고른다.** cost_of(material) -> 가격을 주면 효율로,
    안 주면 증가분으로 고른다.

    ⚠ "직전과 같은 축은 건너뛴다"는 순환 강제 규칙은 **일부러 넣지 않았다.**
    2026-08-21에 넣어 봤더니 병목 축을 연속으로 못 사서 교착했다.
    min()이 이미 순환을 만든다 — 강제하면 돕는 게 아니라 방해한다.
    """
    ranked = rank(stats, layers, start_depth, materials)
    if cost_of is None:
        for m, gain in ranked:
            if gain > 0:
                return m, gain, gain
        return None, 0.0, 0.0

    best_m, best_gain, best_roi = None, 0.0, 0.0
    for m, gain in ranked:
        if gain <= 0:
            continue
        cost = cost_of(m)
        if cost <= 0:
            continue
        roi = gain / cost
        if roi > best_roi:
            best_m, best_gain, best_roi = m, gain, roi
    return best_m, best_gain, best_roi


def print_rank(stats, layers, start_depth=0.0):
    r = RM.income(stats, layers, start_depth=start_depth)
    print("현재: %.0fG / 병목 %s / 깊이 %.1f (시작 %.0f)"
          % (r.gold, r.bottleneck, r.detail.get("depth", 0), start_depth))
    scored = rank(stats, layers, start_depth)
    unit = max(1.0, max((g for _, g in scored), default=1.0) / 40.0)
    print("  %-22s %-9s %10s" % ("재료", "축", "증가"))
    for m, gain in scored:
        print("  %-22s %-9s %+10.1f  %s"
              % (m.label, m.axis, gain, "#" * int(max(0.0, gain) / unit)))


def walk(stats, layers, steps, cost_of=None):
    """계속 사면서 병목이 어떻게 도는지 본다. 합성기의 축소판.

    판마다 도달 깊이를 누적해 엘리베이터 시작점(elevator_start)이 따라 내려온다.
    """
    print("연속 구매 시뮬레이션 (%s 기준)\n" % ("효율" if cost_of else "증가분"))
    print("  %3s %-20s %-9s %10s %10s %8s   %s"
          % ("#", "산 것", "축", "증가", "수입(G)", "깊이", "다음 병목"))

    cur = stats
    reached = 0.0
    for i in range(1, steps + 1):
        start = RM.elevator_start(cur, reached, RM.reachable_depth(cur, layers))
        m, gain, _ = best(cur, layers, cost_of, start)
        if m is None:
            print("  -- 더 살 것이 없다 (남은 재료의 한계효용이 전부 0) --")
            break
        cur = m.apply(cur)
        r = RM.income(cur, layers, start_depth=start)
        d = r.detail
        reached = max(reached, d.get("depth", reached))
        print("  %3d %-20s %-9s %+10.1f %10.0f %8.1f   %s"
              % (i, m.label, m.axis, gain, r.gold, d.get("depth", 0), r.bottleneck))
    return cur


def main():
    ap = argparse.ArgumentParser(description="한계효용 — 다음에 뭘 사야 하나")
    ap.add_argument("--weight", type=float, help="무게 한도를 이 값으로 놓고 본다")
    ap.add_argument("--stamina", type=float, help="최대 스태미나(배터리)를 이 값으로")
    ap.add_argument("--mining-level", type=int)
    ap.add_argument("--elevator", action="store_true", help="엘리베이터 업그레이드 보유 상태로")
    ap.add_argument("--start-depth", type=float, default=0.0, help="판 시작 깊이(엘베 정류장)")
    ap.add_argument("--late", action="store_true",
                    help="면허 I + 엘베 + 배터리 업그레이드 이후의 후반 상태로 본다")
    ap.add_argument("--walk", type=int, nargs="?", const=12, metavar="N",
                    help="N번 연속으로 사면서 병목 순환을 본다 (기본 12)")
    a = ap.parse_args()

    layers = RM.load_layers()
    stats = RM.base_stats()
    if a.late:
        stats = stats.copy(mining_level=1, elevator_unlocked=True,
                           shovel_stamina_mult=0.64, max_stamina=42)
    if a.weight is not None:
        stats = stats.copy(weight_limit=a.weight)
    if a.stamina is not None:
        stats = stats.copy(max_stamina=a.stamina)
    if a.mining_level is not None:
        stats = stats.copy(mining_level=a.mining_level)
    if a.elevator:
        stats = stats.copy(elevator_unlocked=True)

    if a.walk:
        walk(stats, layers, a.walk)
    else:
        print_rank(stats, layers, a.start_depth)
    return 0


if __name__ == "__main__":
    sys.exit(main())
