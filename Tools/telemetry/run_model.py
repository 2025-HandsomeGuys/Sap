#!/usr/bin/env python3
"""잠수 1회의 수입을 스탯 벡터에서 계산한다. 업그레이드 트리 자동 생성의 토대.

왜 있나
-------
fit_upgrade_prices.py의 수입 모델은 이렇다.

    income(k) = base + growth * (산 노드 수)

**어떤 노드를 샀는지가 안 들어간다.** 스태미나를 사든 무게를 사든 똑같이 +64G다.
이 모델 위에서는 "하나를 채우면 다른 게 부족해진다"를 표현할 방법이 없다 —
무엇이 부족한지를 모델이 구분하지 못하기 때문이다.

이 파일은 그 자리를 대신한다. 핵심은 곱셈이 아니라 **min()** 이다.

    캘 수 있는 양   = min(스태미나로 가능한 스윙, 시간으로 가능한 스윙)
    담을 수 있는 양 = min(캔 양, 무게 한도, 슬롯 한도)

min()의 주인이 바뀌는 것이 곧 병목이 넘어가는 것이고, 그게 목표하는 압박 루프다.
`bottleneck()`이 지금 누가 주인인지 돌려준다.

계수는 어디서 오나
------------------
**층 저항·광물 가격/무게/분포·기본 스탯은 전부 실제 데이터 파일에서 읽는다.**
하드코딩하지 않는다 — 광물 가격을 바꾸면 이 모델이 따라와야 한다.

반면 아래 DIALS는 **아직 실측이 없는 추정값**이다. 현재 업그레이드 트리가
정리되지 않아 텔레메트리(dives.csv)가 모델을 검증할 근거가 못 되므로,
지금은 구조를 먼저 세우고 계수는 다이얼로 빼 둔다.
트리가 정리된 뒤 --calibrate 로 맞춘다.

설계: Assets/Docs/superpowers/specs/2026-08-21-upgrade-tree-autogen-design.md §6
표준 라이브러리만 쓴다.
"""

import argparse
import json
import math
import sys
from pathlib import Path

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):
        pass

TILE_DATA = "Assets/StreamingAssets/tileData.json"
PRICE_DATA = "Assets/StreamingAssets/priceData.json"
PLAYER_SO = "Assets/GameData/PlayerData/DefalutPlayerSO.asset"

# 청크 한 변의 픽셀 수 (TerrainChunk._width/_height = 1000)
CHUNK_PX = 1000 * 1000

# 청크 한 변의 월드 유닛 (1000px / 100 PPU).
# 광물 규칙의 minDepth/maxDepth는 **청크 인덱스**라 월드 깊이를 이걸로 나눠 써야 한다.
UNITS_PER_CHUNK = 10.0

# ──────────────────────────────────────────────────────────────────────────
# 실측 상수 — 텔레메트리 28잠수(3회차)에서 나온 값. 추측이 아니다.
# ──────────────────────────────────────────────────────────────────────────

# 깊이 1유닛을 내려가는 데 파야 하는 픽셀.
#
# ⚠ **이것은 물리 상수가 아니라 플레이 방식이다.** 곧게 내려가면 작고,
#   광물을 캐러 곁굴을 파면 커진다. 실측이 그것을 그대로 보여준다:
#
#     2026-08-11 ~ 08-22   약 6,000~6,500   (곧게 내려가던 시기)
#     2026-08-22 ~ 08-31   약 10,283        (곁굴을 파기 시작한 뒤)
#
#   같은 사람의 습관이 바뀌는 것만으로 1.65배 움직였다. 다른 플레이어가 들어오면
#   더 벌어진다. **여기에 '정답'은 없고, 지금 빌드를 설명하는 최근값이 있을 뿐이다.**
#
#   원래 근거였던 "깊이와 파기량의 상관 r=0.945"는 한 플레이 스타일 안에서만
#   성립한다. 스타일이 섞이면 그 상관이 곧 이 값의 드리프트로 나타난다.
#
# 무엇에 영향을 주나: **깊이 예측 = 면허 게이트(채광 레벨) 도달 시점**이다.
# 광물 개수에는 거의 영향이 없다 — 적분이 판 픽셀 총량으로 수렴하기 때문
# (charter §5-B에서 실측으로 확인: 6365 -> 11547로 바꿔도 광물 7.5 -> 7.0).
#
# **2026-08-31: 6365 -> 10283** (최근 1/4 중앙, n=22). check_principles.py 가
# 드리프트를 감시하므로, 플레이 방식이 또 바뀌면 거기서 다시 잡힌다.
PX_PER_DEPTH = 10283.0

