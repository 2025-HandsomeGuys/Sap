#!/usr/bin/env python3
"""텔레메트리 JSONL을 밸런스판 CSV(days.csv / dives.csv)로 굽는다.

설계: Assets/Docs/telemetry/balance-csv-design.md

사용법:
    python export_csv.py <telemetry_dir> -o <out_dir> [--include-fixture]

표준 라이브러리만 쓴다 — 아무 데서나 돌아야 한다.
"""

import argparse
import csv
import json
import sys
from pathlib import Path

# Windows 한국어 콘솔(cp949)은 em dash(—) 등 유니코드 문자를 못 찍고 죽는다.
# stdout/stderr을 UTF-8로 강제 재설정한다 (표준 라이브러리 io.TextIOWrapper.reconfigure).
for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):
        pass

# 공통 필드 — 모든 이벤트가 갖는다
# session_start 는 이벤트가 아니라 **파일명**에서 온다(yyyyMMdd-HHmmss_<세션ID8>.jsonl).
# 로그에 절대 시각이 없어서, 이것이 없으면 회차를 시간순으로 줄 세울 수 없다 —
# run_id 는 GUID라 정렬해도 무작위 순서다. 상수가 시간에 따라 흐르는지
# (check_principles.py 의 drift) 보려면 이 축이 반드시 필요하다.
COMMON = ["run_id", "is_fixture", "anon_id", "session_id", "seq",
          "build_type", "version", "session_start", "playtime", "day", "region", "depth"]

# payload에서 끌어올릴 스칼라 필드 (표별)
DAY_FIELDS = ["ended_day", "gold_start", "gold_end", "mineral_sale", "stock", "coin",
              "shop_purchase", "upgrade", "other", "max_depth",
              "mining_level", "unlocked_nodes", "warehouse_count",
              "stock_value", "stock_cost"]

# stamina_pct(=종료 시 current/max)는 제외했다 — 이 게임은 부상·화상·동상·방사능·파기가
# MaxStamina를 깎고 current가 거기 클램프되므로 그 비율이 늘 1이 되어 정보가 없다.
# 잠수 소모를 재는 값은 max_stamina_pct(= 종료 max / 시작 max)다.
DIVE_FIELDS = ["result", "max_depth", "seconds", "mineral_kinds", "mineral_count",
               "stamina_left", "max_stamina_start", "max_stamina_end", "max_stamina_pct",
               "weight_ratio", "pixels_dug",
               # 모델 캘리브레이션(P0)용. swings 없이는 px/swing·배터리/swing을
               # 실측으로 재확인할 수 없고, real_seconds/time_scale_avg 없이는
               # 배속 플레이가 초당 스윙을 3.4배 과소평가한다(2026-08-21 실측 사고).
               "swings", "swings_hit", "real_seconds", "time_scale_avg",
               # 편의성 축(P13) — 벽타기 속도·스태미나 소모율은 수입을 안 움직여서
               # 런 모델로는 못 잰다. charter §7.
               #   climb_seconds                 -> 벽타기 속도
               #   climb_stamina / climb_seconds -> 스태미나 소모율(초당 드레인)
               "climb_seconds", "climb_stamina"]

# 업그레이드 구매/차단 — upgrades.csv 한 줄.
# `blocked` 컬럼이 두 이벤트를 가른다(1=사고 싶었으나 못 삼).
# short_by/gold/reason 은 upgrade_blocked 에만 있어 구매 행에서는 빈 칸이다.
UPGRADE_FIELDS = ["blocked", "node", "cost", "level", "gold", "short_by", "reason"]

# 실패(사망·긴급탈출) — failures.csv 한 줄. `kind` 가 둘을 가른다.
#
# ⚠ **사망한 잠수는 dives.csv 에 아예 없다.** GameOverHandler 가 SettlementManager 의
#   StopTracking/AbortTracking 을 부르지 않아 dive_end 가 발화하지 않는다
#   (실측: dive_start 212 vs dive_end 113). 그래서 "그날 실패했나"를 dives.csv 의
#   result 로 판정하면 사망이 통째로 안 보인다 — 이 표가 그 구멍을 메운다.
#   stamina_depleted 는 MaxStamina<=0, 즉 **사망의 유일한 조건**이다(PlayerStat.cs:405).
FAILURE_FIELDS = ["kind", "weight", "encumbered", "injury", "burn", "frostbite",
                  "radiation", "lost_count", "kept_count", "lost_kinds", "kept_kinds"]

# 중첩 오브젝트 → 컬럼 접두어
DIVE_NESTED = {"region_seconds": "sec_", "minerals": "min_", "stamina_loss": "sloss_",
               "swings_by_tool": "swing_"}


