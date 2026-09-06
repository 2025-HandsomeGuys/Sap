#!/usr/bin/env python3
"""업그레이드 밸런스 원칙 체커 — P0(모델-실측 정합) / P12(판정 가능한 분산).

왜 있나
-------
원칙 검증은 두 갈래로 갈린다.

  티어 A  텔레메트리로 직접 판정 (한 회차 안의 시계열)
  티어 B  런 모델의 반사실로 판정 (지배 노드·함정·다양성)

혼자 플레이하는 동안은 **분포를 재는 원칙이 전부 무효다.** 트리를 만든 사람이
최적 경로를 이미 알고 매번 같은 것을 사기 때문에, 구매 분포는 데이터가 아니라
본인 취향의 재확인이다. 그래서 그 축은 텔레메트리가 아니라 모델이 맡는다.

그러면 티어 B 전체가 모델 위에 서게 되고, **모델이 틀리면 원칙 판정이 통째로
조용히 틀린다.** 이 파일이 먼저 있어야 하는 이유다.

  P0   모델 상수가 실측과 맞는가          — 안 맞으면 티어 B 판정 보류
  P12  차이를 판정할 만큼 분산이 작은가   — 나머지 원칙의 임계치를 여기서 정한다

P12가 먼저 나와야 하는 이유: "업글하면 수입이 +8% 는다" 같은 임계치는 근거 없이
쓰면 안 된다. 잠수 간 변동이 그보다 크면 그 임계치는 노이즈만 재는 것이고,
PASS/FAIL이 동전 던지기가 된다. 표본이 적을수록 임계치는 커져야 한다.

설계: Assets/Docs/economy/upgrade-balance-charter.md

사용법:
    python check_csv.py 를 먼저 돌려 CSV를 굽고,
    python check_principles.py --csv-dir <out_dir>

표준 라이브러리만 쓴다.
"""

import argparse
import collections
import csv
import json
import math
import statistics
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):
        pass

REPO = Path(__file__).resolve().parents[2]
TILE_DATA = REPO / "Assets/StreamingAssets/tileData.json"
PRICE_DATA = REPO / "Assets/StreamingAssets/priceData.json"
PLAYER_SO = REPO / "Assets/GameData/PlayerData/DefalutPlayerSO.asset"

# P0 합격선 — 실측/모델 비가 이 안에 들어와야 티어 B(모델 기반 판정)를 신뢰한다.
# 25%는 관대해 보이지만, 이 모델이 틀렸을 때는 3.4배·6배로 틀렸다(과거 3회).
# 잡아야 할 것은 미세 오차가 아니라 자릿수 사고다.
P0_TOLERANCE = 0.25

# P12 — 두 집단 비교에서 유의미를 주장하려면 필요한 효과 크기 계수.
# alpha=.05 양측 / power=.80 의 표준 근사: n당 (z_a/2 + z_b)^2 = 7.85, 즉 MDE ≈ 2.8·CV·sqrt(2/n).
Z_FACTOR = 2.80

# 노이즈 추정에 최소로 필요한 '같은 상태 2회 이상' 그룹 수.
# 그룹 1~2개에서 뽑은 CV는 그 자체가 노이즈라, 그것으로 임계치를 정하면
# 근거 없는 숫자를 근거 있는 것처럼 포장하게 된다. 그럴 바엔 정하지 않는다.
MIN_VARIANCE_GROUPS = 5

# 드리프트를 '확정'하는 데 필요한 최근 구간 표본 수.
# 이보다 적으면 비율이 커도 '의심'으로만 표시한다 — 표본 5개로 1.35배를 단정하면
# P12에서 세운 원칙(표본이 임계를 정한다)을 스스로 어기는 것이다.
MIN_DRIFT_RECENT = 8

# P9 — 후반 기울기가 전반의 이 배수를 넘어야 '발산'으로 본다.
# 여유가 없으면 18.0 -> 18.2 같은 사실상 평평한 곡선이 FAIL로 찍힌다.
P9_DIVERGE_MARGIN = 1.20


# ==========================================================================
# 로딩
# ==========================================================================

def read_csv(path):
    if not path.exists():
        sys.exit("없음: %s — export_csv.py 를 먼저 돌려라" % path)
    with path.open(encoding="utf-8", newline="") as f:
        return list(csv.DictReader(f))


def num(row, key, default=None):
    """CSV 셀 → float. 빈 칸·파싱 실패는 default(기본 None)."""
    v = (row.get(key) or "").strip()
    if not v:
        return default
    try:
        return float(v)
    except ValueError:
        return default


def mineral_prices():
    """priceData.json의 {광물id: 단가}. 가격은 튜닝 중에도 계속 바뀌므로 매번 읽는다."""
    data = json.loads(PRICE_DATA.read_text(encoding="utf-8"))
    return {m["id"]: float(m["price"]) for m in data["minerals"]}


def dive_gold(row, prices):
    """min_* 컬럼 × 현재 가격표. dive_end는 지하에서 발화해 금액을 못 싣기 때문에
    (balance-csv-design.md §5.1) 구성만 남아 있고, 금액은 여기서 곱한다.

    ⚠ **귀환한 잠수만 수입으로 센다.** `minerals`는 *캔* 광물이지 *가져온* 광물이
    아니다. 긴급탈출은 개수의 60%를 잃고(SaveManager.cs:307), 사망은 전부 잃는다.
    실패한 잠수를 수입에 넣으면 "실패했는데 많이 벌었다"가 되어 곡선이 위로 들린다.

    파기 픽셀·깊이는 실패해도 실제로 판 양이므로 이 필터를 적용하지 않는다 —
    그쪽은 호출부에서 컬럼을 직접 읽는다.
    """
    if (row.get("result") or "return") != "return":
        return None

    total = 0.0
    seen = False
    for key, val in row.items():
        if not key.startswith("min_"):
            continue
        n = num(row, key)
        if n:
            seen = True
            total += n * prices.get(key[4:], 0.0)
    return total if seen else None


# ==========================================================================
# 통계 — 표본이 작으므로 전부 중앙값 기반
# ==========================================================================

def summary(values):
    """중앙값·사분위·변동계수. 표본이 적어 평균·표준편차는 이상치에 끌려간다."""
    vals = sorted(v for v in values if v is not None)
    n = len(vals)
    if n == 0:
        return None
    med = statistics.median(vals)
    if n >= 4:
        q = statistics.quantiles(vals, n=4, method="inclusive")
        lo, hi = q[0], q[2]
    else:
        lo, hi = vals[0], vals[-1]
    # 로버스트 CV: IQR/1.349 를 표준편차 대신 쓴다 (정규분포에서 일치)
    cv = ((hi - lo) / 1.349) / med if med else float("nan")
    return dict(n=n, median=med, q1=lo, q3=hi, cv=cv)


def verdict(ratio, tol=P0_TOLERANCE):
    if ratio is None or math.isnan(ratio):
        return "표본없음"
    return "PASS" if abs(ratio - 1.0) <= tol else "FAIL"


