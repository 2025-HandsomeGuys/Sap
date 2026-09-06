# -*- coding: utf-8 -*-
"""UpgradeTree.csv의 가격을 reprice_template.py의 PRICE_TABLE에 다시 써넣는다.

배경: 2026-08-26부터 트리의 정본은 **Tools/upgrade_tree_editor.html**이다.
배치·연결·가격·노드를 거기서 편집하고 CSV로 저장한다.

그런데 reprice_template.py의 밸런스 리포트(그 시점 수입 대비 가격, 한계효용)는
자기 안의 PRICE_TABLE을 본다. 편집기로 가격을 바꾸고 나면 그 표가 CSV와 어긋나서
리포트가 낡은 값으로 거짓말을 한다. 리포트를 보기 전에 이걸 한 번 돌리면 맞는다.

    python Tools/telemetry/pull_prices.py            # 무엇이 바뀌는지만 본다
    python Tools/telemetry/pull_prices.py --write    # PRICE_TABLE을 갱신한다

⚠ 이 스크립트는 가격만 옮긴다. 트리 구조(TEMPLATE의 노드 목록·라인)는 손대지 않는다.
  편집기에서 노드를 **추가**했다면 TEMPLATE에도 그 노드를 넣어야 리포트에 나온다
  (안 넣으면 여기서 "TEMPLATE에 없다"로 알려준다).
"""

import argparse
import csv
import io
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CSV_PATH = ROOT / "Assets/GameData/UpgradeData/UpgradeTree.csv"
PY_PATH = ROOT / "Tools/telemetry/reprice_template.py"

TABLE_START = "PRICE_TABLE = {"
TABLE_END = "}"


def load_csv():
    with io.open(CSV_PATH, encoding="utf-8", newline="") as f:
        return list(csv.DictReader(f))


def read_table(text):
    """현재 PRICE_TABLE의 {id: 가격}과 블록의 문자 범위를 돌려준다."""
    i = text.index(TABLE_START)
    j = text.index("\n" + TABLE_END + "\n", i) + len(TABLE_END) + 2
    body = text[i:j]
    table = {m.group(1): int(m.group(2))
             for m in re.finditer(r'"([^"]+)":\s*(\d+)', body)}
    return table, i, j


def build_table(rows):
    """CSV 순서(=구매 순서)를 지키고 지층마다 구분선을 넣어 다시 쓴다."""
    lines = [TABLE_START]
    tier_seen = None
    width = max((len('"%s":' % r["nodeId"]) for r in rows), default=8)
    for r in rows:
        tier = int(r["tier"])
        if tier != tier_seen:
            lines.append("    # ── T%d ──" % tier)
            tier_seen = tier
        key = '"%s":' % r["nodeId"]
        lines.append("    %-*s %s,%s" % (
            width, key, r["cost"],
            ("  # " + r["displayNameKey"]) if r["displayNameKey"] else ""))
    lines.append(TABLE_END)
    return "\n".join(lines)


def main():
    ap = argparse.ArgumentParser(description="CSV 가격을 PRICE_TABLE로 되끌어온다")
    ap.add_argument("--write", action="store_true", help="reprice_template.py를 실제로 고친다")
    a = ap.parse_args()

    rows = load_csv()
    text = io.open(PY_PATH, encoding="utf-8").read()
    old, i, j = read_table(text)
    new = {r["nodeId"]: int(r["cost"]) for r in rows}

    changed = [(k, old[k], new[k]) for k in new if k in old and old[k] != new[k]]
    added = [k for k in new if k not in old]
    dropped = [k for k in old if k not in new]

    if not (changed or added or dropped):
        print("PRICE_TABLE이 이미 CSV와 같다 — 할 일 없음")
        return 0

    for k, o, n in changed:
        print("  %-34s %6d -> %6d" % (k, o, n))
    for k in added:
        print("  %-34s   (신규) %d — TEMPLATE에도 넣어야 리포트에 나온다" % (k, new[k]))
    for k in dropped:
        print("  %-34s   (CSV에서 사라짐) — TEMPLATE에서도 빼야 한다" % k)
    print("\n바뀜 %d · 신규 %d · 사라짐 %d" % (len(changed), len(added), len(dropped)))

    if not a.write:
        print("(--write 를 붙이면 실제로 고친다)")
        return 0

    io.open(PY_PATH, "w", encoding="utf-8").write(text[:i] + build_table(rows) + text[j:])
    print("\n갱신: %s" % PY_PATH.relative_to(ROOT))
    return 0


if __name__ == "__main__":
    sys.exit(main())
