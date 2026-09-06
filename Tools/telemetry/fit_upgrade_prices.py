#!/usr/bin/env python3
"""실측 매출에 맞춰 업그레이드 트리 가격 사다리를 뽑는다.

설계 근거: Assets/Docs/economy/mineral-price-design.md §2.5-A

왜 있나
-------
원래 예산(§7)은 "1층 회당 1,430G"라는 모델값에서 역산했는데, 실제 플레이는
회당 100G였다. 모델을 고치는 대신 **버는 만큼에 예산을 맞추기로** 했으므로,
측정할 때마다 사다리를 다시 짜야 한다. 그 계산을 손으로 하지 않기 위한 도구다.

핵심 규칙 — "다이브마다 하나는 사지고, 두 번째는 모자란다"
----------------------------------------------------------
업그레이드를 살수록 회당 매출이 오른다(스태미나↑ = 더 오래, 삽 크기↑ = 더 빨리).
그래서 가격이 평평하면 후반에 한 다이브로 두 개를 사게 된다. 수입 곡선

    income(k) = base + growth * (k - 1)        # k번째 다이브

에 가격을 붙여 매기고, 시뮬레이션으로 규칙 위반을 잡아낸다.

base / growth 구하기
--------------------
days.csv의 `mineral_sale`(하루 광물 판매액)과 `unlocked_nodes`(산 노드 수),
그리고 dives.csv에서 센 하루 다이브 횟수로 회당 매출을 만들고,
노드 수에 대해 최소제곱 직선을 적합한다.

    회당매출 = base + growth * 노드수

사용법
------
    # 실측에서 뽑기
    python fit_upgrade_prices.py --csv-dir <export_dir> --nodes 9

    # 수치를 직접 주고 사다리만 보기
    python fit_upgrade_prices.py --income 100 --growth 8 --nodes 9

표준 라이브러리만 쓴다 — export_csv.py와 같은 방침이다.
"""

import argparse
import csv
import sys
from collections import defaultdict
from pathlib import Path

# Windows 한국어 콘솔(cp949)은 em dash 등을 못 찍고 죽는다. export_csv.py와 동일 처리.
for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):
        pass


def _num(row, key):
    """CSV 셀을 float으로. 빈 칸·비숫자는 None (0으로 읽으면 거짓말이 된다)."""
    raw = (row.get(key) or "").strip()
    if not raw:
        return None
    try:
        return float(raw)
    except ValueError:
        return None


def measure(csv_dir):
    """days.csv + dives.csv → [(노드수, 회당매출), ...]

    하루 광물 판매액을 그날의 다이브 횟수로 나눈다. 하루 단위로 남는 이유는
    `mineral_sale`이 day_settled에만 있기 때문이다(dive_end는 지하에서 발화해
    가격표를 못 본다 — balance-csv-design.md §5.1).
    """
    days_path = Path(csv_dir) / "days.csv"
    dives_path = Path(csv_dir) / "dives.csv"
    for p in (days_path, dives_path):
        if not p.exists():
            sys.exit(f"없다: {p}  (먼저 export_csv.py를 돌릴 것)")

    # (run_id, day) → 다이브 횟수
    dive_count = defaultdict(int)
    with dives_path.open(encoding="utf-8") as f:
        for row in csv.DictReader(f):
            dive_count[(row.get("run_id"), row.get("day"))] += 1

    points = []
    skipped = 0
    with days_path.open(encoding="utf-8") as f:
        for row in csv.DictReader(f):
            sale = _num(row, "mineral_sale")
            nodes = _num(row, "unlocked_nodes")
            dives = dive_count.get((row.get("run_id"), row.get("day")), 0)
            # 판매액이 없거나 그날 안 내려갔으면 회당 매출을 만들 수 없다.
            if sale is None or nodes is None or dives == 0 or sale <= 0:
                skipped += 1
                continue
            points.append((nodes, sale / dives))

    if skipped:
        print(f"  (판매액·노드수·다이브가 없어 건너뛴 날: {skipped})")
    return points


def fit_line(points):
    """최소제곱 직선. 점이 1개뿐이면 기울기 0으로 두고 그 값을 절편으로 쓴다."""
    n = len(points)
    if n == 0:
        return None, None
    if n == 1:
        return points[0][1], 0.0

    mx = sum(x for x, _ in points) / n
    my = sum(y for _, y in points) / n
    denom = sum((x - mx) ** 2 for x, _ in points)
    if denom == 0:          # 노드 수가 전부 같다 — 기울기를 못 구한다
        return my, 0.0
    slope = sum((x - mx) * (y - my) for x, y in points) / denom
    return my - slope * mx, slope


def build_ladder(base, growth, count, step, margin):
    """다이브 k의 수입에 margin을 곱하고 step으로 반올림해 가격을 만든다.

    margin < 1이라 매 다이브 잔액이 조금씩 남고, 다음 가격이 그 잔액보다 커서
    두 번째를 못 산다. step은 가격을 '읽히는 숫자'로 만든다(10G 단위 등).
    """
    prices = []
    for k in range(1, count + 1):
        income = base + growth * (k - 1)
        prices.append(max(step, round(income * margin / step) * step))
    return prices