def drift(values):
    """**최근 1/4**의 중앙값 ÷ 그 이전의 중앙값. 1에서 멀면 상수가 아니라 흐르는 값이다.

    ⚠ 이게 왜 필요한가: 풀링한 중앙값은 변화를 지운다. 실제로 PX_PER_DEPTH 는
    전체 85잠수 중앙이 6406이라 모델값 6365와 1.01배로 PASS가 떴는데,
    최근 1/4에서는 10283 으로 **1.6배 튀어 있었다.** 옛 데이터가 새 데이터를
    상쇄해 "정확히 맞는다"처럼 보인 것이다.

    반씩 자르지 않고 최근 1/4을 보는 이유: 변화는 대개 계단으로 온다(플레이 방식이
    바뀌거나 게임이 바뀌거나). 반씩 자르면 그 계단이 옛 구간에 희석돼 안 보인다 —
    실제로 이 데이터에서 반쪽 비교는 1.04배로 나와 아무것도 못 잡았다.

    그리고 모델이 설명해야 하는 것은 **지금 빌드**다. 옛 회차와 맞아 봐야 소용없다.

    상수가 흐른다는 건 그것이 상수가 아니라는 뜻이고, 그때는 값을 고치는 게 아니라
    **무엇에 따라 변하는지**를 모델에 넣거나, 최소한 그것이 상수가 아님을 명시해야 한다.

    반환: (비율, 최근 중앙값, 최근 표본 수). 표본이 모자라면 None.

    최근 표본 수를 같이 돌려주는 이유: 최근 구간이 5개뿐인데 "1.35배 흐른다"고
    단정하면 P12에서 배운 것을 그대로 어기는 셈이다(표본이 임계를 정한다).
    호출부가 이 수로 확정(FAIL)과 의심을 가른다.
    """
    vals = [v for v in values if v is not None]
    if len(vals) < 12:
        return None
    cut = len(vals) * 3 // 4
    past, recent = vals[:cut], vals[cut:]
    if not past or not recent:
        return None
    pm, rm = statistics.median(past), statistics.median(recent)
    return ((rm / pm) if pm else None), rm, len(recent)


# ==========================================================================
# P0 — 모델 상수 실측 재추정
# ==========================================================================

_LAYERS = None


def layers_cache(model):
    """지층 데이터는 한 번만 읽는다 — 잠수마다 JSON을 다시 파싱하면 느리다."""
    global _LAYERS
    if _LAYERS is None:
        _LAYERS = model.load_layers(str(TILE_DATA), str(PRICE_DATA))
    return _LAYERS


def expected_minerals(model, layers, pixels, depth):
    """판 픽셀 `pixels`를 깊이 0~`depth`(월드 유닛)에 고르게 뿌렸을 때의 기대 광물 수.

    `run_model.income()`의 4단계와 같은 적분인데, 모델이 *예측한* 픽셀·깊이가 아니라
    그 잠수가 **실제로** 판 픽셀과 도달한 깊이를 넣는다. 그래야 깊이 예측 오차
    (PX_PER_DEPTH 드리프트)가 이 측정에 섞이지 않는다.

    harvest_area_multiplier는 곱하지 않는다 — 그것이 지금 재려는 값이다.
    """
    if depth <= 0 or pixels <= 0:
        return 0.0
    total = 0.0
    d0 = 0.0
    while d0 < depth - 1e-9:
        step = min(1.0, depth - d0)
        chunk_mid = (d0 + step / 2.0) / model.UNITS_PER_CHUNK
        n = sum(L.mix_at(chunk_mid)[0] for L in layers)
        total += n * pixels * (step / depth)
        d0 += step
    return total * model.DIALS["pickup_rate"]