# 스태미나 재생 (StaminaManager.cs: 3초 지연 후 초당 20).
# 최대치가 30이라 1.5초면 가득 찬다. 그래서 실측 28잠수 전부
# stamina_pct 중앙값 1.00 — 스태미나를 가득 채운 채 귀환한다.
#
# ⚠ 그래서 스태미나는 **총량 예산이 아니라 연속 파기 제한기**다.
#   드레인(초당 스윙 x 스윙비용)이 재생률을 넘을 때만 걸린다.
#   Dirt에서는 0.1/초 vs 재생 20/초라 아예 안 걸린다.
#   이걸 총량 제약으로 모델링하면 "스태미나가 병목"이라는 틀린 답이 나온다.
STAMINA_REGEN_PER_SEC = 20.0
STAMINA_REGEN_DELAY = 3.0

# ──────────────────────────────────────────────────────────────────────────
# DIALS — 아직 실측이 없는 추정값. 여기 있는 것만이 추측이다.
#
# 이 값들을 바꿔도 모델의 *구조*(누가 병목인가)는 안 바뀌고 크기만 바뀐다.
# 그래서 지금 틀려도 트리 합성의 뼈대를 세우는 데는 지장이 없다.
# ──────────────────────────────────────────────────────────────────────────
DIALS = {
    # 파기 1회가 실제로 지우는 픽셀 수.
    # miningRange(기본 0.5 유닛 = 50px) 원의 면적 pi*r^2 = 7854px를 그대로 쓰면
    # 한 다이브에 수백 개가 나와 비현실적이다. 실제로는 이미 판 자리를 다시
    # 긁는 비율이 높아 유효 면적이 훨씬 작다. 그 감쇠를 한 숫자로 접어 넣었다.
    # **실측(2026-08-21)**: pixels_dug / swings = 923. 추측이 아니다.
    # 이전 추측값 252는 3.7배 과소평가였다.
    # **2026-08-31 재측정: 923 -> 1649** (check_principles.py, pixels_dug/swings 49잠수 중앙).
    #
    # 923은 다이브 **한 판**의 광물 개수(5개)에서 역산한 값이었다. 그 역산에는
    # pickup_rate·광물 밀도·무게 한도가 전부 섞여 들어가 있어서, 그 중 무엇이
    # 틀려도 이 상수가 대신 흡수해 버린다. 지금은 pixels_dug와 swings를 둘 다
    # 로그로 받으므로 **나눗셈 한 번으로 직접 관측**된다 — 역산할 이유가 없다.
    #
    # 교훈: 여러 항이 곱해진 결과에서 한 항을 역산하면, 그 항이 다른 항의 오차까지
    # 떠안는다. 직접 관측할 수 있는 양이 생기면 역산값을 버린다.
    "dig_px_per_swing_at_base_range": 1649.0,

    # 초당 스윙 수 (**1배속 기준**).
    #
    # 상한은 miningCooldown(0.05초)이 아니라 **삽 풀차징 1초**다
    # (MiningStaminaTuning.DefaultShovelMaxChargeTime = 1f, 차징은 Time.deltaTime 누적).
    # 그래서 1배속에서 초당 1회가 천장이고, ToolChargeTimeReduce가 이걸 여는 레버다.
    # MiningCooldownMultiplier는 배선돼 있어도 이 천장에 안 닿아 의미가 없다.
    #
    # ⚠ 실측 2잠수는 **4배속**이라 게임시간 기준 0.29/초로 보였다. 그건 사람 손이
    #   실시간에 묶여 있기 때문이고(실시간 기준 1.06~1.16/초), 배속을 풀면
    #   차징 천장 1.0에 걸린다. 배속을 모르고 0.29를 쓰면 파기 속도를 3.4배
    #   과소평가한다 — 그래서 real_seconds / time_scale_avg를 남기게 했다.
    "swings_per_sec_base": 1.0,

    # 잠수 1회 길이(초). '하루 = 다이브 1회' 전제 아래의 체감 길이.
    "dive_seconds": 180.0,

    # 그중 순수 이동(하강·복귀)에 쓰는 비율. 나머지가 파기 시간이다.
    "travel_fraction": 0.35,

    # 위 travel_fraction이 성립하는 기준 이동속도 (DefalutPlayerSO.moveSpeed)
    "move_speed_base": 2.4,

    # 이동 시간 중 벽타기가 차지하는 비율.
    # 벽타기는 PlayerController가 매 FixedUpdate마다 StaminaCostPerSecond를 뽑아간다
    # (PlayerController.cs:446). 이게 파기에 쓸 스태미나를 먼저 갉아먹기 때문에,
    # WallClimbSpeed와 StaminaCostPerSecond 업그레이드가 "파기량"으로 환산된다.
    # 이 항이 없으면 그 두 업그레이드가 모델에서 아무 효과가 없다.
    #
    # ⚠ 이 값은 게임 수치가 강제한다. 스태미나 30 / 초당 3 이면
    #   벽타기는 통틀어 **10초**가 한계다(30÷3). travel 63초의 60%를 벽에
    #   매달리면 189 스태미나가 필요해 파기를 시작도 못 한다.
    #   0.1이면 6.3초 = 19 스태미나로, 이미 스태미나의 63%를 등반이 먹는다.
    #   즉 이 게임에서 벽타기는 원래 아주 비싼 행동이다 — 모델이 만든 게 아니라
    #   기본 스탯이 그렇게 정해져 있다.
    "climb_fraction_of_travel": 0.1,

    # 땅에 박힌 광물 중 실제로 주워 오는 비율(놓치는 것·안 줍는 것 제외).
    "pickup_rate": 0.8,

    # 파기 1회가 **광물을 떨어뜨리는 면적** ÷ **실제로 지우는 픽셀**.
    #
    # 게임은 판 자리 안의 광물만 주는 게 아니다. TerrainChunk.NotifyMineralsInDigRegion이
    # 파인 영역의 박스에 여유 0.3u를 더해 OverlapBox를 돌리고, 그 안에서 지지를 잃은
    # 광물이 굴러 나온다 — 즉 **터널 옆구리의 광물도 손에 들어온다.**
    #
    # 기하학적 상한은 크다: 박스 1.3x1.3u = 16,900px² vs 실제 지워지는 1,649px = 10배.
    # 다만 지지를 잃은 것만 떨어지므로 실효값은 그보다 훨씬 작다.
    #
    # ⚠ 이 값을 pickup_rate에 흡수시키지 말 것. 그렇게 하면 한 다이얼이 두 현상의
    #   오차를 같이 떠안아, 어느 쪽이 틀렸는지 영영 못 가린다
    #   (dig_px_per_swing이 923이던 시절이 정확히 그 상태였다 — charter §5).
    #   지금은 다른 항이 전부 직접 관측되므로 이 항만 남겨 따로 잰다.
    #   측정: check_principles.py 의 P0 `harvest_area_multiplier` 행.
    #
    # **실측(2026-08-31): 1.847** — 잠수 82회 중앙, 드리프트 0.87x로 안정적.
    # 기하 상한 10배에 한참 못 미치는 것이 정상이다: 박스 안에 있어도 지지를 잃은
    # 광물만 떨어진다.
    "harvest_area_multiplier": 1.847,

    # (rare_spawn_rate 다이얼은 2026-08-22 폐기 — 게임의 실제 깊이 곡선
    #  MineralDensity.DepthFactor를 Layer.mix_at이 그대로 쓰게 됐다.
    #  실측 "첫 다이브 전부 Common"도 이 곡선의 자연 귀결이다: 얕은 곳 Rare 계수 ~0)

    # 배터리(최대 스태미나)를 어디까지 쓰고 귀환하나.
    # 0까지 쓰면 **사망 — 광물·소모품 전부 소실**(GameOverHandler)이라
    # 플레이어는 복귀분을 남긴다.
    #
    # **2026-08-31 재측정: 0.30 -> 0.163** (귀환한 잠수 17건의 종료max/시작max 중앙).
    # 0.3은 잠수 2건에서 나온 값이었다. 표본이 늘자 절반으로 떨어졌다 —
    # 실제 플레이는 모델이 가정한 것보다 훨씬 아슬아슬하게 배터리를 쓴다.
    #
    # ⚠ 이 값은 트리의 성질이 아니라 **플레이어의 성질**이다. 공격적인 사람일수록
    #   낮다. 지금은 개발자 1인의 습관이므로, 다른 플레이어 로그가 들어오면
    #   중앙값이 다시 올라갈 수 있다. 그때는 그 값을 쓴다.
    "battery_safety_margin": 0.163,
}