def read_events(directory):
    """디렉토리의 모든 *.jsonl을 읽어 이벤트 dict를 yield한다."""
    files = sorted(Path(directory).glob("*.jsonl"))
    if not files:
        print(f"경고: {directory} 에 *.jsonl 파일이 없다", file=sys.stderr)

    broken = 0
    for path in files:
        # "20260830-145246_a1b2c3d4.jsonl" -> "20260830-145246"
        stamp = path.stem.split("_", 1)[0]
        with path.open("r", encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if not line:
                    continue
                try:
                    event = json.loads(line)
                except json.JSONDecodeError:
                    # 크래시로 잘린 마지막 줄이 흔하다 — 세고 넘어간다
                    broken += 1
                    continue
                event.setdefault("session_start", stamp)
                yield event

    if broken:
        print(f"경고: 파싱 실패한 줄 {broken}개를 건너뛰었다", file=sys.stderr)


def flatten(event, scalar_fields, nested_map):
    """이벤트 하나를 평평한 dict로. 중첩 오브젝트는 접두어 붙여 펼친다."""
    row = {k: event.get(k, "") for k in COMMON}
    payload = event.get("payload") or {}

    for k in scalar_fields:
        row[k] = payload.get(k, "")

    for key, prefix in (nested_map or {}).items():
        obj = payload.get(key) or {}
        if isinstance(obj, dict):
            for sub, val in obj.items():
                row[prefix + sub] = val

    return row


def write_csv(path, rows, scalar_fields, nested_map):
    """행을 CSV로 쓴다. 컬럼 집합은 전체 행을 훑어 결정한다(2-pass)."""
    if not rows:
        print(f"건너뜀: {path.name} — 해당 이벤트가 없다", file=sys.stderr)
        return

    nested_cols = set()
    for row in rows:
        for k in row:
            if any(k.startswith(p) for p in (nested_map or {}).values()):
                nested_cols.add(k)

    header = COMMON + scalar_fields + sorted(nested_cols)

    rows.sort(key=lambda r: (str(r.get("run_id", "")), _as_int(r.get("day"))))

    with path.open("w", encoding="utf-8", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=header, extrasaction="ignore")
        writer.writeheader()
        for row in rows:
            writer.writerow({k: row.get(k, "") for k in header})

    print(f"{path} — {len(rows)}행, {len(header)}컬럼")


def _as_int(value):
    try:
        return int(value)
    except (TypeError, ValueError):
        return 0


def main():
    parser = argparse.ArgumentParser(description="텔레메트리 JSONL → 밸런스 CSV")
    parser.add_argument("telemetry_dir", help="JSONL이 있는 디렉토리")
    parser.add_argument("-o", "--out", default=".", help="CSV 출력 디렉토리 (기본: 현재 디렉토리)")
    parser.add_argument("--include-fixture", action="store_true",
                        help="QA 픽스처·디버그 조작 회차도 포함한다 (기본: 제외)")
    args = parser.parse_args()

    out_dir = Path(args.out)
    out_dir.mkdir(parents=True, exist_ok=True)

    days, dives, upgrades, failures, skipped = [], [], [], [], 0

    for event in read_events(args.telemetry_dir):
        if not args.include_fixture and event.get("is_fixture") is True:
            skipped += 1
            continue

        name = event.get("event")
        if name == "day_settled":
            days.append(flatten(event, DAY_FIELDS, None))
        elif name == "dive_end":
            dives.append(flatten(event, DIVE_FIELDS, DIVE_NESTED))
        elif name in ("stamina_depleted", "emergency_escape"):
            row = flatten(event, FAILURE_FIELDS, None)
            row["kind"] = "death" if name == "stamina_depleted" else "escape"
            failures.append(row)
        elif name in ("upgrade_purchased", "upgrade_blocked"):
            row = flatten(event, UPGRADE_FIELDS, None)
            row["blocked"] = 1 if name == "upgrade_blocked" else 0
            upgrades.append(row)

    if skipped:
        print(f"제외: 픽스처 회차 이벤트 {skipped}건 (--include-fixture로 포함 가능)", file=sys.stderr)

    write_csv(out_dir / "days.csv", days, DAY_FIELDS, None)
    write_csv(out_dir / "dives.csv", dives, DIVE_FIELDS, DIVE_NESTED)
    write_csv(out_dir / "upgrades.csv", upgrades, UPGRADE_FIELDS, None)
    write_csv(out_dir / "failures.csv", failures, FAILURE_FIELDS, None)


if __name__ == "__main__":
    main()