def check_p0(dives, model):
    """모델이 박아 둔 상수 하나하나를 실측으로 다시 뽑아 비교한다.

    상수를 통째로 묶어 "수입이 맞나"만 보면 안 된다. 두 상수가 반대로 틀려
    수입만 우연히 맞는 상황을 못 가려내고, 그 상태로 업그레이드 한계효용을 재면
    엉뚱한 축이 1등으로 나온다. 그래서 항마다 따로 잰다.
    """
    print("=" * 74)
    print("P0 — 모델-실측 정합")
    print("=" * 74)

    # 배속 경고를 먼저. 이걸 모르면 초당 스윙을 3.4배 과소평가한다(2026-08-21 사고).
    scales = [num(d, "time_scale_avg") for d in dives]
    sped = [s for s in scales if s and s > 1.05]
    if sped:
        print("  ⚠ 배속 플레이 %d/%d 잠수 (중앙 %.1f배). 게임시간 seconds 가 아니라"
              % (len(sped), len([s for s in scales if s]), statistics.median(sped)))
        print("    real_seconds 로만 속도를 잰다.")

    rows = []

    # ── PX_PER_DEPTH ──────────────────────────────────────────────────────
    # ⚠ 엘리베이터로 중간 깊이에서 시작하면 max_depth 가 이번에 판 깊이보다 크다.
    #   시작 깊이는 로그에 없다(dive_start payload에 없음). 그 경우 이 값은
    #   과소 추정된다 — 아래 주의 문구가 그것을 말한다.
    px_per_depth = []
    for d in dives:
        px, dep = num(d, "pixels_dug"), num(d, "max_depth")
        if px and dep and dep > 1.0:
            px_per_depth.append(px / abs(dep))
    rows.append(("PX_PER_DEPTH", model.PX_PER_DEPTH, summary(px_per_depth),
                 "픽셀 ÷ 최대깊이", px_per_depth))

    # ── dig_px_per_swing ──────────────────────────────────────────────────
    px_per_swing = []
    for d in dives:
        px, sw = num(d, "pixels_dug"), num(d, "swings")
        if px and sw and sw > 0:
            px_per_swing.append(px / sw)
    rows.append(("dig_px_per_swing_at_base_range",
                 model.DIALS["dig_px_per_swing_at_base_range"],
                 summary(px_per_swing), "픽셀 ÷ 스윙", px_per_swing))

    # ── MAX_STAMINA_PER_SHOVEL_SWING (배터리 소모) ────────────────────────
    # 삽 스윙만으로 나눈다. 드릴은 스태미나가 아니라 배터리를 쓰므로 섞으면 희석된다.
    batt = []
    for d in dives:
        s0, s1 = num(d, "max_stamina_start"), num(d, "max_stamina_end")
        shovel = num(d, "swing_shovel") or num(d, "swings")
        if s0 is not None and s1 is not None and shovel and shovel > 0 and s0 > s1:
            batt.append((s0 - s1) / shovel)
    rows.append(("MAX_STAMINA_PER_SHOVEL_SWING", model.MAX_STAMINA_PER_SHOVEL_SWING,
                 summary(batt), "(시작max − 종료max) ÷ 삽스윙", batt))

    # ── swings_per_sec_base (1배속 환산) ──────────────────────────────────
    # ⚠ real_seconds 는 하강·복귀를 포함한 잠수 전체다. 모델의 이 상수는
    #   **파기 구간의** 속도(삽 풀차징 1초가 천장)라 그대로 나누면 이동 시간만큼
    #   구조적으로 낮게 나온다. travel_fraction 을 빼고 같은 축에서 비교한다.
    dig_frac = 1.0 - model.DIALS["travel_fraction"]
    rate = []
    for d in dives:
        sw, rs = num(d, "swings"), num(d, "real_seconds")
        if sw and rs and rs > 5:
            rate.append(sw / (rs * dig_frac))
    rows.append(("swings_per_sec_base", model.DIALS["swings_per_sec_base"],
                 summary(rate), "스윙 ÷ (실시간초 × 파기비율 %.2f)" % dig_frac, rate))

    # ── battery_safety_margin ─────────────────────────────────────────────
    # 귀환(return)한 잠수만. 긴급탈출은 마진을 안 남긴 실패라 정의상 제외된다.
    margin = []
    for d in dives:
        if (d.get("result") or "") != "return":
            continue
        s0, s1 = num(d, "max_stamina_start"), num(d, "max_stamina_end")
        if s0 and s1 is not None and s0 > 0:
            margin.append(s1 / s0)
    rows.append(("battery_safety_margin", model.DIALS["battery_safety_margin"],
                 summary(margin), "종료max ÷ 시작max (귀환한 잠수만)", margin))

    # ── harvest_area_multiplier ───────────────────────────────────────────
    # 모델은 '판 픽셀 안의 광물'만 센다. 게임은 파인 영역의 박스 + 여유 0.3u 안에서
    # 지지를 잃은 광물을 전부 떨어뜨리므로(TerrainChunk.NotifyMineralsInDigRegion),
    # **터널 옆구리의 광물도 손에 들어온다.** 그 초과분이 이 배율이다.
    #
    # 잰 방식: 그 잠수가 **실제로 판 픽셀과 실제로 도달한 깊이**로 모델 밀도를 적분해
    # 기대 개수를 내고, 실측 개수를 그것으로 나눈다. 모델의 깊이·픽셀 *예측* 오차가
    # 섞이지 않도록 예측값이 아니라 실측값을 넣는 것이 핵심이다.
    harvest = []
    for d in dives:
        px, dep, cnt = (num(d, "pixels_dug"), num(d, "max_depth"),
                        num(d, "mineral_count"))
        if not px or not dep or cnt is None or abs(dep) < 1.0:
            continue
        if (d.get("result") or "return") != "return":
            continue          # 실패한 잠수는 캔 것을 잃어 개수가 무의미하다
        expected = expected_minerals(model, layers_cache(model), px, abs(dep))
        if expected > 0:
            harvest.append(cnt / expected)
    rows.append(("harvest_area_multiplier", model.DIALS["harvest_area_multiplier"],
                 summary(harvest), "실측 광물 ÷ (판 픽셀 × 그 깊이 밀도)", harvest))

    print()
    print("  %-32s %9s %9s %7s %6s %7s  %s"
          % ("상수", "모델값", "기준값", "기준/모델", "n", "드리프트", "판정"))
    print("  " + "-" * 82)

    fails, unknown, drifting, suspect = [], [], [], []
    for name, model_val, st, _how, series in rows:
        if st is None:
            print("  %-32s %9.3f %9s %7s %6d %7s  %s"
                  % (name, model_val, "—", "—", 0, "—", "표본없음"))
            unknown.append(name)
            continue
        info = drift(series)
        dr, recent_med, recent_n = info if info else (None, None, 0)
        confirmed = (dr is not None and abs(dr - 1.0) > P0_TOLERANCE
                     and recent_n >= MIN_DRIFT_RECENT)

        # 흐르는 값은 **최근값과 견준다.** 풀링 중앙값은 옛 플레이가 섞인 값이라,
        # 모델이 최근 실측을 정확히 따라가고 있어도 FAIL로 찍힌다(실제로 그랬다:
        # PX_PER_DEPTH를 최근값 10283으로 맞추자 풀링 6406 대비 0.62배 FAIL).
        # 모델이 설명해야 하는 것은 지금 빌드다.
        ref = recent_med if confirmed else st["median"]
        ratio = ref / model_val if model_val else float("nan")
        v = verdict(ratio)

        if confirmed:
            drifting.append((name, dr, recent_med, recent_n, v))
            v = "흐름·" + v
        elif dr is not None and abs(dr - 1.0) > P0_TOLERANCE:
            v = "드리프트?"
            suspect.append((name, dr, recent_med, recent_n))

        print("  %-32s %9.3f %9.3f %7.2fx %6d %7s  %s"
              % (name, model_val, ref, ratio, st["n"],
                 ("%.2fx" % dr) if dr is not None else "—", v))
        if v == "FAIL":
            fails.append((name, model_val, st["median"], ratio))

    print()
    print("  추정식: " + " · ".join("%s = %s" % (r[0].split("_at_")[0], r[3]) for r in rows))

    if drifting:
        print()
        print("  ⚠ 드리프트 %d개 — 이 값은 **상수가 아니다.** 최근 1/4이 그 이전과 다르다:"
              % len(drifting))
        for name, dr, recent_med, recent_n, sub in drifting:
            print("      %-34s 최근이 이전의 %.2f배 · 최근 중앙 %.3f (n=%d) → 모델 대비 %s"
                  % (name, dr, recent_med, recent_n, sub))
        print("    판정은 **최근값 기준**이다. 모델이 최근값을 따라가고 있으면 PASS이고,")
        print("    그래도 이 값은 흐른다는 사실 자체를 주석에 남겨 둘 것 —")
        print("    플레이 방식이 또 바뀌면 여기서 다시 잡힌다.")

    if fails:
        print()
        print("  ✖ FAIL %d개 — 모델 상수를 고치기 전에는 티어 B(지배노드·함정·다양성)"
              % len(fails))
        print("    판정을 신뢰하면 안 된다. run_model.py 의 값을 아래로 바꿀 것:")
        for name, mv, actual, ratio in fails:
            print("      %-34s %.3f -> %.3f  (%.2f배)" % (name, mv, actual, ratio))
    elif not unknown:
        print("  ✔ 모든 상수가 오차 %d%% 안 — 모델 기반 판정을 신뢰할 수 있다."
              % int(P0_TOLERANCE * 100))

    if suspect:
        print()
        print("  ⚠ 드리프트 의심 %d개 — 최근 표본이 %d개 미만이라 확정하지 않는다:"
              % (len(suspect), MIN_DRIFT_RECENT))
        for name, dr, recent_med, recent_n in suspect:
            print("      %-34s 최근이 이전의 %.2f배 · 최근 중앙 %.3f (n=%d)"
                  % (name, dr, recent_med, recent_n))
        print("    잠수를 더 모은 뒤 다시 볼 것. 지금 값을 바꾸면 노이즈를 쫓는 것이다.")

    if unknown:
        print()
        print("  ⚠ 표본없음: %s" % ", ".join(unknown))
        print("    해당 계측이 안 붙었거나 잠수 수가 부족하다. 판정 불가 = PASS 아님.")

    print()
    print("  주의: 시작 깊이가 로그에 없어 PX_PER_DEPTH 는 '지표면에서 시작'을 가정한다.")
    print("        엘리베이터로 중간부터 판 잠수가 섞이면 이 값은 과소 추정된다.")
    return fails, unknown, drifting


# ==========================================================================
# P0-B — 끝에서 끝까지 (상수가 아니라 결과가 맞는가)
# ==========================================================================

def check_p0_endtoend(dives, days, model):
    """스탯을 재구성할 수 있는 잠수에 한해 모델을 실제로 돌려 결과를 맞춰 본다.

    상수가 하나씩 맞아도 조립이 틀릴 수 있다(min() 의 주인이 다르면 결과가 갈린다).
    """
    print()
    print("=" * 74)
    print("P0-B — 끝에서 끝까지 (모델 실행 vs 실측)")
    print("=" * 74)

    layers = model.load_layers(str(TILE_DATA), str(PRICE_DATA))
    base = model.base_stats(str(PLAYER_SO))

    # 채광 레벨은 잠수 로그에 없다 — days.csv 를 (run_id, day)로 조인해 가져온다.
    level_by = {}
    for d in days:
        key = (d.get("run_id", ""), (d.get("day") or "").strip())
        lv = num(d, "mining_level")
        if lv is not None:
            level_by[key] = int(lv)

    pairs = []
    for d in dives:
        s0 = num(d, "max_stamina_start")
        if not s0:
            continue  # 배터리를 모르면 모델을 못 돌린다
        lv = level_by.get((d.get("run_id", ""), (d.get("day") or "").strip()),
                          int(base.mining_level))
        stats = base.copy(max_stamina=s0, mining_level=lv)
        r = model.income(stats, layers)
        pairs.append((d, r))

    if not pairs:
        print("  표본없음 — max_stamina_start 가 붙은 잠수가 없다.")
        print("  (계측이 최근에 추가돼 옛 로그엔 없다. 몇 판 더 돌리면 채워진다.)")
        return None

    print("  n=%d 잠수 (max_stamina_start 가 있는 것만)" % len(pairs))
    print()
    print("  %-14s %10s %10s %8s  %s" % ("항목", "모델중앙", "실측중앙", "실측/모델", "판정"))
    print("  " + "-" * 60)

    checks = [
        ("깊이",     lambda d, r: (r.detail.get("depth"), abs(num(d, "max_depth") or 0))),
        ("파기픽셀", lambda d, r: (r.detail.get("pixels"), num(d, "pixels_dug"))),
        ("스윙",     lambda d, r: (r.swings, num(d, "swings"))),
        ("광물개수", lambda d, r: (r.minerals, num(d, "mineral_count"))),
    ]

    worst = None
    for label, get in checks:
        mv, av = [], []
        for d, r in pairs:
            m, a = get(d, r)
            if m and a:
                mv.append(m)
                av.append(a)
        if not mv:
            print("  %-14s %10s %10s %8s  %s" % (label, "—", "—", "—", "표본없음"))
            continue
        mm, am = statistics.median(mv), statistics.median(av)
        ratio = am / mm if mm else float("nan")
        v = verdict(ratio)
        print("  %-14s %10.1f %10.1f %8.2fx  %s" % (label, mm, am, ratio, v))
        if v == "FAIL" and (worst is None or abs(ratio - 1) > abs(worst[1] - 1)):
            worst = (label, ratio)

    bn = {}
    for _d, r in pairs:
        bn[r.bottleneck] = bn.get(r.bottleneck, 0) + 1
    print()
    print("  모델이 본 병목: " + ", ".join("%s %d" % kv for kv in
                                         sorted(bn.items(), key=lambda x: -x[1])))
    if worst:
        print("  ✖ '%s'가 %.2f배 어긋난다 — 여기부터 본다." % worst)
    return worst