# 과적 임계 비율 (MineralInventory.EncumbranceRatio = 0.8).
# 무게가 한도의 80%를 넘으면 이동·벽타기 0.5배 + 리젠 0.5배 —
# 복귀가 두 배로 느려지고 위험해진다. 그래서 플레이어는 임계 아래로 자기 제한한다
# (2026-08-22 사용자: "과적이 있으면 느려져서 올라오기 힘들어져").
# 모델에서는 무게 축이 한도가 아니라 **임계(80%)에서 걸리는** 것으로 다룬다.
ENCUMBRANCE_RATIO = 0.8

# 긴급탈출로 남는 광물 비율. 추측이 아니라 코드 상수다 —
# SaveManager.cs:307이 개수의 60%를 무작위로 버린다(Fisher-Yates).
# 무작위라 기댓값으로는 가치도 60% 잃는다.
ESCAPE_KEEP_RATIO = 0.4


# ==========================================================================
# 데이터 로딩 — 전부 실제 파일에서 읽는다
# ==========================================================================

class Layer:
    """한 지층. tileData.json + priceData.json에서 조립한다."""

    __slots__ = ("name", "start_depth", "resistance", "tier",
                 "avg_price", "avg_weight", "density_per_px", "rules", "density_mult")

    def __init__(self, tile, prices, global_density):
        self.name = tile["tileType"]
        # tileData.json의 startDepth는 **청크 인덱스**다(-20 = 청크 20 = 월드 200).
        # 광물 밴드 minDepth/maxDepth(20~39 등)와 같은 단위이고, 게임에서도
        # GetStaminaReductionAtWorldY가 worldY를 chunkY로 바꿔 조회한다.
        # 모델 내부는 월드 유닛으로 계산하므로 여기서 한 번만 환산한다.
        #
        # ⚠ 2026-08-31까지 이걸 월드 유닛으로 읽어 **층이 10배 얕은 곳에서
        #   시작하는 것으로** 계산했다. 그 탓에 면허 천장이 월드 100(청크 10)에서
        #   막혀, 청크 20~39에 있는 Ice층 광물이 영원히 도달 불가로 나왔다
        #   (티어 B의 P10이 MineralPrice_Topaz 등 4개를 '함정'으로 오진).
        self.start_depth = float(tile.get("startDepth", 0)) * UNITS_PER_CHUNK
        # 파기 1회(반경 1.0 기준)에 깎이는 최대 스태미나
        self.resistance = float(tile.get("maxStaminaReduction", 0.1))
        self.tier = int(tile.get("tier", 0))

        rules = tile.get("minerals") or []
        # 규칙 원본을 (단가, 무게, perChunk중앙, 밴드, Rare여부)로 들고 있는다.
        # 깊이별 스폰은 게임의 MineralDensity.DepthFactor 곡선을 그대로 쓴다 —
        # **Rare는 밴드에서 0->1로 늘고, Common은 1->0.5로 준다.**
        # (예전엔 rare_spawn_rate 고정값으로 뭉갰는데, 그 탓에
        #  "층 안에서 수입이 안 자란다"는 틀린 결론이 나왔다. 2026-08-22 정정)
        self.rules = []
        for r in rules:
            mid = r.get("mineralType")
            per = r.get("perChunk") or [0, 0]
            share = (per[0] + per[1]) / 2.0
            pw = prices.get(mid)
            if pw and share > 0:
                self.rules.append(dict(
                    id=mid, price=pw[0], weight=pw[1], share=share,
                    min_d=int(r.get("minDepth", 0)), max_d=int(r.get("maxDepth", 0)),
                    rare=str(r.get("rarity", "Common")).lower() != "common"))

        self.density_mult = global_density * float(tile.get("mineralDensity", 1.0))

        # 밴드 중간 지점 기준의 대략값 (표 출력·급조 비교용)
        mid_d = (min(r["min_d"] for r in self.rules) +
                 max(r["max_d"] for r in self.rules)) // 2 if self.rules else 0
        n, v, w = self.mix_at(mid_d)
        self.avg_price = v / n if n else 0.0
        self.avg_weight = w / n if n else 1.0
        self.density_per_px = n

    @staticmethod
    def depth_factor(depth, min_d, max_d, rare):
        """MineralDensity.DepthFactor 그대로. Rare 0->1, Common 1->0.5."""
        t = ((depth - min_d) / (max_d - min_d)) if max_d > min_d else 1.0
        t = 0.0 if t < 0 else (1.0 if t > 1 else t)
        return t if rare else 1.0 - 0.5 * t

    def mix_at(self, chunk_depth, price_bonus=None):
        """이 깊이의 픽셀당 (광물 수, 가치, 무게).

        ⚠ `chunk_depth`는 **청크 인덱스**다(월드 유닛 아님). 규칙의 minDepth/maxDepth가
        청크 단위이기 때문이다 — `MineralGenerator.cs:56`이
        `int currentDepth = Mathf.Abs(coord.y)`로 청크 Y를 그대로 넘긴다.

        2026-08-31까지 `income()`이 여기에 **월드 유닛**을 넘겨 10배 어긋나 있었다
        (1청크 = 10유닛). 그 탓에:
          · Rare 계수 t가 10배로 뛰어 희귀 광물을 10배 과대 예측했다
            (모델 15.7% vs 실측 1.7%)
          · Dirt 규칙의 밴드가 0~19'유닛'으로 읽혀 월드 깊이 19를 넘으면
            광물이 통째로 0이 됐다 (실제 밴드는 청크 0~19 = 월드 0~190)
        고친 뒤 구성이 실측과 거의 일치한다(GarbageBag 33% vs 38%, 희귀 1.8% vs 1.7%).

        price_bonus는 {광물id: +골드} — 광물별 판매가 업그레이드(MineralPriceUp).
        전역 배율이 아니라 광물별 가산이라, 그 광물이 실제로 나오는 깊이에서만
        수입이 오른다. 그래서 "지금 무엇을 캐고 있나"와 자연스럽게 엮인다.
        """
        n = v = w = 0.0
        for r in self.rules:
            if chunk_depth < r["min_d"] or chunk_depth > r["max_d"]:
                continue
            k = r["share"] * self.depth_factor(chunk_depth, r["min_d"], r["max_d"], r["rare"])
            k *= self.density_mult / CHUNK_PX
            price = r["price"] + (price_bonus.get(r["id"], 0) if price_bonus else 0)
            n += k
            v += k * price
            w += k * r["weight"]
        return n, v, w

    def __repr__(self):
        return "<Layer %s r=%.2f price=%.1f weight=%.2f>" % (
            self.name, self.resistance, self.avg_price, self.avg_weight)


