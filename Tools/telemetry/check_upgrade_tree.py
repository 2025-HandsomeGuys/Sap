#!/usr/bin/env python3
"""UpgradeTreeGenerator.cs를 읽어 업그레이드 트리를 진단한다.

왜 있나
-------
fit_upgrade_prices.py는 "이런 수입 곡선이면 가격 사다리가 이래야 한다"를 뽑는다.
하지만 정작 **지금 트리가 그 사다리 위에 있는지**는 아무도 안 본다 — Unity를
띄워 EditMode 테스트를 돌리기 전까지는. 곁가지를 필수로 올리거나 선행을 바꾸면
필수 노드 집합 자체가 달라져서 사다리를 통째로 다시 짜야 하는데, 그 판단을
손으로 하다 보니 매번 같은 계산을 반복하게 됐다. 그걸 없애기 위한 도구다.

이 스크립트는 UpgradeTree.csv(트리의 단일 원본)를 읽으므로
Unity도, 에셋 재생성도 필요 없다. CSV를 고치고 바로 돌려보면 된다.

  python Tools/telemetry/check_upgrade_tree.py              # 진단
  python Tools/telemetry/check_upgrade_tree.py --suggest    # 사다리 재적합 + 노드별 배정

'필수'의 정의
-------------
면허 노드에서 parentIds를 거슬러 올라가 닿는 모든 노드. UpgradeManager.CanUnlock이
부모를 전부(AND) 검사하므로, 조상 집합에 들어가면 반드시 사야 하는 노드가 된다.
좌표는 화면 배치일 뿐 필수 여부와 무관하다 — 이 스크립트가 그 둘을 갈라 보여준다.

표준 라이브러리만 쓴다 — export_csv.py / fit_upgrade_prices.py와 같은 방침이다.
"""

import argparse
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
import upgrade_tree_csv

# Windows 한국어 콘솔(cp949)은 em dash 등을 못 찍고 죽는다. 다른 도구와 동일 처리.
for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):
        pass

DEFAULT_SRC = upgrade_tree_csv.DEFAULT_PATH
DEFAULT_TESTS = "Assets/Tests/EditMode/UpgradeTreeCostTests.cs"


class Node:
    """CSV 한 행. 속성 이름은 옛 C# 파서 시절 그대로다 —
    ancestors / topo_order / simulate / structural_checks가 전부 이 이름을 쓴다."""

    __slots__ = ("id", "name", "tier", "cost", "pos", "parents", "effect")

    def __init__(self, row):
        self.id = row["nodeId"]
        self.name = row["displayNameKey"]
        self.tier = row["tier"]
        self.cost = row["cost"]
        self.pos = (row["uiX"], row["uiY"])
        self.parents = row["parentIds"]
        self.effect = row["effectType"]

    @property
    def is_facility(self):
        return self.id.startswith("Facility_")


def parse(src):
    nodes = [Node(r) for r in upgrade_tree_csv.load(src)]
    if not nodes:
        sys.exit("노드를 하나도 못 읽었다 — %s를 확인할 것" % src)
    return {n.id: n for n in nodes}


def ancestors(nodes, root_id):
    """root_id와 그 모든 조상. = 그 노드를 사려면 반드시 거쳐야 하는 집합."""
    seen, stack = set(), [root_id]
    while stack:
        cur = stack.pop()
        if cur in seen:
            continue
        seen.add(cur)
        node = nodes.get(cur)
        if node is None:
            continue
        stack.extend(node.parents)
    return seen


def topo_order(nodes, ids):
    """부모가 항상 먼저 오도록 정렬. 동순위는 현재 가격 오름차순.

    사다리 값을 노드에 배정할 때 쓴다 — 구매 순서와 가격 순서가 어긋나면
    "싼 걸 먼저 산다"는 전제가 깨진다.
    """
    idset = set(ids)
    depth = {}

    def d(i, guard=()):
        if i in depth:
            return depth[i]
        if i in guard:            # 사이클 — 여기서 끊는다(진단은 structural_checks가 따로 본다)
            return 0
        node = nodes.get(i)
        if node is None:
            return 0
        parents = [p for p in node.parents if p in idset]
        depth[i] = 0 if not parents else 1 + max(d(p, guard + (i,)) for p in parents)
        return depth[i]

    for i in ids:
        d(i)
    return sorted(ids, key=lambda i: (depth[i], nodes[i].cost, i))