# ==========================================================================
# P12 — 판정 가능한 분산
# ==========================================================================

def check_p12(dives, days, prices):
    """잠수 간 수입 변동이 얼마나 큰지 재고, 그것이 강제하는 임계치를 계산한다.

    같은 스탯으로 두 번 잠수해도 수입은 다르다(어느 광물이 나왔나, 어디를 팠나).
    그 자연 변동보다 작은 차이는 **표본을 아무리 늘려도 이 표본 수에서는 못 가린다.**
    P2("업글하면 다음 잠수가 달라진다")의 임계치가 여기서 나온다.
    """
    print()
    print("=" * 74)
    print("P12 — 판정 가능한 분산")
    print("=" * 74)

    # **같은 회차·같은 업그레이드 상태**의 잠수끼리 묶는다. 스탯이 같은 잠수들의
    # 흩어짐이 곧 '순수 잠수 노이즈'다 — 어느 광물이 나왔나, 어디를 팠나.
    # 회차·일차를 섞어 한꺼번에 재면 성장 추세가 분산으로 잘못 계산돼
    # 노이즈를 크게 과대평가한다.
    #
    # 하루 단위(run_id, day)로 묶는 것이 더 직관적이지만 이 게임은 하루 1잠수가
    # 대부분이라 그룹이 거의 안 생긴다(실측 85그룹 중 2개만 2회 이상).
    # 업그레이드는 며칠에 하나씩 사므로 노드 수로 묶으면 같은 스탯 구간이
    # 여러 날에 걸쳐 모인다 — 같은 조건을 유지하면서 표본이 8배 늘어난다.
    nodes_by_day = {}
    for d in days:
        n = num(d, "unlocked_nodes")
        if n is not None:
            nodes_by_day[(d.get("run_id", ""), (d.get("day") or "").strip())] = int(n)

    groups, ungrouped = {}, 0
    for d in dives:
        g = dive_gold(d, prices)
        if g is None or g <= 0:
            continue
        key = (d.get("run_id", ""), (d.get("day") or "").strip())
        nodes = nodes_by_day.get(key)
        if nodes is None:
            ungrouped += 1
            continue
        groups.setdefault((key[0], nodes), []).append(g)

    ratios = []
    for vals in groups.values():
        if len(vals) < 2:
            continue
        med = statistics.median(vals)
        if med > 0:
            ratios.extend(v / med for v in vals)

    all_gold = [g for vals in groups.values() for g in vals]
    st_all = summary(all_gold)
    st_within = summary(ratios)

    if st_all:
        print("  잠수당 수입 (전체 n=%d): 중앙 %.0fG  사분위 %.0f~%.0fG"
              % (st_all["n"], st_all["median"], st_all["q1"], st_all["q3"]))
    if ungrouped:
        print("  (day_settled 가 없어 상태를 모르는 잠수 %d건 제외)" % ungrouped)

    n_groups = sum(1 for v in groups.values() if len(v) >= 2)
    if not st_within or n_groups < MIN_VARIANCE_GROUPS:
        print("  ✖ 같은 상태로 2회 이상 잠수한 그룹이 %d개뿐이다 (최소 %d 필요)."
              % (n_groups, MIN_VARIANCE_GROUPS))
        print("    순수 노이즈를 분리할 표본이 없다 — 임계치를 정하지 않는다.")
        print("    임계를 감으로 채우면 이후 모든 PASS/FAIL이 동전 던지기가 된다.")
        return None

    cv = st_within["cv"]
    print("  같은 회차·같은 노드 수 안의 변동계수(CV): %.0f%%  (%d개 잠수, %d개 그룹)"
          % (cv * 100, st_within["n"], n_groups))
    print()
    print("  → 이 CV에서 '전/후 차이'를 유의하게 주장하려면 필요한 최소 효과 크기:")
    print()
    print("     %8s  %s" % ("각 n판", "최소 판정 가능 차이(MDE)"))
    print("     " + "-" * 42)
    rec = None
    for n in (1, 2, 3, 5, 8, 12):
        mde = Z_FACTOR * cv * math.sqrt(2.0 / n)
        mark = ""
        if rec is None and mde <= 0.30:
            rec = (n, mde)
            mark = "  ← 현실적인 최소 표본"
        print("     %8d  %+.0f%%%s" % (n, mde * 100, mark))

    print()
    if rec:
        print("  권장: P2/P3 판정은 업글 전후 각 %d잠수 이상을 모아 %+.0f%% 를 임계로 쓴다."
              % (rec[0], rec[1] * 100))
    else:
        print("  ⚠ CV가 커서 12판을 모아도 30% 미만 차이는 못 가린다.")
        print("    임계를 크게 잡거나(예: +50%), 수입 대신 분산이 작은 지표")
        print("    (파기 픽셀·도달 깊이)로 P2를 판정하는 편이 낫다.")

    # ── 대체 지표 — 무엇으로 P2를 판정하는 것이 가장 싼가 ────────────────
    # 수입은 "어느 광물이 나왔나"라는 뽑기가 통째로 섞여 있어 분산이 크다.
    # 파기 픽셀·도달 깊이는 같은 파기 능력을 재면서 뽑기가 빠져 있어 더 조용하다.
    # 업그레이드 효과를 판정할 때는 조용한 지표를 쓰는 편이 표본을 훨씬 아낀다.
    print()
    print("  → 무엇으로 재는 것이 가장 싼가 (같은 그룹 기준):")
    print()
    print("     %-12s %6s %10s  %s" % ("지표", "CV", "MDE(n=3)", "필요 표본(30% 판정)"))
    print("     " + "-" * 58)

    alts = [("수입(G)", None), ("파기픽셀", "pixels_dug"), ("도달깊이", "max_depth"),
            ("광물개수", "mineral_count")]
    best = None
    for label, col in alts:
        vals = {}
        for d in dives:
            key = (d.get("run_id", ""), (d.get("day") or "").strip())
            nodes = nodes_by_day.get(key)
            if nodes is None:
                continue
            v = dive_gold(d, prices) if col is None else num(d, col)
            if v:
                vals.setdefault((key[0], nodes), []).append(abs(v))
        rs = []
        for group in vals.values():
            if len(group) < 2:
                continue
            med = statistics.median(group)
            if med > 0:
                rs.extend(x / med for x in group)
        st = summary(rs)
        if not st or math.isnan(st["cv"]):
            print("     %-12s %6s %10s  %s" % (label, "—", "—", "표본없음"))
            continue
        c = st["cv"]
        mde3 = Z_FACTOR * c * math.sqrt(2.0 / 3)
        need = math.ceil(2.0 * (Z_FACTOR * c / 0.30) ** 2)
        print("     %-12s %5.0f%% %9.0f%%  각 %d판" % (label, c * 100, mde3 * 100, need))
        if best is None or c < best[1]:
            best = (label, c, need)

    if best:
        print()
        print("  권장: P2 판정은 '%s'(CV %.0f%%)로 한다 — 30%% 차이를 각 %d판에 가릴 수 있어"
              % (best[0], best[1] * 100, best[2]))
        print("        수입으로 재는 것보다 표본이 적게 든다. 수입은 어느 광물이 나왔나라는")
        print("        뽑기가 섞여 있어 같은 능력을 재는 데 더 비싸다.")

    print()
    print("  ⚠ 애초 초안의 '+8%'는 근거가 없었다. 위 표가 그것을 대체한다.")

    # 티어 A(P2/P3)가 쓸 임계치를 여기서 넘긴다 — 감으로 정하지 않기 위해서다.
    if best:
        return dict(metric=best[0], cv=best[1], min_side=best[2], threshold=0.30)
    return None