def load_layers(tile_path=TILE_DATA, price_path=PRICE_DATA):
    tile = json.loads(Path(tile_path).read_text(encoding="utf-8"))
    price = json.loads(Path(price_path).read_text(encoding="utf-8"))
    prices = {m["id"]: (m["price"], m["weight"]) for m in price["minerals"]}
    g = float(tile.get("globalMineralDensity", 1.0))
    return [Layer(t, prices, g) for t in tile["tiles"]]


class Stats:
    """플레이어 스탯 벡터. 업그레이드가 이 값들을 움직인다."""

    __slots__ = ("max_stamina", "weight_limit", "slots", "mining_range",
                 "mining_speed", "stamina_cost_mult", "shovel_stamina_mult",
                 "move_speed", "mining_level", "sell_bonus",
                 "wall_climb_speed", "stamina_per_sec", "elevator_unlocked",
                 "mineral_price_bonus")

    def __init__(self, **kw):
        self.max_stamina = kw.get("max_stamina", 30.0)
        self.weight_limit = kw.get("weight_limit", 8.0)
        self.slots = kw.get("slots", 0)  # 미구현 — run_model 3)의 주석 참고
        self.mining_range = kw.get("mining_range", 0.5)
        self.mining_speed = kw.get("mining_speed", 1.0)
        self.stamina_cost_mult = kw.get("stamina_cost_mult", 1.0)
        self.shovel_stamina_mult = kw.get("shovel_stamina_mult", 1.0)
        self.move_speed = kw.get("move_speed", 2.4)
        self.mining_level = kw.get("mining_level", 0)
        self.sell_bonus = kw.get("sell_bonus", 1.0)
        # 벽타기: 속도가 빠를수록 같은 거리를 짧게 매달려 있어 스태미나를 덜 쓴다
        self.wall_climb_speed = kw.get("wall_climb_speed", 1.0)
        self.stamina_per_sec = kw.get("stamina_per_sec", 3.0)
        # 엘리베이터 업그레이드(Facility_Elevator 노드) 보유 여부.
        # 발견이 아니라 **구매**로 열린다(2026-08-22 사용자 확정).
        self.elevator_unlocked = kw.get("elevator_unlocked", False)
        # {광물id: +골드} — MineralPriceUp 업그레이드 누적분
        self.mineral_price_bonus = dict(kw.get("mineral_price_bonus", {}) or {})

    def copy(self, **kw):
        d = {s: getattr(self, s) for s in self.__slots__}
        d.update(kw)
        return Stats(**d)


