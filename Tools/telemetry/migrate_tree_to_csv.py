#!/usr/bin/env python3
"""UpgradeTreeGenerator.cs의 C# 리터럴을 UpgradeTree.csv로 옮긴다 (일회성).

왜 있나
-------
트리 원본을 C# 코드에서 데이터로 내리는 마이그레이션이다. 손으로 옮기면
50행 x 11필드 = 550칸을 눈으로 검사해야 하고, 한 칸만 틀려도 조용히 넘어간다.

두 가지 모드
------------
    python Tools/telemetry/migrate_tree_to_csv.py            # CSV 생성
    python Tools/telemetry/migrate_tree_to_csv.py --verify   # C#과 CSV 대조

--verify가 "불일치 0건"을 찍어야 마이그레이션이 끝난 것이다.

주의: --src의 C# 리터럴은 마이그레이션이 끝나면 삭제된다(Task 4).
그 뒤로는 --verify를 쓸 수 없다. 그게 정상이다 — 원본이 하나가 됐다는 뜻이다.

설계: Assets/Docs/superpowers/specs/2026-08-21-upgrade-tree-autogen-design.md §4.3
표준 라이브러리만 쓴다.
"""

import argparse
import csv
import re
import sys
from pathlib import Path

# Windows 한국어 콘솔(cp949)은 일부 문자를 못 찍고 죽는다. 다른 도구와 동일 처리.
for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):
        pass

SRC = "Assets/Scripts/Editor/UpgradeTreeGenerator.cs"
OUT = "Assets/GameData/UpgradeData/UpgradeTree.csv"

FIELDS = ["nodeId", "tier", "effectType", "effectValue", "isPercentage",
          "parentIds", "cost", "uiX", "uiY", "displayNameKey",
          "descriptionKey", "locked"]

NUMERIC = ("tier", "cost", "effectValue", "uiX", "uiY")

# list.Add(new UpgradeNodeData(
#     "id", "이름", "설명", tier, cost, new Vector2(x, y),
#     new string[] { "부모" }, UpgradeEffectType.X, 값, bool
# ));
NODE_RE = re.compile(
    r'new\s+UpgradeNodeData\(\s*'
    r'"(?P<id>[^"]*)"\s*,\s*'
    r'"(?P<name>[^"]*)"\s*,\s*'
    r'"(?P<desc>[^"]*)"\s*,\s*'
    r'(?P<tier>-?\d+)\s*,\s*'
    r'(?P<cost>-?\d+)\s*,\s*'
    r'new\s+Vector2\(\s*(?P<x>-?[\d.]+)f?\s*,\s*(?P<y>-?[\d.]+)f?\s*\)\s*,\s*'
    r'new\s+string\[\]\s*\{(?P<parents>[^}]*)\}\s*,\s*'
    r'UpgradeEffectType\.(?P<effect>\w+)\s*,\s*'
    r'(?P<value>-?[\d.]+)f?\s*,\s*'
    r'(?P<pct>true|false)',
    re.S,
)

PARENT_RE = re.compile(r'"([^"]*)"')


def parse_cs(path):
    text = Path(path).read_text(encoding="utf-8")
    rows = []
    for m in NODE_RE.finditer(text):
        # 주석 처리된 줄은 건너뛴다
        line_start = text.rfind("\n", 0, m.start()) + 1
        if text[line_start:m.start()].lstrip().startswith("//"):
            continue
        parents = [p for p in PARENT_RE.findall(m.group("parents")) if p.strip()]
        rows.append({
            "nodeId":         m.group("id"),
            "displayNameKey": m.group("name"),
            "descriptionKey": m.group("desc"),
            "tier":           m.group("tier"),
            "cost":           m.group("cost"),
            "uiX":            trim_num(m.group("x")),
            "uiY":            trim_num(m.group("y")),
            "parentIds":      ";".join(parents),
            "effectType":     m.group("effect"),
            "effectValue":    trim_num(m.group("value")),
            "isPercentage":   m.group("pct"),
            "locked":         "false",
        })
    return rows


def trim_num(s):
    """'0.0' -> '0', '-350.0' -> '-350'. 소수점이 의미 있는 값은 그대로 둔다."""
    if "." in s:
        s = s.rstrip("0").rstrip(".")
    return s or "0"


def parse_csv(path):
    with open(path, encoding="utf-8", newline="") as f:
        return [dict(r) for r in csv.DictReader(f)]


def write_csv(rows, path):
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    # newline="" + QUOTE_MINIMAL: 쉼표가 든 설명만 인용된다 (RFC 4180)
    with open(path, "w", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, fieldnames=FIELDS, quoting=csv.QUOTE_MINIMAL)
        w.writeheader()
        for r in rows:
            w.writerow({k: r[k] for k in FIELDS})


def norm(key, value):
    """숫자 칸은 값으로 비교한다 — '60'과 '60.0'은 같다."""
    if key in NUMERIC:
        try:
            return float(value)
        except (TypeError, ValueError):
            return value
    return value


def verify(cs_rows, csv_rows):
    cs = {r["nodeId"]: r for r in cs_rows}
    cv = {r["nodeId"]: r for r in csv_rows}
    bad = 0

    for nid in sorted(set(cs) | set(cv)):
        if nid not in cs:
            print("  [CSV에만] %s" % nid)
            bad += 1
            continue
        if nid not in cv:
            print("  [C#에만]  %s" % nid)
            bad += 1
            continue
        for k in FIELDS:
            a, b = cs[nid][k], cv[nid][k]
            if norm(k, a) != norm(k, b):
                print("  [불일치] %s.%s: C#=%r CSV=%r" % (nid, k, a, b))
                bad += 1

    print("\nC# %d개 / CSV %d개 / 불일치 %d건" % (len(cs), len(cv), bad))
    return bad == 0


def main():
    ap = argparse.ArgumentParser(description="C# 트리 리터럴 -> UpgradeTree.csv 마이그레이션")
    ap.add_argument("--src", default=SRC)
    ap.add_argument("--out", default=OUT)
    ap.add_argument("--verify", action="store_true", help="생성하지 않고 대조만")
    a = ap.parse_args()

    cs_rows = parse_cs(a.src)
    print("C#에서 노드 %d개를 읽었다." % len(cs_rows))
    if not cs_rows:
        print("한 개도 못 읽었다 — 정규식이 리터럴 형식과 안 맞는다.")
        return 1

    if a.verify:
        if not Path(a.out).exists():
            print("CSV가 없다: %s" % a.out)
            return 1
        return 0 if verify(cs_rows, parse_csv(a.out)) else 1

    write_csv(cs_rows, a.out)
    print("저장: %s" % a.out)
    print("이어서 --verify 로 대조할 것.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