# ==========================================================================
# 티어 A — 텔레메트리로 직접 판정
#
# 여기 있는 원칙은 전부 **한 회차 안의 시계열**만 쓴다. 여러 플레이어의 분포를
# 요구하지 않으므로 혼자 플레이해도 유효하다. 분포가 필요한 원칙(지배 노드·
# 빌드 다양성)은 티어 B로 넘겼다 — charter §1.
# ==========================================================================

def gate_nodes():
    """관문(합류점) 노드 id 집합. reprice_template.CONVERGENCE 가 정본이다.

    여기에 목록을 복사해 두면 트리를 바꿀 때 두 곳이 어긋난다. 임포트가 실패하면
    비어 있는 집합을 돌려주고 P1은 '관문 판정 없이' 돌아간다 — 조용히 틀린 답을
    내놓는 것보다 낫다.
    """
    try:
        import reprice_template
        return set(reprice_template.CONVERGENCE)
    except Exception as exc:                                   # noqa: BLE001
        print("  ⚠ 관문 목록을 못 읽었다 (%s) — 관문 예외 없이 판정한다." % exc)
        return set()


def by_run_day(rows):
    """(run_id, day) -> 행 목록."""
    out = {}
    for r in rows:
        out.setdefault((r.get("run_id", ""), _day(r)), []).append(r)
    return out


def _day(row):
    d = num(row, "day")
    return int(d) if d is not None else None


def run_days(days):
    """run_id -> 일차 오름차순 day_settled 행."""
    out = {}
    for d in days:
        if _day(d) is not None:
            out.setdefault(d.get("run_id", ""), []).append(d)
    for rows in out.values():
        rows.sort(key=_day)
    return out


def failure_days(failures, dives):
    """실패한 (run_id, day) 집합. 사망 + 긴급탈출.

    ⚠ **dives.csv 의 result 만 보면 사망이 통째로 빠진다.** 사망하면
    GameOverHandler 가 SettlementManager 를 안 거쳐 dive_end 자체가 안 찍히기
    때문이다(export_csv.FAILURE_FIELDS 주석 참고). failures.csv 가 정본이고,
    dives.csv 의 escape 는 보조로만 합친다.
    """
    out = {}
    for f in failures:
        key = (f.get("run_id", ""), _day(f))
        if _day(f) is not None:
            out.setdefault(key, set()).add(f.get("kind", "?"))
    for key, rows in by_run_day(dives).items():
        for d in rows:
            res = d.get("result") or ""
            if res in ("escape", "death"):
                out.setdefault(key, set()).add(res)
    return out


def tree_costs():
    """UpgradeTree.csv 의 {nodeId: 가격}. 가격은 손으로 튜닝하는 값이라 매번 읽는다."""
    path = REPO / "Assets/GameData/UpgradeData/UpgradeTree.csv"
    if not path.exists():
        return {}
    out = {}
    with path.open(encoding="utf-8-sig", newline="") as f:
        for row in csv.DictReader(f):
            try:
                out[row["nodeId"]] = float(row.get("cost") or 0)
            except (ValueError, KeyError):
                pass
    return out


def check_p1(days, dives, upgrades, failed, prices):
    """P1 — 관문 노드가 아니면 하루에 하나는 살 수 있어야 한다.

    ⚠ **구매 행위가 아니라 구매력을 재야 한다.**
    처음에는 "그날 산 게 있나"로 판정했는데, 매출 0인 날을 전부 FAIL로 찍었다.
    실제로 그날들은 광물을 캤지만 **팔지 않고 창고에 넣은 날**이었다
    (창고 수량이 그날 캔 개수와 정확히 일치: 3/3, 11/11, 15/15, 20/20, 32/32).
    팔았으면 살 수 있었으므로 트리가 막은 것이 아니다 — 플레이어의 선택이다.

    그래서 판정은 `그날 가진 골드 + 그날 캔 광물의 값` 이 **그 시점 최저가
    미구매 노드**보다 적을 때만 한다. 최저가 노드를 쓰는 것은 하한이라, 여기에
    걸리면 무엇을 노렸든 못 샀다는 뜻이 된다.

    긴급탈출·사망한 날은 제외한다. 그날 못 산 것은 트리가 빡빡해서가 아니라
    광물을 잃어서이고, 그것은 P8(실패 비용)이 따로 본다.
    """
    print("=" * 74)
    print("P1 — 관문 외엔 하루 하나는 산다")
    print("=" * 74)

    gates = gate_nodes()
    bought = by_run_day([u for u in upgrades if (u.get("blocked") or "0") == "0"])
    escaped = failed

    costs = tree_costs()
    dive_by_day = by_run_day(dives)

    streaks, total_days, stockpiled = [], 0, 0
    for run, rows in run_days(days).items():
        run_streak = []
        owned = set()                    # 이 회차에서 지금까지 산 노드
        for d in rows:
            key = (run, _day(d))
            total_days += 1
            for u in bought.get(key, []):
                owned.add(u.get("node", ""))
            if key in escaped:
                run_streak = []          # 실패한 날은 사슬을 끊는다(면제)
                continue
            if bought.get(key):
                run_streak = []
                continue

            # 안 산 날 — 못 산 것인지 안 산 것인지 가른다.
            # 구매력 = 그날 시작 골드 + 그날 벌 수 있었던 돈.
            # 실제 매출(mineral_sale)과 캔 광물의 장부가 중 **큰 쪽**을 쓴다 —
            # 판매가 업그레이드·보너스가 붙으면 실매출이 장부가보다 크고,
            # 안 팔고 창고에 넣었으면 실매출이 0이라 장부가가 크다.
            # 한쪽만 보면 각각 반대 방향으로 틀린다(실측: 장부 18G vs 실매출 21G,
            # 최저가 노드가 20G라 판정이 뒤집혔다).
            potential = 0.0
            for v in dive_by_day.get(key, []):
                potential += dive_gold(v, prices) or 0.0
            afford = (num(d, "gold_start", 0.0) or 0.0) + max(
                potential, num(d, "mineral_sale", 0.0) or 0.0)
            remaining = [c for n, c in costs.items() if n not in owned and c > 0]
            if remaining and afford >= min(remaining):
                stockpiled += 1
                run_streak = []          # 살 수 있었는데 안 샀다 — 트리 탓이 아니다
                continue

            run_streak.append(_day(d))
            if len(run_streak) >= 2:
                streaks.append((run, list(run_streak)))
    # 같은 회차의 겹치는 사슬은 가장 긴 것만 남긴다
    longest = {}
    for run, s in streaks:
        prev = longest.get(run)
        if prev is None or len(s) > len(prev):
            longest[run] = s

    kinds = collections.Counter(k for v in escaped.values() for k in v)
    print("  검사한 일차 %d개 · 구매가 있던 날 %d개 · 실패로 면제된 날 %d개 (%s)"
          % (total_days, len(bought), len(escaped),
             ", ".join("%s %d" % kv for kv in sorted(kinds.items())) or "없음"))
    print("  살 수 있었는데 안 산 날 %d개 (창고에 쟁였거나 저축) — 구매력 기준으로 제외"
          % stockpiled)

    if not longest:
        print("  ✔ PASS — 2일 연속 아무것도 못 산 구간이 없다.")
        return True

    print("  ✖ FAIL — 2일 이상 연속으로 아무것도 못 산 구간 %d개:" % len(longest))
    for run, s in sorted(longest.items(), key=lambda kv: -len(kv[1])):
        # 그 직후에 산 것이 관문이면 의도된 저축 구간이다
        after = None
        for d in sorted(set(x[1] for x in bought if x[0] == run)):
            if d > s[-1]:
                after = d
                break
        nodes = [u.get("node", "") for u in bought.get((run, after), [])] if after else []
        tag = ""
        if nodes and gates and any(n in gates for n in nodes):
            tag = "  ← 직후 구매가 관문(%s), 저축 구간으로 보임" % nodes[0]
        print("     회차 %s  %d일차부터 %d일 연속%s"
              % (run[:8], s[0], len(s), tag))
    print()
    print("  관문 직전 저축 구간은 의도된 것이다. 그 밖의 구간이 남으면 그 시점의")
    print("  가격이 그날 수입보다 앞서 있다는 뜻이다.")
    return False


