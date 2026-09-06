#!/usr/bin/env python3
"""업그레이드로 살 수 있는 것들의 목록. 트리 합성기의 재료다.

노드가 아니라 **재료**다
------------------------
노드는 "이 스탯을 이만큼 올려주고 얼마"라는 묶음일 뿐이다. 합성기가 병목을 보고
필요한 재료를 골라 가격을 붙이면 그게 노드가 된다.
그래서 여기에는 가격도, 선행도, 좌표도 없다 — 전부 합성 결과다.

축(axis)이 왜 필요한가
----------------------
런 모델을 돌려 보면 최대 스태미나·소모율·벽타기가 **전부 같은 +33.9G**로 나온다.
셋이 같은 제약(스태미나) 하나를 밀고 있기 때문이다.
"직전과 같은 것을 연속으로 사지 않는다"를 스탯 단위로 걸면 스태미나 계열을
돌아가며 사는 것이 순환처럼 보이지만 실제로는 같은 벽을 계속 미는 것이다.
그래서 순환 판정은 **축 단위**로 한다.

설계: Assets/Docs/superpowers/specs/2026-08-21-upgrade-tree-autogen-design.md §7
표준 라이브러리만 쓴다.
"""

# 축 — 런 모델의 min() 항 하나에 대응한다.
# 같은 축의 재료는 같은 병목을 민다.
AXIS_WEIGHT = "weight"
AXIS_STAMINA = "stamina"
AXIS_TIME = "time"
AXIS_YIELD = "yield"      # 스윙 1회의 산출 자체를 키우는 것
AXIS_SLOTS = "slots"
AXIS_GATE = "gate"        # 하드 게이트. 한계효용이 아니라 통행권이다


class Material:
    """업그레이드 재료 하나.

    apply(stats) -> 새 Stats. 원본은 건드리지 않는다.
    """

    __slots__ = ("key", "effect_type", "axis", "label", "field", "mode", "amount")

    def __init__(self, key, effect_type, axis, label, field, mode, amount):
        self.key = key
        self.effect_type = effect_type   # UpgradeEffectType 이름 (CSV에 그대로 나간다)
        self.axis = axis
        self.label = label
        self.field = field               # Stats의 어느 필드를 움직이나
        self.mode = mode                 # "add" | "mul"
        self.amount = amount

    def apply(self, stats):
        cur = getattr(stats, self.field)
        new = cur + self.amount if self.mode == "add" else cur * self.amount
        return stats.copy(**{self.field: new})

    @property
    def effect_value(self):
        """CSV의 effectValue 칸에 들어갈 값."""
        return self.amount

    @property
    def is_percentage(self):
        return self.mode == "mul"

    def __repr__(self):
        return "<%s %s %s%s>" % (
            self.key, self.axis,
            "+" if self.mode == "add" else "x", self.amount)


# ──────────────────────────────────────────────────────────────────────────
# 재료 목록
#
# effect_type은 UpgradeEffectType enum 이름 그대로다 — 오타는 CSV 리더가
# "모르는 effectType"으로 잡아낸다.
#
# amount는 **노드 1개가 주는 양**이다. 다단계 강화를 접었으므로(2026-08-21)
# 같은 재료를 여러 번 쓰려면 노드를 여러 개 만든다.
# ──────────────────────────────────────────────────────────────────────────
MATERIALS = [
    # ── 무게 ────────────────────────────────────────────────────────────
    Material("weight_small", "InventoryWeightUp", AXIS_WEIGHT,
             "가방 확장", "weight_limit", "add", 3.0),

    # ── 스태미나 ────────────────────────────────────────────────────────
    # 2026-08-21: 네 재료가 Ice(스태미나 병목)에서 모두 +180G 언저리가 되도록 역산했다.
    # 이전 값에서는 최대 스태미나(+10)가 나머지를 5.1배로 압도해, 합성기가
    # 그것만 4번 뽑고 나머지 3종을 죽은 재료로 만들었다.
    # 조정은 marginal.py --balance 로 편차를 보면서 한다.
    Material("stamina_max", "MaxStaminaUp", AXIS_STAMINA,
             "최대 스태미나", "max_stamina", "add", 6.0),
    Material("stamina_cost", "StaminaCostMultiplier", AXIS_STAMINA,
             "스태미나 소모 감소", "stamina_cost_mult", "mul", 0.83),
    Material("shovel_stamina", "ShovelStaminaReduce", AXIS_STAMINA,
             "삽질 효율", "shovel_stamina_mult", "mul", 0.64),
    Material("climb_speed", "ClimbSpeedMultiplier", AXIS_STAMINA,
             "벽타기 속도", "wall_climb_speed", "mul", 1.5),

    # ── 시간 ────────────────────────────────────────────────────────────
    Material("mining_speed", "MiningSpeedMultiplier", AXIS_TIME,
             "채굴 속도", "mining_speed", "mul", 1.12),
    Material("move_speed", "MoveSpeedMultiplier", AXIS_TIME,
             "이동 속도", "move_speed", "mul", 1.15),

    # ── 산출 ────────────────────────────────────────────────────────────
    # 범위는 면적(제곱)으로 들어가고 스태미나 비용은 반경에 선형이라,
    # 스태미나당 산출이 순증한다. 그래서 시간축이 아니라 산출축이다.
    Material("mining_range", "MiningRangeMultiplier", AXIS_YIELD,
             "채굴 범위", "mining_range", "mul", 1.12),

    # ── 게이트 ──────────────────────────────────────────────────────────
    # 한계효용으로 고르지 않는다. 다음 층으로 넘어가는 통행권이라
    # 합성기가 티어 경계에 직접 놓는다.
    Material("mining_level", "MiningLevel", AXIS_GATE,
             "채광 면허", "mining_level", "add", 1.0),
]

# 노드 id 접두사 — 기존 트리의 명명 규칙(PascalCase)을 따른다.
# 소문자 key를 그대로 쓰면 `weight_small_T0_01` 같은 id가 나와 기존 에셋·
# 세이브·QA 픽스처와 이름이 어긋난다. 같은 이름을 쓰면 픽스처가 그대로 산다.
NODE_PREFIX = {
    "weight_small":   "InventoryWeight",
    "stamina_max":    "MaxStamina",
    "stamina_cost":   "StaminaCost",
    "shovel_stamina": "ShovelStamina",
    "climb_speed":    "ClimbSpeed",
    "mining_speed":   "MiningSpeed",
    "move_speed":     "MoveSpeed",
    "mining_range":   "MiningRange",
    "mining_level":   "MiningLevel",
}


def node_prefix(material):
    return NODE_PREFIX.get(material.key, material.key)


BY_KEY = {m.key: m for m in MATERIALS}

# 한계효용으로 고를 수 있는 것만 (게이트 제외)
CHOOSABLE = [m for m in MATERIALS if m.axis != AXIS_GATE]


def by_axis(axis):
    return [m for m in MATERIALS if m.axis == axis]


if __name__ == "__main__":
    import sys
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):
        pass

    print("업그레이드 재료 %d종 (게이트 제외 %d종)\n" % (len(MATERIALS), len(CHOOSABLE)))
    cur = None
    for m in MATERIALS:
        if m.axis != cur:
            cur = m.axis
            print("[%s]" % cur)
        print("  %-16s %-26s %s%s  ->  %s" % (
            m.key, m.effect_type,
            "+" if m.mode == "add" else "x", m.amount, m.field))