def base_stats(path=PLAYER_SO):
    """DefalutPlayerSO.asset에서 시작 스탯을 읽는다. 없으면 코드 기본값."""
    kw = {}
    try:
        text = Path(path).read_text(encoding="utf-8", errors="replace")
    except OSError:
        return Stats()

    for key, field in (("maxStamina", "max_stamina"),
                       ("moveSpeed", "move_speed"),
                       ("miningRange", "mining_range"),
                       ("miningLevel", "mining_level"),
                       ("wallClimbingSpeed", "wall_climb_speed"),
                       ("staminaCostPerSecond", "stamina_per_sec")):
        for line in text.splitlines():
            s = line.strip()
            if s.startswith(key + ":"):
                try:
                    kw[field] = float(s.split(":", 1)[1])
                except ValueError:
                    pass
                break
    return Stats(**kw)


# ==========================================================================
# 런 모델 — 여기가 핵심
# ==========================================================================

class Result:
    __slots__ = ("gold", "bottleneck", "minerals", "swings", "detail")

    def __init__(self, gold, bottleneck, minerals, swings, detail):
        self.gold = gold
        self.bottleneck = bottleneck
        self.minerals = minerals
        self.swings = swings
        self.detail = detail

    def __repr__(self):
        return "<Result %.0fG bottleneck=%s minerals=%.1f>" % (
            self.gold, self.bottleneck, self.minerals)