def simulate(prices, base, growth):
    """규칙 검사. 반환: (표 행들, 위반 목록)"""
    rows, problems = [], []
    wallet = 0.0
    for k, price in enumerate(prices, start=1):
        income = base + growth * (k - 1)
        wallet += income
        if wallet < price:
            problems.append(f"{k}번째 다이브: 하나도 못 산다 (지갑 {wallet:.0f} < {price})")
            rows.append((k, income, price, wallet, "못 삼"))
            continue
        wallet -= price
        nxt = prices[k] if k < len(prices) else None
        if nxt is not None and wallet >= nxt:
            problems.append(f"{k}번째 다이브: 두 개가 사진다 (잔액 {wallet:.0f} ≥ 다음 {nxt})")
        rows.append((k, income, price, wallet, "" if nxt is None else str(nxt)))
    return rows, problems


def main():
    ap = argparse.ArgumentParser(description="실측 매출에 맞춘 업그레이드 가격 사다리")
    ap.add_argument("--csv-dir", help="export_csv.py 출력 디렉토리 (days.csv/dives.csv)")
    ap.add_argument("--income", type=float, help="회당 매출. 주면 --csv-dir 적합을 덮어쓴다")
    ap.add_argument("--growth", type=float, help="업그레이드 1개당 회당 매출 증가분")
    ap.add_argument("--nodes", type=int, default=9, help="면허 제외 필수 노드 수 (기본 9)")
    ap.add_argument("--step", type=int, default=10, help="가격 반올림 단위 (기본 10)")
    ap.add_argument("--margin", type=float, default=0.92,
                    help="가격 / 그 다이브 수입 비율. 낮출수록 잔액이 늘어 널널해진다")
    ap.add_argument("--license-share", type=float, default=0.22,
                    help="면허가 총액에서 차지할 비중 (기본 0.22)")
    args = ap.parse_args()

    base, growth = args.income, args.growth

    if args.csv_dir:
        points = measure(args.csv_dir)
        if not points:
            sys.exit("회당 매출을 만들 수 있는 날이 없다 — 더 플레이하고 다시 export할 것")
        fb, fg = fit_line(points)
        print(f"실측 {len(points)}일치 적합: 회당매출 ≈ {fb:.0f} + {fg:.1f} × 노드수")
        lo = min(y for _, y in points)
        hi = max(y for _, y in points)
        print(f"  회당 매출 범위 {lo:.0f} ~ {hi:.0f}G")
        if base is None:
            base = fb
        if growth is None:
            growth = fg

    if base is None:
        sys.exit("--income 을 주거나 --csv-dir 로 실측을 넣을 것")
    if growth is None:
        growth = 0.0

    prices = build_ladder(base, growth, args.nodes, args.step, args.margin)
    rows, problems = simulate(prices, base, growth)

    print(f"\n기준: 회당 {base:.0f}G, 업그레이드당 +{growth:.1f}G, 노드 {args.nodes}개")
    print("\n다이브  수입   가격   잔액   다음")
    for k, income, price, wallet, nxt in rows:
        print(f"{k:>5}  {income:>5.0f}  {price:>5}  {wallet:>5.0f}  {nxt:>5}")

    required = sum(prices)
    # 면허가 = 총액 × share 를 만족하는 값. 총액 = 필수 + 면허 이므로
    #   면허 = 필수 × share / (1 - share)
    license_cost = round(required * args.license_share / (1 - args.license_share) / args.step) * args.step
    total = required + license_cost

    print(f"\n필수 노드 합계  {required:>6}G")
    print(f"면허            {license_cost:>6}G   (총액의 {license_cost / total:.0%})")
    print(f"최소 경로       {total:>6}G")

    save_dives = license_cost / (base + growth * args.nodes)
    print(f"  → 면허까지 저축 약 {save_dives:.1f} 다이브, 총 {args.nodes + save_dives:.0f} 다이브")

    if problems:
        print("\n⚠ 규칙 위반")
        for p in problems:
            print("  " + p)

        too_cheap = any("두 개가" in p for p in problems)
        too_dear = any("못 산다" in p for p in problems)
        if too_cheap and not too_dear:
            print(f"  → 가격이 싸다. --margin 을 {args.margin:.2f}보다 올릴 것 (1.0 근처까지)")
        elif too_dear and not too_cheap:
            print(f"  → 가격이 비싸다. --margin 을 {args.margin:.2f}보다 낮출 것")
        else:
            print("  → 앞뒤가 같이 어긋난다. growth(수입 증가분)가 실제와 다를 가능성이 크다")
    else:
        print("\n✓ 매 다이브 정확히 하나씩 사진다")


if __name__ == "__main__":
    main()