def simulate(prices, base, growth):
    """'매 다이브 하나만 사진다'를 검사. UpgradeTreeCostTests와 같은 규칙."""
    rows, problems = [], []
    wallet = 0.0
    for k, price in enumerate(prices):
        income = base + growth * k
        wallet += income
        if wallet < price:
            problems.append("%d번째 다이브: 하나도 못 산다 (지갑 %.0f < %d)" % (k + 1, wallet, price))
            rows.append((k + 1, income, price, wallet, "못 삼"))
            continue
        wallet -= price
        nxt = prices[k + 1] if k + 1 < len(prices) else None
        flag = ""
        if nxt is not None and wallet >= nxt:
            problems.append("%d번째 다이브: 두 개가 사진다 (잔액 %.0f >= 다음 %d)" % (k + 1, wallet, nxt))
            flag = "두 개"
        rows.append((k + 1, income, price, wallet, flag))
    return rows, problems


def structural_checks(nodes, required):
    """가격과 무관한 구조 문제. 여기 걸리면 사다리를 논할 단계가 아니다.

    반환: (문제, 참고). 참고는 곁가지라 실제로는 안 깨지는 것들이다 —
    필수 경로 밖의 노드는 언제 사든 상관없으므로 가격 역전이 문제가 아니다.
    """
    out, notes = [], []

    for n in nodes.values():
        for p in n.parents:
            if p not in nodes:
                out.append("%s: 선행 '%s'가 트리에 없다 (id 오타 의심)" % (n.id, p))

    seen = {}
    for n in nodes.values():
        if n.pos in seen:
            out.append("좌표 충돌 %s: %s 와 %s" % (n.pos, seen[n.pos], n.id))
        seen[n.pos] = n.id

    # 부모보다 싼 자식. 필수 경로 안에서만 문제다 — 사다리가 '싼 것부터' 훑는
    # 오름차순 경로를 전제하므로, 필수 노드끼리 역전되면 그 전제가 깨진다.
    for n in nodes.values():
        for p in n.parents:
            par = nodes.get(p)
            if par and par.cost > n.cost:
                msg = "%s(%dG)가 선행 %s(%dG)보다 싸다" % (n.id, n.cost, p, par.cost)
                (out if (n.id in required and p in required) else notes).append(msg)

    # 시설이 필수 경로에 들어가면 T0 예산이 시설 값까지 떠안는다.
    # 2026-08-15 설계가 엘리베이터·단말기를 일부러 필수로 올렸으므로 이제는
    # 경고가 아니라 사실 보고다 — UpgradeTreeCostTests는 아직 옛 전제라 여기서 걸린다.
    fac = sorted(i for i in required if nodes[i].is_facility)
    if fac:
        notes.append("필수 경로에 시설 노드가 있다: %s "
                     "(FacilityUnlockNodes_AreNotOnLicensePath가 이걸 금지한다)" % ", ".join(fac))

    return out, notes


def read_test_expectations(path):
    """테스트 파일에서 기대 상수를 긁어온다. 실패해도 진단은 계속한다."""
    try:
        text = Path(path).read_text(encoding="utf-8")
    except OSError:
        return {}
    exp = {}
    m = re.search(r"Assert\.AreEqual\((\d+),\s*LoadNodes\(\)\.Count\)", text)
    if m:
        exp["노드 총 개수"] = int(m.group(1))
    m = re.search(r"Assert\.AreEqual\((\d+),\s*SumTierExcludingFacilities\(LoadNodes\(\),\s*0\)", text)
    if m:
        exp["T0 총액(시설 제외)"] = int(m.group(1))
    m = re.search(r"Assert\.AreEqual\((\d+),\s*sum,", text)
    if m:
        exp["T0 최소 경로"] = int(m.group(1))
    m = re.search(r'Assert\.AreEqual\((\d+),\s*nodes\["MiningLevel_T0_Final"\]\.cost', text)
    if m:
        exp["면허 I 가격"] = int(m.group(1))

    # 2026-08-21: 이 기대값들은 전부 [Ignore] 상태다(설계 §9.1). 숫자는 참고용으로 남기되
    # 라벨에 표시해서, 진단 출력이 "지금 지켜지고 있는 값"처럼 읽히지 않게 한다.
    if "[Ignore(" in text:
        exp = {"%s (Ignore됨)" % k: v for k, v in exp.items()}
    return exp