def _state_series(dives, days, value_of):
    """(run_id, 노드 수) -> 값 목록. 업그레이드 전후 비교의 공통 뼈대.

    같은 회차·같은 노드 수 = 스탯이 같은 구간이다. 노드 수가 k -> k+1로 넘어가는
    지점이 곧 '업그레이드를 하나 샀다'이고, 그 앞뒤를 비교하는 것이 P2·P3다.
    """
    nodes_by = {}
    for d in days:
        n = num(d, "unlocked_nodes")
        if n is not None:
            nodes_by[(d.get("run_id", ""), _day(d))] = int(n)

    out = {}
    for d in dives:
        k = nodes_by.get((d.get("run_id", ""), _day(d)))
        if k is None:
            continue
        v = value_of(d)
        if v:
            out.setdefault((d.get("run_id", ""), k), []).append(v)
    return out


def check_p2(dives, days, cv, floor):
    """P2 — 업그레이드하면 다음 잠수가 달라진다.

    **수입이 아니라 파기 픽셀로 잰다** (charter §4). 수입에는 "어느 광물이
    나왔나"라는 뽑기가 통째로 섞여 있어 같은 능력을 재는 데 표본이 2.5배 든다.

    임계치를 하나로 고정하지 않는다. 구간마다 잠수 수가 다르므로 **그 구간이
    실제로 가릴 수 있는 크기**(MDE)를 표본 수에서 매번 계산하고, 관측된 변화가
    그것을 넘을 때만 판정한다. 고정 임계를 쓰면 표본이 적은 구간에서 노이즈를
    효과로 읽거나(관대할 때), 멀쩡한 데이터를 통째로 버린다(엄할 때).

    `floor`는 통계와 무관한 설계 하한이다 — 표본이 아무리 많아도 이보다 작은
    변화는 "달라졌다"고 부르지 않는다.
    """
    print()
    print("=" * 74)
    print("P2 — 업그레이드하면 다음 잠수가 달라진다 (파기 픽셀)")
    print("=" * 74)

    series = _state_series(dives, days, lambda d: num(d, "pixels_dug"))
    steps, thin = [], 0
    for (run, k), before in sorted(series.items()):
        after = series.get((run, k + 1))
        if not after:
            continue
        if len(before) < 2 or len(after) < 2:
            thin += 1
            continue
        b, a = statistics.median(before), statistics.median(after)
        mde = Z_FACTOR * cv * math.sqrt(1.0 / len(before) + 1.0 / len(after))
        steps.append((run, k, b, a, (a / b - 1.0) if b else float("nan"),
                      len(before), len(after), max(mde, floor)))

    print("  비교 가능한 구매 %d건 (양쪽 2잠수 미만이라 건너뜀 %d건)" % (len(steps), thin))
    if not steps:
        print("  판정 불가 — 같은 노드 수로 2잠수 이상 모인 구간이 없다.")
        print("  하루 1잠수 습관이면 잘 안 모인다. 같은 상태로 여러 판 뛰면 채워진다.")
        return None

    print()
    print("     %-10s %5s %9s %9s %7s %7s %8s  %s"
          % ("회차", "노드", "이전", "이후", "n(전/후)", "변화", "판정선", "판정"))
    print("     " + "-" * 70)
    passed = 0
    for run, k, b, a, ch, nb, na, need in steps:
        ok = ch >= need
        passed += ok
        print("     %-10s %5d %9.0f %9.0f %7s %+6.0f%% %+7.0f%%  %s"
              % (run[:8], k, b, a, "%d/%d" % (nb, na), ch * 100, need * 100,
                 "PASS" if ok else "FAIL"))

    rate = passed / len(steps)
    print()
    print("  판정선 = max(그 표본이 가릴 수 있는 크기, 설계 하한 %+.0f%%)" % (floor * 100))
    print("  %d/%d 구매가 자기 판정선을 넘었다 (%.0f%%)" % (passed, len(steps), rate * 100))
    if rate < 0.5:
        print("  ✖ FAIL — 절반 이상의 업그레이드가 다음 잠수를 유의하게 바꾸지 못한다.")
        return False
    print("  ✔ PASS")
    return True


def check_p3(dives, days, upgrades, prices, max_payback):
    """P3 — 회수 기간 상한. 가격 ÷ 잠수당 수입 증분.

    P2와 달리 여기서는 **수입**을 써야 한다 — 회수는 골드로 일어나기 때문이다.
    분산이 크다는 것을 알고 쓰는 것이라, 판정은 개별 노드가 아니라 중앙값으로 한다.
    """
    print()
    print("=" * 74)
    print("P3 — 회수 기간 상한")
    print("=" * 74)

    gates = gate_nodes()
    series = _state_series(dives, days, lambda d: dive_gold(d, prices))

    # 노드 수 k -> k+1 사이에 산 노드의 가격
    cost_at = {}
    nodes_at = {}
    nodes_by = {}
    for d in days:
        n = num(d, "unlocked_nodes")
        if n is not None:
            nodes_by[(d.get("run_id", ""), _day(d))] = int(n)
    for u in upgrades:
        if (u.get("blocked") or "0") != "0":
            continue
        k = nodes_by.get((u.get("run_id", ""), _day(u)))
        c = num(u, "cost")
        if k is None or not c:
            continue
        cost_at.setdefault((u.get("run_id", ""), k), []).append(c)
        nodes_at.setdefault((u.get("run_id", ""), k), []).append(u.get("node", ""))

    rows = []
    for (run, k), before in sorted(series.items()):
        after = series.get((run, k + 1))
        costs = cost_at.get((run, k))
        if not after or not costs:
            continue
        if len(before) < 2 or len(after) < 2:
            continue
        gain = statistics.median(after) - statistics.median(before)
        cost = sum(costs)
        is_gate = bool(gates and any(n in gates for n in nodes_at.get((run, k), [])))
        rows.append((run, k, cost, gain, is_gate))

    if not rows:
        print("  판정 불가 — 가격과 수입 증분을 둘 다 아는 구간이 없다.")
        return None

    print("     %-10s %5s %8s %9s %9s  %s"
          % ("회차", "노드", "가격", "수입증분", "회수잠수", "판정"))
    print("     " + "-" * 60)
    bad = 0
    for run, k, cost, gain, is_gate in rows:
        limit = max_payback + (2 if is_gate else 0)
        if gain <= 0:
            verdict_s, payback = "FAIL(증분≤0)", float("inf")
        else:
            payback = cost / gain
            verdict_s = "PASS" if payback <= limit else "FAIL"
        bad += verdict_s.startswith("FAIL")
        print("     %-10s %5d %8.0f %9.0f %9s  %s%s"
              % (run[:8], k, cost, gain,
                 "∞" if payback == float("inf") else "%.1f" % payback,
                 verdict_s, "  (관문)" if is_gate else ""))

    print()
    print("  상한: 일반 %d잠수 · 관문 %d잠수" % (max_payback, max_payback + 2))
    if bad:
        print("  ✖ FAIL %d건 — 그 가격은 그 시점 수입으로 회수되지 않는다." % bad)
        return False
    print("  ✔ PASS")
    return True


