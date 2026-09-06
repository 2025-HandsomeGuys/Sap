#!/usr/bin/env python3
"""UpgradeEffectType이 실제로 게임에 배선돼 있는지 전수 조사한다.

왜 있나
-------
2026-08-11 조사에서 27종 중 12종이 "골드만 받고 아무 일도 하지 않는" 상태로
드러났다(mineral-price-design.md §10.1). 그 목록은 손으로 센 것이라
효과가 추가·배선될 때마다 낡는다.

트리 합성기가 죽은 효과를 재료로 쓰면 **아무 일도 안 하는 노드로 트리를 채운다.**
그래서 합성 전에 매번 이걸 돌려 재료 목록을 거른다.

판정 방법
---------
소비 경로가 **두 가지**라 둘 다 본다. 하나만 보면 멀쩡한 효과를 죽었다고 한다.
  (1) StatType 경유 — UpgradeStatProvider가 StatType으로 옮기고 게임이 그걸 읽는다
  (2) 직접 — UpgradeManager.GetStatValue(UpgradeEffectType.X, ...)를 그대로 부른다
      NapCount / MapExploreRadiusUp / MineralExtraDropChance가 이 경로다.

배선 파일(스탯을 정의·중계만 하는 곳)은 소비처로 치지 않는다 —
거기 이름이 있는 것과 그 값이 게임에 영향을 주는 것은 다르다.
그게 정확히 12종이 조용히 죽어 있던 이유다.

한계: grep 기반이라 "읽기는 하는데 결과를 안 쓰는" 코드는 못 걸러낸다.
살아있다고 나온 것도 의심스러우면 사람이 확인할 것. 죽었다고 나온 것은 확실하다.

표준 라이브러리만 쓴다.
"""

import argparse
import re
import sys
from pathlib import Path

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):
        pass

SCRIPTS = "Assets/Scripts"
MAP_FILE = "Assets/Scripts/UI/Player/Stats/Providers/UpgradeStatProvider.cs"
ENUM_FILE = "Assets/Scripts/UI/Upgrade/UpgradeEffectSO.cs"

# 스탯을 정의·중계·표시만 하는 파일. 여기 이름이 있어도 "쓰이는" 것이 아니다.
PLUMBING = (
    "Stats/StatType.cs",
    "Stats/PlayerStat.cs",
    "Stats/StatBaseValueTable.cs",
    "Stats/StatModifier.cs",
    "Stats/IStatProvider.cs",
    "Stats/StatDiagnosticPanel.cs",
    "Stats/Providers/",
    "Upgrade/UpgradeEffectSO.cs",
    "Upgrade/UpgradeManager.cs",
    "Upgrade/UpgradeNodeSO.cs",
    "Upgrade/UpgradeTreeSO.cs",
    "Upgrade/EquipmentUpgradeOverlayUI.cs",
    "UI/Core/CodeStatIcons.cs",
    "Editor/",
    "Tests/",
)


def is_plumbing(path):
    p = str(path).replace("\\", "/")
    return any(mark in p for mark in PLUMBING)


def load_enum(path=ENUM_FILE):
    text = Path(path).read_text(encoding="utf-8", errors="replace")
    body = text.split("enum UpgradeEffectType", 1)[-1]
    body = body.split("}", 1)[0]
    names = []
    for line in body.splitlines():
        m = re.match(r"\s*(\w+)\s*=\s*\d+", line)
        if m and m.group(1) != "None":
            names.append(m.group(1))
    return names


def load_map(path=MAP_FILE):
    """{ UpgradeEffectType.X, StatType.Y } 매핑을 읽는다."""
    text = Path(path).read_text(encoding="utf-8", errors="replace")
    pairs = re.findall(
        r"\{\s*UpgradeEffectType\.(\w+)\s*,\s*StatType\.(\w+)\s*\}", text)
    return dict(pairs)


def consumers(stat_name, root=SCRIPTS, effect_name=None):
    """이 효과를 배선 파일 바깥에서 읽는 파일 목록.

    StatType 경유와 UpgradeEffectType 직접 호출을 모두 센다 — 한쪽만 보면
    멀쩡히 도는 효과를 죽었다고 보고한다(NapCount가 그랬다).
    """
    alts = [r"StatType\.%s\b" % re.escape(stat_name)]
    if effect_name:
        alts.append(r"UpgradeEffectType\.%s\b" % re.escape(effect_name))
    pat = re.compile("|".join(alts))
    hits = []
    for p in Path(root).rglob("*.cs"):
        if is_plumbing(p):
            continue
        try:
            if pat.search(p.read_text(encoding="utf-8", errors="replace")):
                hits.append(str(p).replace("\\", "/"))
        except OSError:
            pass
    return hits


def main():
    ap = argparse.ArgumentParser(description="업그레이드 효과 배선 전수 조사")
    ap.add_argument("--root", default=SCRIPTS)
    ap.add_argument("--verbose", action="store_true", help="소비처 파일까지 출력")
    a = ap.parse_args()

    names = load_enum()
    mapping = load_map()

    live, dead, unmapped = [], [], []
    for name in names:
        stat = mapping.get(name)
        if stat is None:
            # StatType 매핑이 없어도 직접 호출로 살아 있을 수 있다
            direct = consumers("__none__", a.root, name)
            if direct:
                live.append((name, "(직접)", direct))
            else:
                unmapped.append(name)
            continue
        hits = consumers(stat, a.root, name)
        (live if hits else dead).append((name, stat, hits))

    print("UpgradeEffectType %d종 — 살아있음 %d / 죽음 %d / 매핑없음 %d\n"
          % (len(names), len(live), len(dead), len(unmapped)))

    print("[죽음] 골드만 받고 아무 일도 하지 않는다 — 합성기 재료에서 빼야 한다")
    for name, stat, _ in dead:
        print("  %-26s -> StatType.%s" % (name, stat))
    if not dead:
        print("  (없음)")

    if unmapped:
        print("\n[매핑없음] UpgradeStatProvider에 StatType 연결이 없다")
        for name in unmapped:
            print("  %s" % name)

    print("\n[살아있음]")
    for name, stat, hits in live:
        if a.verbose:
            label = stat if stat.startswith("(") else "StatType." + stat
            print("  %-26s -> %-30s %s" % (name, label, hits[0]))
            for h in hits[1:]:
                print("  %-26s    %-24s %s" % ("", "", h))
        else:
            label = stat if stat.startswith("(") else "StatType." + stat
            print("  %-26s -> %-30s (%d곳)" % (name, label, len(hits)))

    return 0


if __name__ == "__main__":
    sys.exit(main())