# 삽 풀차징 1회가 깎는 **최대 스태미나** (MiningStaminaTuning.ShovelTerrainReductionPerCharge).
# 실측과 정확히 일치한다: 41스윙에 30 -> 9.62 (20.4 감소 = 0.497/스윙).
#
# ⚠ 삽은 비용이 **차징 비율**에만 걸리고 반경과 무관하다(설계 의도).
#   그래서 채굴 범위를 키우면 스윙당 픽셀만 늘고 비용은 그대로 —— 순수 이득이다.
#   곡괭이는 반대로 반경 기준이라 키울수록 비용도 오른다.
MAX_STAMINA_PER_SHOVEL_SWING = 0.5


def px_per_swing(stats):
    """파기 1회가 지우는 픽셀. 반경의 제곱에 비례한다."""
    return DIALS["dig_px_per_swing_at_base_range"] * (stats.mining_range / 0.5) ** 2


def battery_per_swing(stats):
    """스윙 1회가 깎는 최대 스태미나 = 이 게임의 '배터리 소모'."""
    return MAX_STAMINA_PER_SHOVEL_SWING * stats.shovel_stamina_mult


def layers_down_to(layers, depth):
    """깊이 0~depth 구간이 지나는 층들과 각 층의 픽셀 비중."""
    out = []
    for i, L in enumerate(layers):
        top = -L.start_depth
        bot = -layers[i + 1].start_depth if i + 1 < len(layers) else float("inf")
        lo, hi = max(top, 0.0), min(bot, depth)
        if hi > lo:
            out.append((L, hi - lo))
    return out


def reachable_depth(stats, layers):
    """채광 레벨이 허용하는 최대 깊이."""
    limit = 0.0
    for i, L in enumerate(layers):
        if stats.mining_level < L.tier:
            break
        limit = (-layers[i + 1].start_depth if i + 1 < len(layers)
                 else -L.start_depth + 40.0)
    return limit


# 엘리베이터 정류장 간격. ElevatorLayerCatalog가 지층마다 상층(startDepth)과
# 하층(두께 절반)을 만들어서 0/-10/-20/... 로 10 단위가 된다.
ELEVATOR_STOP_INTERVAL = 10.0


def elevator_start(stats, reached_depth, max_allowed):
    """다음 판의 시작 깊이.

    엘리베이터는 발견이 아니라 **업그레이드 구매**로 열린다(2026-08-22 확정).
    안 샀으면 매 판 지표면(0)에서 다시 판다 — 실측 10일 회차가 그랬다.
    샀으면 지난 판까지 도달한 깊이 아래의 가장 가까운 정류장에서 시작하고
    거기로 돌아온다. 배터리를 재하강에 쓰지 않게 되는 것이 이 업그레이드의 가치다.

    ⚠ 2026-08-22 현재 게임 데이터의 프리팹 unlockNodeId가 전부 비어 있어
      실제로는 업그레이드 없이 열린다(배선 구멍). 모델은 의도를 따른다.
    """
    if not stats.elevator_unlocked:
        return 0.0
    stop = math.floor(max(reached_depth, 0.0) / ELEVATOR_STOP_INTERVAL) * ELEVATOR_STOP_INTERVAL
    return min(stop, max_allowed)