def check_p6(upgrades):
    """P6 — 벽 금지. `upgrade_blocked`의 부족 금액이 회당 수입의 몇 배인가."""
    print()
    print("=" * 74)
    print("P6 — 벽 금지")
    print("=" * 74)

    blocked = [u for u in upgrades if (u.get("blocked") or "0") == "1"]
    if not blocked:
        print("  ✖ 계측 불가 — `upgrade_blocked` 이벤트가 한 건도 없다.")
        print()
        print("  이벤트는 UpgradeManager.UnlockNode 안에 배선돼 있지만 **도달할 수 없다.**")
        print("  호출부 세 곳이 전부 CanUnlock으로 먼저 막는다:")
        print("    UpgradeSlotUI.cs:122      if (!_isUnlocked && _isUnlockable)")
        print("    UpgradeOverlayUI.cs:1342  if (mgr.CanUnlock(_selected))")
        print("    UpgradeOverlayUI.cs:1575  _unlockButton.interactable = canUnlock")
        print()
        print("  즉 '사고 싶었지만 못 샀다'가 기록되는 경로가 없다. 게임 코드를 고쳐야 한다.")
        return None

    short = [num(u, "short_by") for u in blocked
             if (u.get("reason") or "") == "gold"]
    st = summary(short)
    if not st:
        print("  골드 부족으로 막힌 기록이 없다 (선행조건 미충족만 있음).")
        return None
    print("  골드 부족 %d건 · 부족 금액 중앙 %.0fG" % (st["n"], st["median"]))
    return None


def check_p8(days, dives, failed):
    """P8 — 실패 비용 상한. 한 번 망해도 며칠씩 되돌아가지 않아야 한다."""
    print()
    print("=" * 74)
    print("P8 — 실패 비용 상한")
    print("=" * 74)

    escaped = failed
    if not escaped:
        print("  실패한 날이 없다 — 판정 불가 (실패 표본 없음).")
        return None

    rows = []
    for run, seq in run_days(days).items():
        for i, d in enumerate(seq):
            if (run, _day(d)) not in escaped:
                continue
            g0, g1 = num(d, "gold_start"), num(d, "gold_end")
            if g0 is None or g1 is None:
                continue
            prior = [num(x, "gold_end", 0) - num(x, "gold_start", 0)
                     for x in seq[max(0, i - 3):i]]
            base = statistics.median([p for p in prior if p is not None]) if prior else None
            rows.append((run, _day(d), g1 - g0, base,
                         "/".join(sorted(escaped[(run, _day(d))]))))

    print("     %-10s %5s %8s %10s %12s  %s"
          % ("회차", "일차", "종류", "당일손익", "직전3일중앙", "판정"))
    print("     " + "-" * 62)
    bad = 0
    for run, day, delta, base, kind in rows:
        if base is None or base <= 0:
            v = "기준없음"
        elif delta < -base:
            v = "FAIL"
            bad += 1
        else:
            v = "PASS"
        print("     %-10s %5d %8s %10.0f %12s  %s"
              % (run[:8], day, kind, delta,
                 "—" if base is None else "%.0f" % base, v))

    print()
    if bad:
        print("  ✖ FAIL %d건 — 한 번의 실패가 하루 이상의 진행을 되돌린다." % bad)
        return False
    print("  ✔ PASS — 실패해도 직전 수입 이상을 잃지는 않는다.")
    return True


def check_p9(dives, days, prices):
    """P9 — 성장 곡선 포화. 후반에 성장률이 전반보다 크면(발산) 실패."""
    print()
    print("=" * 74)
    print("P9 — 성장 곡선 포화")
    print("=" * 74)

    series = _state_series(dives, days, lambda d: dive_gold(d, prices))
    per_run = {}
    for (run, k), vals in series.items():
        per_run.setdefault(run, []).append((k, statistics.median(vals)))

    rows = []
    for run, pts in per_run.items():
        pts.sort()
        if len(pts) < 4:
            continue
        half = len(pts) // 2
        early, late = pts[:half], pts[half:]
        # 노드 하나당 수입 증가폭 — 구간 양 끝의 기울기
        def slope(seg):
            dk = seg[-1][0] - seg[0][0]
            return (seg[-1][1] - seg[0][1]) / dk if dk else None
        se, sl = slope(early), slope(late)
        if se is None or sl is None:
            continue
        rows.append((run, len(pts), se, sl))

    if not rows:
        print("  판정 불가 — 노드 수 구간이 4개 이상인 회차가 없다.")
        return None

    print("     %-10s %6s %12s %12s  %s"
          % ("회차", "구간수", "전반 기울기", "후반 기울기", "판정"))
    print("     " + "-" * 56)
    bad = 0
    for run, n, se, sl in rows:
        # 18.0 -> 18.2 같은 것은 발산이 아니라 노이즈다. 20% 여유를 둔다.
        diverging = se > 0 and sl > se * P9_DIVERGE_MARGIN
        bad += diverging
        print("     %-10s %6d %12.1f %12.1f  %s"
              % (run[:8], n, se, sl, "FAIL(발산)" if diverging else "PASS"))

    print()
    print("  기울기 = 노드 하나당 잠수 수입 증가폭(G). 후반이 전반보다 크면 발산이다.")
    if bad:
        print("  ✖ FAIL %d회차 — 후반에 수입이 가속한다. 상한이 없다는 뜻이다." % bad)
        return False
    print("  ✔ PASS — 성장이 포화한다.")
    return True


# ==========================================================================
# P13 — 편의성 축
#
# 채굴 속도·이동 속도·벽타기·스태미나 소모율은 **수입을 안 올린다.** 런 모델의
# income()이 그 축을 수식에 안 쓰기 때문에 한계효용이 언제나 0이고, 그래서
# 티어 B로는 "함정인지 편의성인지"를 영영 못 가린다(charter §7).
#
# 그러면 잣대를 바꿔야 한다. 수입이 아니라 **체감 지표**로 잰다.
# 원칙의 모양은 P2와 같다 — 사고 나면 달라져야 한다. 재는 대상만 다르다.
# ==========================================================================

# (effectType, 라벨, 지표 이름, dive 행 -> 값, 좋아지는 방향)
# 방향 -1 = 값이 **줄어야** 좋아진 것(시간·소모).
CONVENIENCE = [
    ("MiningSpeedUp", "채굴 속도", "초당 스윙",
     lambda d, m: (num(d, "swings") / (num(d, "real_seconds") * m))
     if num(d, "swings") and num(d, "real_seconds", 0) > 5 else None, +1),
    ("MoveSpeedUp", "이동 속도", "깊이당 실시간(초/m)",
     lambda d, m: (num(d, "real_seconds") / abs(num(d, "max_depth")))
     if num(d, "real_seconds") and abs(num(d, "max_depth", 0) or 0) > 1 else None, -1),
    ("WallClimbSpeed", "벽타기 속도", "매달린 시간(초)",
     lambda d, m: num(d, "climb_seconds"), -1),
    ("StaminaCostReduce", "스태미나 소모율", "등반 초당 드레인",
     lambda d, m: (num(d, "climb_stamina") / num(d, "climb_seconds"))
     if num(d, "climb_stamina") and num(d, "climb_seconds", 0) > 1 else None, -1),
]


