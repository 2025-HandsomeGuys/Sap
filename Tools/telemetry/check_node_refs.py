#!/usr/bin/env python3
"""트리 바깥에서 노드 id를 직접 참조하는 곳을 찾는다.

왜 있나
-------
업그레이드 노드는 스탯만 올리는 게 아니다. 일부는 **통행권**이라 다른 시스템이
id 문자열로 붙잡고 있다.

    toolConfig.json  -> "PickaxeUnlock_T0_01"  (곡괭이를 쓸 수 있게 됨)
    MapUnlockGate.cs -> "Facility_Map_T0"      (지도가 열림)
    RelicSO          -> "RelicUnlock_..."      (유물을 받음)

합성기(synth_tree)는 한계효용으로 스탯 재료만 고르므로 이런 노드를 만들지 않는다.
그 상태로 트리를 갈아엎으면 **곡괭이·드릴·지도·시설이 통째로 잠긴다.**
에러도 안 난다 — 그냥 영원히 안 열린다.

그래서 트리를 새로 쓰기 전에 반드시 이걸 돌려, 참조되는 id를 보존 목록에 넣는다.

    python Tools/telemetry/check_node_refs.py
    python Tools/telemetry/check_node_refs.py --against <새 CSV>   # 새 트리에 빠진 것 확인

표준 라이브러리만 쓴다.
"""

import argparse
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
import upgrade_tree_csv as UTC

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):
        pass

# 트리 자신은 참조처가 아니다
SKIP_PARTS = (
    "GameData/UpgradeData/",
    "UpgradeTree.csv",
    "Tools/telemetry/",
)

# priceData.json은 우리가 CSV에서 다시 굽는 생성물이고, 테스트는 트리와 함께 고친다.
# 둘 다 "끊기면 기능이 죽는" 참조가 아니므로 위험도를 나눠 본다.
GENERATED = ("StreamingAssets/priceData.json",)
TESTS = ("Assets/Tests/",)
FIXTURES = ("Assets/QA/",)


def kind(path):
    if any(g in path for g in GENERATED):
        return "생성물"
    if any(x in path for x in TESTS):
        return "테스트"
    if any(x in path for x in FIXTURES):
        return "QA픽스처"
    return "게임"


SEARCH_ROOTS = ["Assets"]
SEARCH_EXT = (".cs", ".json", ".asset", ".prefab", ".unity", ".csv")


def skip(path):
    p = str(path).replace("\\", "/")
    return any(s in p for s in SKIP_PARTS)


def find_refs(node_ids, roots=None):
    """{node_id: [참조 파일...]}"""
    roots = roots or SEARCH_ROOTS
    # 한 번만 훑고 모든 id를 동시에 찾는다 — 파일이 많아 id마다 훑으면 느리다
    pat = re.compile("|".join(re.escape(n) for n in sorted(node_ids, key=len, reverse=True)))
    refs = {}
    for root in roots:
        for p in Path(root).rglob("*"):
            if not p.is_file() or p.suffix not in SEARCH_EXT or skip(p):
                continue
            try:
                text = p.read_text(encoding="utf-8", errors="replace")
            except OSError:
                continue
            for m in set(pat.findall(text)):
                refs.setdefault(m, []).append(str(p).replace("\\", "/"))
    return refs


def main():
    ap = argparse.ArgumentParser(description="노드 id 외부 참조 조사")
    ap.add_argument("--csv", default=UTC.DEFAULT_PATH, help="현재 트리")
    ap.add_argument("--against", help="새로 만든 트리 CSV — 여기 빠진 참조를 경고한다")
    a = ap.parse_args()

    cur = UTC.load(a.csv)
    ids = {r["nodeId"] for r in cur}
    refs = find_refs(ids)

    print("현재 트리 %d개 중 **외부에서 id로 참조되는** 노드 %d개\n"
          % (len(ids), len(refs)))
    critical = {}
    for nid, files in refs.items():
        hard = [f for f in files if kind(f) in ("게임", "QA픽스처")]
        if hard:
            critical[nid] = hard

    print("  이 중 게임 코드·설정이 붙잡고 있는 것 %d개"
          " — 사라지면 에러 없이 기능이 잠긴다\n" % len(critical))
    for nid in sorted(critical):
        for f in sorted(set(critical[nid])):
            print("    %-34s [%s] %s" % (nid, kind(f), f))
    if not critical:
        print("    (없음)")
    print("\n  나머지 %d개는 priceData.json(생성물)·테스트에서만 참조 — 함께 갱신하면 된다"
          % (len(refs) - len(critical)))

    if a.against:
        new_ids = {r["nodeId"] for r in UTC.load(a.against)}
        missing = sorted(set(critical) - new_ids)
        print("\n새 트리(%s)에 빠진 참조 노드: %d개" % (a.against, len(missing)))
        for nid in missing:
            print("  [끊김] %-32s %s" % (nid, ", ".join(sorted(set(refs[nid])))))
        if missing:
            print("\n이대로 덮어쓰면 위 기능이 조용히 잠긴다. 에러는 안 난다.")
            return 1
        print("  (없음 — 안전하다)")

    return 0


if __name__ == "__main__":
    sys.exit(main())