def income(stats, layers, time_budget=None, start_depth=0.0):
    """잠수 1회 수입과 병목.

    **한 판을 끝내는 것은 시간이 아니라 최대 스태미나다.**
    삽질 1회가 최대치를 0.5씩 영구히 깎고(수면으로 회복), 그게 바닥나면
    더 못 판다. A Game About Digging A Hole의 배터리와 같은 자리다.

    현재 스태미나는 재생 20/초라 순식간에 다시 차므로 예산이 아니다 —
    실측 stamina_pct가 늘 1.00인 이유가 그것이고, 비율만 보면
    "스태미나는 병목이 아니다"라는 정반대 결론에 빠진다.
    절대값(stamina_left)이 30 -> 9.6으로 무너지고 있었다.

      배터리(최대 스태미나)  -> 스윙 수  -> 픽셀 -> 깊이 -> 광물
      무게 한도              -> 담아 오는 양
      채광 면허              -> 갈 수 있는 깊이의 천장
      실시간                 -> 사람이 앉아 있는 시간 (부차적 제약)
    """
    cost = battery_per_swing(stats)
    if cost <= 0:
        return Result(0.0, "invalid", 0.0, 0.0, {})

    # ── 1) 배터리가 허락하는 스윙 수 — 안전 마진을 뺀 가용분만.
    #    끝까지 쓰면 죽어서 전부 잃으므로(사망 페널티) 아무도 끝까지 안 쓴다.
    usable = stats.max_stamina * (1.0 - DIALS["battery_safety_margin"])
    swings_battery = usable / cost

    # ── 2) 실시간 제약 (있으면). 배속 없는 1배속 기준 초당 스윙. ────────
    swings_time = math.inf
    if time_budget:
        swings_time = time_budget * DIALS["swings_per_sec_base"] * stats.mining_speed

    swings = min(swings_battery, swings_time)
    dig_limit = "battery" if swings_battery <= swings_time else "time"

    # ── 3) 그만큼 파면 얼마나 내려가나 ──────────────────────────────────
    pixels = swings * px_per_swing(stats)
    dug_depth = pixels / PX_PER_DEPTH

    # 엘리베이터로 start_depth에서 시작한다 — 거기까지는 다시 안 판다.
    #
    # 단, 면허 천장에 닿으면 시작점을 **한 발 물린다**: 가장 깊은 정류장에서
    # 시작하면 팔 새 땅이 없으므로, 실제 플레이어는 배터리만큼 팔 수 있는
    # 위쪽 정류장을 고른다. 이게 없으면 천장 도달 순간 수입이 0으로 떨어져
    # "면허 직전 구간"의 가격이 전부 무너진다.
    max_d = reachable_depth(stats, layers)
    start = min(max(start_depth, 0.0), max_d)
    if start + dug_depth > max_d:
        start = max(0.0, max_d - dug_depth)
    depth = start + dug_depth
    gated = depth > max_d
    if gated:
        depth = max_d
        pixels = max(depth - start, 0.0) * PX_PER_DEPTH
    if depth - start <= 0:
        return Result(0.0, "mining_level", 0.0, 0.0, {"reachable": max_d})

    # ── 4) 그 구간에서 나오는 광물 — 깊이 1유닛씩 수치 적분.
    #    Rare가 밴드 하단으로 갈수록 늘어나므로(0->1) 깊이마다 구성이 다르다.
    #    광물은 **이번에 판 구간**(start ~ depth)에서만 나온다.
    minerals = value = weight = 0.0
    d0 = start
    while d0 < depth - 1e-9:
        step = min(1.0, depth - d0)
        mid = d0 + step / 2.0
        for L in layers_down_to(layers, depth):
            pass  # (구간 판정은 mix_at의 밴드 검사에 맡긴다)
        n = v = w = 0.0
        chunk_mid = mid / UNITS_PER_CHUNK      # 월드 유닛 -> 청크 인덱스
        for L in layers:
            ln, lv, lw = L.mix_at(chunk_mid, stats.mineral_price_bonus)
            n += ln; v += lv; w += lw
        px = PX_PER_DEPTH * step
        minerals += n * px
        value += v * px
        weight += w * px
        d0 += step
    # 수확 계수 = 줍는 비율 x 수확 면적 배율.
    # 둘을 곱으로 분리해 두는 이유는 DIALS["harvest_area_multiplier"] 주석 참고 —
    # 하나로 합치면 어느 쪽이 틀렸는지 못 가린다.
    harvest = DIALS["pickup_rate"] * DIALS["harvest_area_multiplier"]
    minerals *= harvest
    value *= harvest
    weight *= harvest
    avg_price = value / minerals if minerals > 0 else 0.0
    avg_weight = weight / minerals if minerals > 0 else 1.0

    # ── 5) 담을 수 있는 만큼만 — 한도가 아니라 과적 임계(80%)까지.
    #    임계를 넘으면 복귀 속도·리젠이 절반이라 실질적으로 여기가 상한이다.
    comfortable = stats.weight_limit * ENCUMBRANCE_RATIO
    by_weight = comfortable / avg_weight if avg_weight > 0 else math.inf
    carried = min(minerals, by_weight)

    bn = "weight" if carried < minerals else ("mining_level" if gated else dig_limit)
    gold = carried * avg_price * stats.sell_bonus

    return Result(gold, bn, carried, swings, {
        "depth": depth, "start_depth": start, "dug_depth": depth - start,
        "max_depth_allowed": max_d, "gated": gated,
        "pixels": pixels, "battery_per_swing": cost,
        "swings_battery": swings_battery, "swings_time": swings_time,
        "minerals": minerals, "by_weight": by_weight,
        "avg_price": avg_price, "avg_weight": avg_weight,
        "real_seconds": swings / (DIALS["swings_per_sec_base"] * stats.mining_speed),
    })