def check_p13(dives, days, upgrades, model):
    """P13 — 편의성 노드를 사면 그 체감 지표가 달라져야 한다."""
    print()
    print("=" * 74)
    print("P13 — 편의성 축은 체감 지표로 잰다")
    print("=" * 74)

    if not upgrades:
        print("  판정 불가 — upgrades.csv 가 없다.")
        return None

    nodes_by = {}
    for d in days:
        n = num(d, "unlocked_nodes")
        if n is not None:
            nodes_by[(d.get("run_id", ""), _day(d))] = int(n)

    # 노드 수 k -> k+1 사이에 어떤 effectType을 샀나
    bought_at = {}
    for u in upgrades:
        if (u.get("blocked") or "0") != "0":
            continue
        k = nodes_by.get((u.get("run_id", ""), _day(u)))
        if k is None:
            continue
        bought_at.setdefault((u.get("run_id", ""), k), []).append(u.get("node", ""))

    dig_frac = 1.0 - model.DIALS["travel_fraction"]
    print("     %-14s %-18s %8s %9s %9s  %s"
          % ("축", "지표", "n(전/후)", "이전", "이후", "판정"))
    print("     " + "-" * 72)

    any_judged, failed = False, 0
    for effect, label, metric, getter, sign in CONVENIENCE:
        series = _state_series(dives, days, lambda d: getter(d, dig_frac))
        rows = []
        for (run, k), before in sorted(series.items()):
            after = series.get((run, k + 1))
            if not after or len(before) < 2 or len(after) < 2:
                continue
            # 그 구간에서 이 축의 노드를 실제로 샀는가 (노드 id 접두사로 판정)
            names = bought_at.get((run, k), [])
            if not any(effect.replace("Up", "").replace("Reduce", "").replace("Speed", "")
                       in nm or _matches(effect, nm) for nm in names):
                continue
            b, a = statistics.median(before), statistics.median(after)
            rows.append((run, k, b, a, len(before), len(after)))

        if not rows:
            has_data = any(getter(d, dig_frac) is not None for d in dives)
            note = "표본 없음" if has_data else "계측 없음"
            print("     %-14s %-18s %8s %9s %9s  %s"
                  % (label, metric, "-", "-", "-", note))
            continue

        any_judged = True
        for run, k, b, a, nb, na in rows:
            change = (a / b - 1.0) * sign if b else float("nan")
            ok = change > 0
            failed += (not ok)
            print("     %-14s %-18s %8s %9.2f %9.2f  %s (%+.0f%%)"
                  % (label, metric, "%d/%d" % (nb, na), b, a,
                     "PASS" if ok else "FAIL", change * 100))

    print()
    if not any_judged:
        print("  판정 불가 — 편의성 노드를 산 전후로 표본이 모인 구간이 없다.")
        print("  climb_seconds / climb_stamina 는 2026-08-31에 추가된 계측이라")
        print("  그 이전 로그에는 없다. 몇 판 더 돌리면 채워진다.")
        return None
    if failed:
        print("  ✖ FAIL %d건 — 샀는데 체감 지표가 안 좋아졌다." % failed)
        return False
    print("  ✔ PASS")
    return True


def _matches(effect, node_id):
    """effectType과 노드 id 접두사를 잇는다(UpgradeTree.csv의 명명 규칙)."""
    prefix = {"MiningSpeedUp": "MiningSpeed", "MoveSpeedUp": "MoveSpeed",
              "WallClimbSpeed": "ClimbSpeed", "StaminaCostReduce": "StaminaCost"}
    p = prefix.get(effect)
    return bool(p) and node_id.startswith(p)


# ==========================================================================

def main():
    ap = argparse.ArgumentParser(description="업그레이드 밸런스 원칙 체커 (P0 / P12)")
    ap.add_argument("--csv-dir", required=True,
                    help="export_csv.py 가 만든 days.csv/dives.csv 가 있는 폴더")
    a = ap.parse_args()

    import run_model

    csv_dir = Path(a.csv_dir)
    dives = read_csv(csv_dir / "dives.csv")
    days = read_csv(csv_dir / "days.csv")
    up_path = csv_dir / "upgrades.csv"
    upgrades = read_csv(up_path) if up_path.exists() else []
    fail_path = csv_dir / "failures.csv"
    failures = read_csv(fail_path) if fail_path.exists() else []

    # 드리프트는 순서에 의존한다. 익스포터는 run_id(GUID)로 정렬하므로
    # 그대로 쓰면 시간순이 아니다 — session_start(파일명 시각)로 다시 세운다.
    dives.sort(key=lambda d: (d.get("session_start", ""), num(d, "playtime", 0.0)))
    prices = mineral_prices()

    print("입력: %s — 잠수 %d건 / 일차 %d건" % (csv_dir, len(dives), len(days)))
    print()

    fails, unknown, drifting = check_p0(dives, run_model)
    check_p0_endtoend(dives, days, run_model)
    rec = check_p12(dives, days, prices)

    # 임계치는 P12가 정한다. 표본이 모자라 못 정하면 티어 A를 건너뛴다 —
    # 근거 없는 숫자로 PASS/FAIL을 찍는 것이 판정을 안 하는 것보다 나쁘다.
    print()
    if rec is None:
        print("=" * 74)
        print("티어 A 건너뜀 — P12가 임계치를 못 정했다 (표본 부족).")
        results = {}
    else:
        cv, thr = rec["cv"], rec["threshold"]
        print("티어 A 임계치: P12가 정한 값 — 지표 %s (CV %.0f%%), 설계 하한 %+.0f%%"
              % (rec["metric"], cv * 100, thr * 100))
        print("  판정선은 구간마다 그 표본 수로 다시 계산한다(고정값 아님).")
        print()
        if not upgrades:
            print("⚠ upgrades.csv 가 없다 — P1·P3·P6는 건너뛴다. export_csv.py를 다시 돌려라.")
            print()
        failed = failure_days(failures, dives)
        results = {
            "P1": check_p1(days, dives, upgrades, failed, prices) if upgrades else None,
            "P2": check_p2(dives, days, cv, thr),
            "P3": check_p3(dives, days, upgrades, prices, 4) if upgrades else None,
            "P6": check_p6(upgrades) if upgrades else None,
            "P8": check_p8(days, dives, failed),
            "P9": check_p9(dives, days, prices),
            "P13": check_p13(dives, days, upgrades, run_model),
        }

    print()
    print("=" * 74)
    print("요약")
    print("=" * 74)
    model_ok = not fails
    print("  P0  모델-실측 정합           %s" % ("PASS" if model_ok else
          "FAIL (어긋남 %d · 드리프트 %d)" % (len(fails), len(drifting))))
    for k in ("P1", "P2", "P3", "P6", "P8", "P9", "P13"):
        v = results.get(k)
        label = {"P1": "관문 외엔 하루 하나", "P2": "업글하면 달라진다",
                 "P3": "회수 기간 상한", "P6": "벽 금지",
                 "P8": "실패 비용 상한", "P9": "성장 곡선 포화",
                 "P13": "편의성 축 체감"}[k]
        print("  %-3s %-24s %s" % (k, label,
              "PASS" if v is True else ("FAIL" if v is False else "판정불가")))
    print()
    if not model_ok:
        print("  티어 B(지배노드·함정·다양성)는 판정하지 않는다 — 모델이 현재 빌드를")
        print("  설명하지 못한다. charter §5-A 참고.")

    failed = [k for k, v in results.items() if v is False]
    if fails or failed:
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
