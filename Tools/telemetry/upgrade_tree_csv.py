#!/usr/bin/env python3
"""UpgradeTree.csv를 읽는다. 파이썬 밸런스 도구의 공통 입구다.

이전에는 check_upgrade_tree.py가 UpgradeTreeGenerator.cs를 정규식으로 파싱했다.
2026-08-21에 원본이 CSV로 내려왔으므로 그 파서는 필요 없다.

설계: Assets/Docs/superpowers/specs/2026-08-21-upgrade-tree-autogen-design.md §5.2
표준 라이브러리만 쓴다 — export_csv.py / fit_upgrade_prices.py와 같은 방침.
"""

import csv
from pathlib import Path

DEFAULT_PATH = "Assets/GameData/UpgradeData/UpgradeTree.csv"


def load(path=DEFAULT_PATH):
    """CSV를 읽어 dict 목록으로 돌려준다. 타입 변환은 여기서 한 번만 한다."""
    p = Path(path)
    if not p.exists():
        raise FileNotFoundError("UpgradeTree.csv가 없다: %s" % path)

    rows = []
    with open(p, encoding="utf-8", newline="") as f:
        for r in csv.DictReader(f):
            nid = (r.get("nodeId") or "").strip()
            if not nid:
                continue
            rows.append({
                "nodeId":         nid,
                "tier":           int(r["tier"]),
                "cost":           int(r["cost"]),
                "effectType":     (r.get("effectType") or "").strip(),
                "effectValue":    float(r["effectValue"]),
                "isPercentage":   _bool(r.get("isPercentage")),
                "parentIds":      _parents(r.get("parentIds")),
                "uiX":            float(r["uiX"]),
                "uiY":            float(r["uiY"]),
                "displayNameKey": r.get("displayNameKey") or "",
                "descriptionKey": r.get("descriptionKey") or "",
                "locked":         _bool(r.get("locked")),
                # MineralPriceUp 전용 대상 광물. 그 외에는 빈 문자열.
                "targetMineral":  (r.get("targetMineral") or "").strip(),
            })
    return rows


def _bool(s):
    return (s or "").strip().lower() == "true"


def _parents(s):
    # 세미콜론 구분 — 쉼표는 CSV 구분자라 못 쓴다 (C# 리더와 같은 규약)
    return [p.strip() for p in (s or "").split(";") if p.strip()]