def bottleneck(stats, layers, time_budget=None):
    return income(stats, layers, time_budget).bottleneck


def print_layers(layers):
    print("지층 (tileData.json + priceData.json)")
    print("  %-14s %6s %8s %8s %10s %10s" %
          ("층", "저항", "평균가", "평균무게", "가치/무게", "광물/청크"))
    for L in layers:
        print("  %-14s %6.2f %8.1f %8.2f %10.1f %10.1f" %
              (L.name, L.resistance, L.avg_price, L.avg_weight,
               L.avg_price / L.avg_weight if L.avg_weight else 0,
               L.density_per_px * CHUNK_PX))


def print_income(stats, layers, time_budget=None):
    r = income(stats, layers, time_budget)
    d = r.detail
    print("\n잠수 1회 (배터리=최대스태미나 %.0f / 무게한도 %.0f / 채광레벨 %d)"
          % (stats.max_stamina, stats.weight_limit, stats.mining_level))
    if "depth" not in d:
        print("  %s 에 막힘" % r.bottleneck)
        return
    print("  스윙당 배터리 %6.2f   -> 배터리로 %.0f 스윙" % (d["battery_per_swing"], d["swings_battery"]))
    if d["swings_time"] != float("inf"):
        print("  시간으로       %.0f 스윙 (예산 %.0f초)" % (d["swings_time"], time_budget))
    print("  -> 실제 스윙  %6.0f   파기 %.0f px   깊이 %.1f%s"
          % (r.swings, d["pixels"], d["depth"], "  (면허 천장에 걸림)" if d["gated"] else ""))
    print("  캔 광물       %6.1f개  (무게로는 %.1f개까지)" % (d["minerals"], d["by_weight"]))
    print("  평균단가      %6.1fG  평균무게 %.2fkg" % (d["avg_price"], d["avg_weight"]))
    print("  실제 소요     %6.0f초 (1배속 기준)" % d["real_seconds"])
    print("  ---------------------------------------------")
    print("  수입          %6.0fG   병목: %s" % (r.gold, r.bottleneck))


def main():
    ap = argparse.ArgumentParser(description="잠수 1회 수입 모델 (깊이 기반)")
    ap.add_argument("--time", type=float, default=None, help="하루 시간 예산(초)")
    ap.add_argument("--mining-level", type=int, default=None)
    ap.add_argument("--weight", type=float, default=None)
    a = ap.parse_args()

    layers = load_layers()
    stats = base_stats()
    if a.mining_level is not None:
        stats = stats.copy(mining_level=a.mining_level)
    if a.weight is not None:
        stats = stats.copy(weight_limit=a.weight)

    print_layers(layers)
    print_income(stats, layers, a.time)
    return 0


if __name__ == "__main__":
    sys.exit(main())