def main():
    ap = argparse.ArgumentParser(description="업그레이드 트리 진단 (필수 경로·리듬·구조)")
    ap.add_argument("--src", default=DEFAULT_SRC)
    ap.add_argument("--tests", default=DEFAULT_TESTS)
    ap.add_argument("--license", default="MiningLevel_T0_Final", help="기준 면허 노드 id")
    ap.add_argument("--base", type=float, default=60.0, help="첫 다이브 수입 (기본 60)")
    ap.add_argument("--growth", type=float, default=64.0, help="노드 1개당 수입 증가분 (기본 64)")
    ap.add_argument("--step", type=int, default=10, help="가격 반올림 단위")
    ap.add_argument("--margin", type=float, default=0.92, help="가격 / 그 다이브 수입 비율")
    ap.add_argument("--suggest", action="store_true", help="사다리를 다시 뽑아 노드별로 배정해 보여준다")
    args = ap.parse_args()

    nodes = parse(args.src)
    if args.license not in nodes:
        sys.exit("면허 노드 '%s'가 없다" % args.license)

    req = ancestors(nodes, args.license)
    req_wo_license = [i for i in req if i != args.license]
    order = topo_order(nodes, req_wo_license)
    prices = [nodes[i].cost for i in order]
    license_cost = nodes[args.license].cost
    tier = nodes[args.license].tier

    print("소스: %s" % args.src)
    print("노드 %d개 / 면허 %s 기준 필수 %d개 (면허 제외 %d)\n"
          % (len(nodes), args.license, len(req), len(order)))

    print("필수 경로 (구매 순서)")
    print("  #  가격   노드                             좌표")
    for k, i in enumerate(order, 1):
        n = nodes[i]
        mark = " [시설]" if n.is_facility else ""
        print("%3d  %5d  %-32s (%6.0f,%6.0f)%s" % (k, n.cost, n.id, n.pos[0], n.pos[1], mark))
    print("     %5d  %-32s <- 면허" % (license_cost, args.license))

    side = [n for n in nodes.values() if n.tier == tier and n.id not in req]
    if side:
        print("\n곁가지 (안 사도 면허를 딸 수 있다)")
        for n in sorted(side, key=lambda n: n.cost):
            print("     %5d  %-32s (%6.0f,%6.0f)" % (n.cost, n.id, n.pos[0], n.pos[1]))

    total_tier = sum(n.cost for n in nodes.values() if n.tier == tier)
    total_wo_fac = sum(n.cost for n in nodes.values() if n.tier == tier and not n.is_facility)
    min_path = sum(prices) + license_cost

    print("\nT%d 총액          %6dG   (시설 제외 %dG)" % (tier, total_tier, total_wo_fac))
    print("최소 경로         %6dG   (필수 %d + 면허 %d)" % (min_path, sum(prices), license_cost))

    rows, problems = simulate(prices, args.base, args.growth)
    print("\n다이브 리듬  (수입 %.0f + %.0fk)" % (args.base, args.growth))
    print("다이브  수입   가격   잔액")
    for k, income, price, wallet, flag in rows:
        print("%5d  %5.0f  %5d  %5.0f  %s" % (k, income, price, wallet, flag))

    issues, notes = structural_checks(nodes, req)
    if issues:
        print("\n[구조 문제]")
        for i in issues:
            print("  " + i)
    if notes:
        print("\n[참고]")
        for i in notes:
            print("  " + i)

    if problems:
        print("\n[리듬 위반]")
        for p in problems:
            print("  " + p)
    else:
        print("\n[OK] 매 다이브 정확히 하나씩 사진다")

    exp = read_test_expectations(args.tests)
    if exp:
        actual = {
            "노드 총 개수": len(nodes),
            "T0 총액(시설 제외)": total_wo_fac,
            "T0 최소 경로": min_path,
            "면허 I 가격": license_cost,
        }
        print("\nEditMode 테스트 기대값 대조 (%s)" % args.tests)
        print("  항목                    기대     실제")
        for key, want in exp.items():
            got = actual.get(key)
            state = "OK" if got == want else "어긋남"
            print("  %-20s %7d  %7s   %s" % (key, want, got, state))
        print("  ※ 기대값은 사람이 정한다 — 어긋난 것을 여기 맞춰 고치지 말 것 (CLAUDE.md)")

    if args.suggest:
        ladder = []
        for k in range(len(order)):
            income = args.base + args.growth * k
            ladder.append(max(args.step, int(round(income * args.margin / args.step)) * args.step))
        _, lp = simulate(ladder, args.base, args.growth)
        print("\n제안 사다리 (margin %s, step %d)" % (args.margin, args.step))
        print("  #   현재 ->  제안   노드")
        for k, i in enumerate(order):
            n = nodes[i]
            diff = "" if n.cost == ladder[k] else "  *"
            print("%3d  %5d -> %5d   %s%s" % (k + 1, n.cost, ladder[k], n.id, diff))
        print("    합계 %dG   면허 %dG   최소경로 %dG"
              % (sum(ladder), license_cost, sum(ladder) + license_cost))
        if lp:
            print("  [경고] 제안 사다리도 위반이 있다 — margin/growth를 조정할 것")
            for p in lp:
                print("    " + p)
        else:
            print("  [OK] 제안 사다리는 위반 없음")


if __name__ == "__main__":
    main()
