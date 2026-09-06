#!/usr/bin/env python3
"""노드 에셋(.asset)에서 트리를 CSV로 복원한다.

왜 있나
-------
2026-08-22: 합성 트리가 UpgradeTree.csv를 덮었지만 에셋은 아직 이전 트리
(48노드) 상태다 — 생성기를 안 돌렸기 때문이다. 사람이 손으로 다듬었던
그 트리의 초반 구성을 참고 자료로 쓰기 위해 에셋에서 역으로 CSV를 만든다.

    python Tools/telemetry/recover_tree_from_assets.py
    -> Assets/GameData/UpgradeData/UpgradeTree.reference.csv

.reference.csv는 생성기가 읽지 않는다(생성기는 UpgradeTree.csv만 본다).
표준 라이브러리만 쓴다.
"""

import csv
import re
import sys
from pathlib import Path

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):
        pass

NODE_DIR = Path("Assets/GameData/UpgradeData/Node")
EFF_DIR = Path("Assets/GameData/UpgradeData/Effect")
OUT = "Assets/GameData/UpgradeData/UpgradeTree.reference.csv"

# UpgradeEffectType 숫자 -> 이름 (UpgradeEffectSO.cs에서 읽는다)
def load_effect_enum():
    text = Path("Assets/Scripts/UI/Upgrade/UpgradeEffectSO.cs").read_text(
        encoding="utf-8", errors="replace")
    body = text.split("enum UpgradeEffectType", 1)[1].split("}", 1)[0]
    return {int(m.group(2)): m.group(1)
            for m in re.finditer(r"(\w+)\s*=\s*(\d+)", body)}


def field(text, key):
    m = re.search(r"^  %s: (.*)$" % re.escape(key), text, re.M)
    return m.group(1).strip() if m else None


def guid_of(meta_path):
    m = re.search(r"^guid: ([0-9a-f]{32})", meta_path.read_text(
        encoding="utf-8", errors="replace"), re.M)
    return m.group(1) if m else None


def main():
    enum_names = load_effect_enum()

    # guid -> nodeId / guid -> 효과 정보
    node_by_guid, eff_by_guid = {}, {}
    nodes = {}

    for asset in NODE_DIR.glob("*.asset"):
        text = asset.read_text(encoding="utf-8", errors="replace")
        nid = field(text, "nodeId")
        if not nid:
            continue
        g = guid_of(asset.with_suffix(".asset.meta"))
        if g:
            node_by_guid[g] = nid
        # parentNodes 블록의 guid들
        pm = re.search(r"^  parentNodes:\n((?:  - .*\n)*)", text, re.M)
        parents = re.findall(r"guid: ([0-9a-f]{32})", pm.group(1)) if pm else []
        em = re.search(r"^  effect: \{fileID: \d+, guid: ([0-9a-f]{32})", text, re.M)
        ui = re.search(r"^  uiPosition: \{x: ([-\d.]+), y: ([-\d.]+)\}", text, re.M)
        nodes[nid] = {
            "tier": int(field(text, "tier") or 0),
            "cost": int(field(text, "cost") or 0),
            "name": (field(text, "displayNameKey") or "").strip('"'),
            "desc": (field(text, "descriptionKey") or "").strip('"'),
            "parents_guid": parents,
            "effect_guid": em.group(1) if em else None,
            "uiX": float(ui.group(1)) if ui else 0.0,
            "uiY": float(ui.group(2)) if ui else 0.0,
        }

    for asset in EFF_DIR.glob("*.asset"):
        text = asset.read_text(encoding="utf-8", errors="replace")
        g = guid_of(asset.with_suffix(".asset.meta"))
        if not g:
            continue
        eff_by_guid[g] = {
            "type": enum_names.get(int(field(text, "type") or 0), "None"),
            "value": float(field(text, "value") or 0),
            "pct": (field(text, "isPercentage") or "0") == "1",
        }

    # 유니코드 이스케이프(\uXXXX) 복원
    def decode(s):
        try:
            return s.encode("latin-1", "backslashreplace").decode("unicode_escape") \
                if "\\u" in s else s
        except Exception:
            return s

    rows = []
    for nid, n in sorted(nodes.items(), key=lambda kv: (kv[1]["tier"], kv[1]["cost"])):
        eff = eff_by_guid.get(n["effect_guid"], {"type": "None", "value": 0, "pct": False})
        rows.append({
            "nodeId": nid,
            "tier": n["tier"],
            "effectType": eff["type"],
            "effectValue": ("%g" % eff["value"]),
            "isPercentage": "true" if eff["pct"] else "false",
            "parentIds": ";".join(node_by_guid.get(g, "?") for g in n["parents_guid"]),
            "cost": n["cost"],
            "uiX": ("%g" % n["uiX"]),
            "uiY": ("%g" % n["uiY"]),
            "displayNameKey": decode(n["name"]),
            "descriptionKey": decode(n["desc"]),
            "locked": "false",
        })

    fields = ["nodeId", "tier", "effectType", "effectValue", "isPercentage",
              "parentIds", "cost", "uiX", "uiY", "displayNameKey",
              "descriptionKey", "locked"]
    with open(OUT, "w", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, fieldnames=fields, quoting=csv.QUOTE_MINIMAL)
        w.writeheader()
        for r in rows:
            w.writerow(r)
    print("복원: %s (%d행)" % (OUT, len(rows)))
    bad = [r["nodeId"] for r in rows if "?" in r["parentIds"]]
    if bad:
        print("경고 — 선행을 못 푼 노드: %s" % ", ".join(bad))
    return 0


if __name__ == "__main__":
    sys.exit(main())
