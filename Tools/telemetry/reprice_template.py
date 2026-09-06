#!/usr/bin/env python3
"""사람이 짰던 이전 트리 T0 구성에 배터리 모델로 가격을 다시 매긴다.

⚠ 이제 트리의 정본은 이 파일이 아니다 (2026-08-26)
--------------------------------------------------
배치·연결·가격·노드는 **Tools/upgrade_tree_editor.html**에서 편집하고
UpgradeTree.csv로 저장한다. 이 스크립트는 **처음부터 다시 짤 때만** 쓴다 —
`--write`는 손으로 그린 선·배치·가격을 전부 자동 규칙으로 되돌리므로
`--force`를 같이 줘야 실행된다.

인자 없이 돌리면 밸런스 리포트(수입 대비 가격·한계효용)만 출력한다.
그 리포트는 아래 PRICE_TABLE을 보므로, 편집기로 가격을 바꿨다면 먼저
`python Tools/telemetry/pull_prices.py --write`로 표를 맞춰야 거짓말을 안 한다.

왜 있나
-------
2026-08-22 결정: 노드 구성은 이전(수제) 트리의 초반부를 참고하고,
가격·리듬만 모델에서 뽑는다. 합성기가 만든 순수 자동 트리는 통행권·
편의성·풍미 노드(시야·낙하·낮잠)가 빠져 얇았다.

구조: **라인 4개** (2026-08-26 — 판매·편의를 곁가지에서 네 번째 라인으로 올렸다).
      2026-08-22에 외줄+곁가지 대신 3개로 갔고, 거기에 trade가 붙었다.
  dig(파기·도구) / body(몸·수납) / camp(시설·탐사)
각 라인 안에서만 선행이 이어지고, 면허가 네 라인의 끝을 AND로 모은다.
플레이어는 어느 라인을 먼저 밀지 고를 수 있다 — 병목이 순환하므로
결국 번갈아 사게 되고, 그게 목표하는 압박 루프다.

가격: 아래 TEMPLATE 순서 = 모델이 예상하는 구매 순서.
그 시점 잠수 1회 수입 x TREE_SHARE x margin (synth_tree와 동일).
모델 밖 노드(시야 등)도 사다리 위치가 가격을 정하므로 문제없다.

    python Tools/telemetry/reprice_template.py            # 리포트
    python Tools/telemetry/reprice_template.py --write    # UpgradeTree.csv 저장

표준 라이브러리만 쓴다.
"""

import argparse
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))

import run_model as RM
import synth_tree as ST

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):
        pass


def stat_fx(**kw):
    """모델이 아는 효과: Stats 필드를 이렇게 바꾼다."""
    return ("stat", kw)


def price_fx(mineral, plus):
    """광물별 판매가 +N골드 (MineralPriceUp). 모델 수입에 직접 반영된다.

    mineral에 여러 개를 주면(튜플) **한 노드가 그 광물들을 전부** 같은 값만큼 올린다.
    CSV의 targetMineral 칸에 ';'로 이어 적히고, UpgradeEffectSO가 그대로 받는다.
    """
    names = (mineral,) if isinstance(mineral, str) else tuple(mineral)
    return ("price", names, plus)


MODEL_ONLY = ("model",)      # 모델 전용 처리 (엘베)
FLAVOR = ("flavor",)         # 모델 밖 — 편의성·풍미. 수입 증가 0이 정상
LICENSE = ("license",)

# 라인 정의 — (이름, 화면 X, 가격 배율)
#
# 배율이 라인의 성격이다: 파기는 생계라 싸고, 시설은 큰 지출이다
# (이전 수제 트리도 시설이 비쌌다 — 지도 780·단말기 890·낮잠 950).
# 같은 높이의 노드가 라인마다 다른 값이 되어 밋밋함이 사라진다.
# 라인 간격. 350 -> 175로 좁혔다 (2026-08-24 사용자 요청 — 화면에서 세 줄이
# 너무 벌어져 시선이 흩어졌다). 노드 실폭이 70단위 남짓이라 175에서도 겹치지 않는다.
# (700은 2026-08-22에 이미 너무 넓다고 판정했다 — 되돌리지 말 것.)
#
# ⚠ 2026-08-24: 곡괭이 입수 **이후**의 가방·수납 노드(가방 확장 II~IV, 수납 주머니 I~II)는
#   body가 아니라 camp로 옮겼다(사용자 요청). 가운데 줄이 혼자 길어 트리가 세로로
#   늘어지던 것을 없애고, 얇던 camp 줄을 채운다. 가방 확장 I만 곡괭이 앞이라 body에 남는다.
#
# ⚠ 2026-08-26: 판매가·편의 노드를 곁가지(±350에 매달린 잎)에서 **네 번째 라인**으로
#   올렸다(사용자 요청). 곁가지는 왼쪽 4 / 오른쪽 7로 흩어져 있어 '줄'로 안 읽혔다.
#   trade는 합류점의 선행에도 들어간다 — 판매가를 사야 면허가 열린다(사용자 결정).
#
# ⚠ 2026-08-26: 레인을 **0 기준으로 재중심화**했다(사용자 요청). 네 라인이
#   -175/0/175/350일 때 스팬의 가운데가 87.5라, x=0에 놓이는 루트·합류점이
#   왼쪽으로 치우쳐 보이고 트리 왼쪽에 빈 여백이 남았다
#   (콘텐츠 폭은 maxAbsX 기준이라 0을 중심으로 잡힌다).
#   간격 175는 그대로 두고 통째로 -87.5 옮겼다 — 폭은 같고 루트·합류점이 한가운데다.
#   짝수 개 라인이라 가운데 라인은 없다: 루트·합류점은 안쪽 두 라인 **사이**에 선다.
LANES = {
    "dig":   ("파기·도구", -262.5, 0.85),
    "body":  ("몸·수납", -87.5, 1.0),
    "camp":  ("시설·탐사", 87.5, 1.35),
    "trade": ("판매·편의", 262.5, 1.35),
}

# 라인마다 구간 시작을 몇 칸 미룰지. 지금은 안 쓴다(전부 0) —
# 라인째로 밀면 트리가 그만큼 길어지기만 했다(2026-08-24 시도 후 철회).
LANE_ROW_OFFSET = {"dig": 0, "body": 0, "camp": 0, "trade": 0}

# ── 첫 구간(루트 ~ 첫 합류점)은 계단식으로 놓는다 (2026-08-24 사용자 요청) ──
# 같은 행에 여러 노드가 나란히 서는 게 빽빽해 보인다는 판단.
# **구매 순서(=가격 오름차순)대로** 한 칸씩 어긋나게 쌓아 계단을 만든다.
# ⚠ 칸 간격은 **화면 노드 높이보다 넉넉히 커야** 한다. 75로 뒀더니 화면에서
#   위아래 노드 사각형이 겹쳐(75 x graphScale 1.5 = 112 < 노드 높이 146),
#   루트에서 나가는 선이 꺾일 자리가 없어 모서리가 노드에 딱 붙었다(2026-08-24).
#   120이면 180 > 146 + 여유라 꺾임이 양쪽 사각형 밖에 놓인다.
# 칸 간격이 노드 높이를 넘으므로 같은 라인끼리도 **한 칸이면 충분**하다.
# (두 칸을 강제했더니 30->40은 한 칸, 40->50은 두 칸이 되어 가격 간격이
#  같은데 화면 간격만 벌어졌다 — 2026-08-24 사용자 지적.)
# 모델 가격을 사람이 덮어쓰는 자리. 여기 적힌 노드는 사다리 계산을 무시한다
# (2026-08-24 사용자 지정). 초반 몇 개의 체감을 손으로 잡을 때만 쓴다 —
# 많이 쌓이면 모델과 트리가 다시 어긋난다.
# 트리 가격표 — **여기 적힌 값이 정본이다** (2026-08-24 사용자 결정).
#
# 예전에는 모델(수입 x 트리몫)과 라인 램프가 값을 만들었는데, 한 노드를 손보면
# 같은 라인 뒤쪽이 통째로 딸려 움직여서 손으로 잡은 값이 자꾸 어긋났다.
# 이제 표에 있는 노드는 **모델을 아예 안 탄다** — 적힌 값 그대로 나간다.
#
# 표에 없는 노드(새로 추가한 것)만 모델이 값을 매기고, 그 값은 리포트에
# "(모델)"로 표시된다. 확정되면 이 표에 옮겨 적는다.
# ⚠ 최대 스태미나는 **값(+N)으로만** 올린다 (2026-08-24 사용자 결정).
#   퍼센트판(강인한 체력 x1.05/x1.08)은 같은 축에 두 종류가 섞여 있으면
#   "무엇을 사야 얼마나 오르는지"가 안 읽혀서 뺐다. 삽 크기와 같은 이유다.
PRICE_TABLE = {
    # ── T0 ──
    "MiningRange_T0_01": 20,               # 넓은 삽날 I
    "InventoryWeight_T0_01": 40,           # 가방 확장 I
    "Facility_Map_T0": 50,                 # 지도 장비 구입
    "ShovelStamina_T0_01": 30,             # 가벼운 삽질 I
    "RelicUnlock_ElevatorTracker_T0": 60,  # 엘리베이터 신호기 입수
    "MiningSpeed_T0_01": 50,               # 빠른 차징 I
    "MaxStamina_T0_01": 60,                # 지구력 강화 I
    "PickaxeUnlock_T0_01": 70,             # 곡괭이 입수
    "ClimbSpeed_T0_01": 80,                # 등반 요령 I
    "PickaxeStamina_T0_01": 80,            # 가벼운 곡괭이질 I
    "InventoryWeight_T0_02": 110,          # 가방 확장 II
    "InventorySlot_T0_01": 160,            # 수납 주머니 I
    "MineralPrice_Recycle": 180,           # 재활용 계약
    "MiningRange_T0_02": 100,              # 넓은 삽날 II
    "MiningSpeed_T0_02": 130,              # 빠른 차징 II
    "InventoryWeight_T0_03": 220,          # 가방 확장 III
    "ShovelStamina_T0_02": 200,            # 가벼운 삽질 II
    "JumpForce_T0_01": 120,                # 점프력 강화 I
    "MaxStamina_T0_02": 200,               # 지구력 강화 II
    "StaminaCost_T0_01": 250,              # 효율적인 호흡 I
    "MineralExtraDrop_T0_01": 280,         # 광물 탐지기 I
    "MineralPrice_Coal": 250,              # 석탄 등급 인증
    "PickaxeDamage_T0_01": 280,            # 곡괭이 위력 I
    "Facility_Computer_T0": 440,           # 단말기 개통
    "MiningRange_T0_03": 480,              # 넓은 삽날 III
    "MineralPrice_Copper": 700,            # 구리 정련 계약
    "RareMineral_T0_01": 650,              # 감별사의 눈 I
    "MineralPrice_Iron": 1050,             # 제철소 직거래
    "MiningSpeed_T0_03": 750,              # 빠른 차징 III
    "Nap_T0_01": 870,                      # 짧은 낮잠 I
    "InventoryWeight_T0_04": 1000,         # 가방 확장 IV
    "PickaxeStamina_T0_02": 1080,          # 가벼운 곡괭이질 II
    "ClimbSpeed_T0_02": 750,               # 등반 요령 II
    "MiningRange_T0_04": 1250,             # 넓은 삽날 IV
    "MiningLevel_T0_Final": 1400,          # 채광 면허 I
    # ── T1 ──
    "ShovelStamina_T1_01": 1300,           # 삽질 효율 I
    "InventoryWeight_T1_01": 1000,         # 가방 확장 V
    "MineralPrice_Fossil": 1600,           # 화석 감정
    "MaxStamina_T1_01": 1800,              # 지구력 강화 III
    "MiningRange_T1_01": 2000,             # 넓은 삽날 V
    "InventorySlot_T1_01": 1500,           # 수납 주머니 II
    "DrillCapacity_T1_01": 2700,           # 드릴 입수
    "MineralPrice_Silver": 2900,           # 은 정련 계약
    "DrillRegen_T1_01": 3300,              # 드릴 충전기 I
    "InventoryWeight_T1_02": 2900,         # 가방 확장 VI
    "RareMineral_T1_01": 3600,             # 감별사의 눈 II
    "ShovelStamina_T1_02": 4100,           # 삽질 효율 II
    "Facility_Coin_T1": 4500,              # 코인 거래 개통
    "MineralPrice_Meteorite": 5100,        # 운석 감정
    "StaminaCost_T1_01": 4900,             # 효율적인 호흡 II
    "Nap_T1_01": 5600,                     # 깊은 낮잠 II
    "MiningSpeed_T1_01": 6300,             # 빠른 차징 IV
    "MineralPrice_Topaz": 7000,            # 보석 세공 계약
    "MaxStamina_T1_02": 6100,      # 지구력 강화 IV
    "PickaxeStamina_T1_01": 7800,          # 가벼운 곡괭이질 III
    "InventoryWeight_T1_03": 7600,         # 가방 확장 VII
    "MineralExtraDrop_T1_01": 8700,        # 광물 탐지기 II
    "ClimbSpeed_T1_01": 9500,              # 등반 요령 III
    "MiningRange_T1_02": 4600,             # 넓은 삽날 VI
    "MiningRange_T1_03": 9000,             # 넓은 삽날 VII
    "MiningLevel_T1_Final": 12300,         # 채광 면허 II
}

# 지층별 가격 단위. 값이 커질수록 잔돈이 의미가 없어져 읽기만 나빠진다 —
# 2지층부터는 100원 단위로 **내림**한다 (2026-08-24 사용자 결정).
PRICE_STEP_BY_TIER = {0: 10, 1: 100}


def floor_price(value, tier):
    step = PRICE_STEP_BY_TIER.get(tier, 10)
    return max(step, int(value) // step * step)


STAIR_SEGMENTS = None    # None = 전 구간 계단
STAIR_DY = 120
STAIR_MIN_GAP = 1

# 곁가지가 척추에서 옆으로 벌어지는 거리. 바깥쪽으로만 뻗는다.
BRANCH_DX = 175
BRANCH_SIDE = {"dig": -1, "camp": 1}

# 곁가지를 받는 라인. **가운데(body)는 제외**한다 —
# 중앙은 합류점으로 모이는 선 여러 갈래가 지나는 자리라, 거기에 곁가지까지 붙으면
# 가로선이 겹쳐 쌓여 읽을 수 없게 된다(2026-08-22 UI 확인).
# body의 편의 노드는 곁가지 대신 척추에 남아 필수가 된다.
BRANCH_LANES = {"dig", "camp"}

# 같은 라인에서 다음 노드가 최소한 이만큼은 비싸진다.
#
# 가격의 1차 근거는 "그 시점 수입"인데, 수입을 올리는 노드가 다 사지고 나면
# 수입이 천장(T0는 ~114G)에 붙어 사다리가 평평해진다(70,70,70…).
# 위로 갈수록 비싸지는 건 트리의 기본 문법이므로, 수입이 멈춘 뒤에는
# 이 램프가 이어받는다 — 후반 노드가 한 다이브 이상 걸리게 되는 것도
# 면허 직전의 저축 구간으로서 자연스럽다.
LANE_RAMP = 1.25

# 합류점을 지난 뒤 노드들의 가격 하한 배수. 관문보다 싼 노드가 그 위에 있으면
# "관문을 넘었다"는 느낌이 사라진다.
GATE_FLOOR_RATIO = 1.1

# 면허는 트리에서 가장 비싼 노드여야 관문으로 느껴진다.
LICENSE_FLOOR_OVER_MAX = 1.3

# 합류점 — 네 라인이 여기서 모였다가 다시 갈라진다.
#
# 왜 이것들인가: 전부 **다음 단계를 여는** 노드다(도구·시스템·지층).
# 스탯 노드를 합류점으로 쓰면 "왜 저걸 사야 다음이 열리지?"가 설명되지 않는데,
# 통행권은 원래 관문이라 여러 방면의 준비를 요구하는 것이 자연스럽다.
#
# 합류가 없으면 한 라인만 파고들어 트리를 종주할 수 있어 마디가 사라진다.
# ⚠ 2026-08-24: '엘리베이터 가동'(Facility_Elevator_T0)을 합류점에서도 트리에서도 뺐다.
#   엘리베이터는 **처음 발견하면 그대로 작동**한다(업그레이드로 사는 게 아니다).
#   WorldInteractable.unlockNodeId의 툴팁 예시에만 이름이 남아 있고 실배선은 없다.
CONVERGENCE = [
    "PickaxeUnlock_T0_01",    # 1/3 — 돌을 캘 수 있게 (새 자원 경로)
    "Facility_Computer_T0",   # 2/3 — 주식 (새 시스템)
    "MiningLevel_T0_Final",   # 최종 — 다음 지층
    # ── T1 ──
    "DrillCapacity_T1_01",    # 드릴 = 새 도구·새 자원 경로 (T0의 곡괭이 자리)
    "Facility_Coin_T1",       # 코인 = 새 시스템 (T0의 단말기 자리)
    "MiningLevel_T1_Final",   # 최종 — 다음 지층
]

# 다음 지층(T1) 템플릿으로 미룬 T0 노드 — 2026-08-24 사용자 결정.
# 이 스크립트엔 아직 TEMPLATE_T1이 없어 지금은 그냥 빠져 있는 상태다.
# T1을 짤 때 여기서 가져갈 것: 깊은 낮잠 II(Nap_T0_02).
#
# 아예 뺀 노드 (2026-08-24) — 되살리려면 여기 적힌 값으로 TEMPLATE에 다시 넣는다:
#   수납 주머니 II  InventorySlot_T0_02     InventorySlotUp 1.0  (T1로 넘김)
#
# 방한 장비(EnvironmentResist)는 트리에서 아예 뺐다 (2026-08-24) —
# 환경 저항은 상점에서 방한 세트(Equip_Winter_*)를 사서 얻는다. 트리는 관여하지 않는다.
#   야간 시야 I/II  VisionRadius_T0_01·02   VisionRadiusUp 1.3 (pct)
#   탐사 반경 I/II  MapExplore_T0_01·02     MapExploreRadiusUp 2.0
#   팔 뻗기 I      ToolRange_T0_01         ToolRange 1.15 (pct)

# 곁가지로 뺄 노드 — **사람이 직접 고른다** (2026-08-24 사용자 결정).
#
# 이전에는 "kind == FLAVOR면 자동으로 곁가지"였는데, T0 노드 절반 가까이가
# FLAVOR라 15개가 통째로 옆으로 빠져 트리가 3줄이 아니라 가시덤불이 됐다.
# 기본형은 **순수 3줄**이고, 여기 적은 id만 옆으로 나간다. 비어 있으면 곁가지 0개.
#
# 여기 넣은 노드는 **안 사도 진행된다** — 라인의 다음 노드가 선행으로 요구하지 않는다.
# 넣지 말아야 하는 것: 합류점(CONVERGENCE)·루트, 그리고 가운데 라인(body) 노드
# (중앙은 합류선이 지나는 자리라 곁가지가 붙으면 가로선이 겹쳐 읽을 수 없다).
# 2026-08-26부터 **비어 있다**. 판매가·낮잠·유물 해금은 전부 trade 라인의 척추가 됐다
# (사용자 요청 — 곁가지로 좌우에 흩어져 있으면 '네 번째 줄'로 안 읽힌다).
#
# 곁가지 기계장치(BRANCH_DX/SIDE/LANES)는 남겨 둔다. 다시 옆으로 빼고 싶은 노드가
# 생기면 여기 id만 적으면 된다 — 규칙은 그대로다:
#   · 여기 넣은 노드는 **안 사도 진행된다**(라인의 다음 노드가 선행으로 안 건다)
#   · 합류점·루트와 가운데 라인(body)에는 넣지 않는다(합류선과 가로선이 겹친다)
BRANCH_IDS = set()

# 트리의 뿌리 — 여기서 네 라인이 **같은 높이로** 갈라진다.
# 넓은 삽날 I: 모델상 첫 구매 한계효용 1위이자, 이전 수제 트리의 첫 노드.
ROOT_ID = "MiningRange_T0_01"

# ── 이전 트리 T0 구성, 라인 4개로 재배치 ──────────────────────────────────
# 순서 = 모델이 예상하는 구매 순서 (라인 안 선행은 이 순서에서 유도된다).
# (id, 라인, 이름, effectType, effectValue, isPct, 모델 효과, 설명)
TEMPLATE_T0 = [
    ("MiningRange_T0_01", "dig", "넓은 삽날 I", "MiningRangeUp", 0.1, False,
     stat_fx(mining_range=lambda v: v + 0.1),
     "한 번에 파내는 범위가 +0.1 넓어집니다."),
    ("InventoryWeight_T0_01", "camp", "가방 확장 I", "InventoryWeightUp", 3.0, False,
     stat_fx(weight_limit=lambda v: v + 3.0),
     "가방 무게 한도가 +3 늘어납니다."),
    ("Facility_Map_T0", "camp", "지도 장비 구입", "None", 0, False,
     FLAVOR,
     "휴대용 지도 장비를 들여 미니맵과 전체 지도(M)를 볼 수 있게 됩니다."),
    ("ShovelStamina_T0_01", "dig", "가벼운 삽질 I", "ShovelStaminaReduce", -0.1, False,
     stat_fx(shovel_stamina_mult=lambda v: v - 0.1),
     "삽질 1회 비용이 0.05 줄어듭니다(기준 0.5)."),
    ("RelicUnlock_ElevatorTracker_T0", "trade", "엘리베이터 신호기 입수", "None", 0, False,
     FLAVOR,
     "유물 '엘리베이터 신호기'를 얻습니다. 엘리베이터가 지도에 표시됩니다."),
    # 이름은 '차징'이지만 effectType은 MiningSpeedMultiplier가 맞다 —
    # SapStrategy.cs:172가 차징 게이지를 StatType.MiningSpeed 배율로 채우므로
    # 이 효과가 곧 차징 시간 감소다. ToolChargeTimeReduce는 스탯 정의만 있고
    # 차징 코드에 연결돼 있지 않아 쓰면 죽은 노드가 된다(2026-08-22 확인).
    ("MiningSpeed_T0_01", "dig", "빠른 차징 I", "MiningSpeedMultiplier", 1.1, True,
     FLAVOR,       # 예산이 스윙 수라 수입엔 0 — 실시간 단축(편의성)
     "삽 차징이 10% 빨리 차오릅니다."),
    ("MaxStamina_T0_01", "body", "지구력 강화 I", "MaxStaminaUp", 10.0, False,
     stat_fx(max_stamina=lambda v: v + 10.0),
     "최대 스태미나가 +10 증가합니다."),
    ("PickaxeUnlock_T0_01", "dig", "곡괭이 입수", "None", 0, False,
     FLAVOR,       # 돌 -> 희귀 광물 경로. 모델에 돌 축이 아직 없음
     "곡괭이를 쓸 수 있게 됩니다. 단단한 돌을 캐 희귀 광물을 얻습니다."),
    ("ClimbSpeed_T0_01", "body", "등반 요령 I", "ClimbSpeedMultiplier", 1.10, True,
     FLAVOR,       # 복귀 편의성 (현재 스태미나 경로)
     "벽 타기 속도가 10% 증가합니다."),
    ("PickaxeStamina_T0_01", "dig", "가벼운 곡괭이질 I", "PickaxeStaminaReduce", -0.1, False,
     FLAVOR,
     "곡괭이질 1회 비용이 0.05 줄어듭니다(기준 0.5)."),
    ("InventoryWeight_T0_02", "camp", "가방 확장 II", "InventoryWeightUp", 4.0, False,
     stat_fx(weight_limit=lambda v: v + 4.0),
     "가방 무게 한도가 +4 늘어납니다."),
    ("InventorySlot_T0_01", "camp", "수납 주머니 I", "InventorySlotUp", 1.0, False,
     FLAVOR,       # 소모품 칸이라 광물 수입 모델 밖. 2026-08-22 배선 확인됨
     "소모품을 넣을 수 있는 칸이 1개 늘어납니다."),
    # 쓰레기봉지 + 페트병 + 고철을 한 노드로 묶었다 (2026-08-24 사용자 결정).
    # 전부 1층 초반 쓰레기라 따로 사게 하면 같은 결정을 세 번 시키는 꼴이었다.
    # 자리는 수납 주머니 I 옆 곁가지 — 이 줄의 위치가 곧 붙는 자리다.
    ("MineralPrice_Recycle", "trade", "재활용 계약", "MineralPriceUp", 10.0, False,
     price_fx(("GarbageBag", "PETBottle", "ScrapMetal"), 10),
     "쓰레기봉지·페트병·고철 판매가가 각각 +10골드 오릅니다."),
    # ── 여기까지가 첫 지층의 1/3 (2026-08-22 사용자 기준) ───────────────

    # ── 2/3 구간 — II 등급과 새 계열 ────────────────────────────────────
    ("MiningRange_T0_02", "dig", "넓은 삽날 II", "MiningRangeUp", 0.15, False,
     stat_fx(mining_range=lambda v: v + 0.15),
     "한 번에 파내는 범위가 +0.15 넓어집니다."),
    ("MiningSpeed_T0_02", "dig", "빠른 차징 II", "MiningSpeedMultiplier", 1.12, True,
     FLAVOR,
     "삽 차징이 12% 더 빨리 차오릅니다."),
    ("InventoryWeight_T0_03", "camp", "가방 확장 III", "InventoryWeightUp", 3.0, False,
     stat_fx(weight_limit=lambda v: v + 3.0),
     "가방 무게 한도가 +3 늘어납니다."),
    ("ShovelStamina_T0_02", "dig", "가벼운 삽질 II", "ShovelStaminaReduce", -0.1, False,
     stat_fx(shovel_stamina_mult=lambda v: v - 0.1),
     "삽질 1회 비용이 0.05 줄어듭니다(기준 0.5)."),
    ("JumpForce_T0_01", "body", "점프력 강화 I", "JumpForceMultiplier", 1.15, True,
     FLAVOR,       # 이동 편의. 예산이 스윙 수라 수입엔 0
     "점프력이 15% 증가합니다."),
    ("MaxStamina_T0_02", "body", "지구력 강화 II", "MaxStaminaUp", 10.0, False,
     stat_fx(max_stamina=lambda v: v + 10.0),
     "최대 스태미나가 +10 증가합니다."),
    ("StaminaCost_T0_01", "body", "효율적인 호흡 I", "StaminaCostMultiplier", 0.85, True,
     stat_fx(stamina_cost_mult=lambda v: v * 0.85),
     "행동 시 스태미나 소모가 15% 감소합니다."),
    ("MineralExtraDrop_T0_01", "camp", "광물 탐지기 I", "MineralExtraDropChance", 10.0, False,
     FLAVOR,
     "광물을 캘 때 추가로 하나 더 나올 확률이 10% 늘어납니다."),
    ("MineralPrice_Coal", "trade", "석탄 등급 인증", "MineralPriceUp", 15.0, False,
     price_fx("Coal", 15),
     "석탄 판매가가 +15골드 오릅니다."),
    ("PickaxeDamage_T0_01", "dig", "곡괭이 위력 I", "PickaxeDamageUp", 1.0, False,
     FLAVOR,
     "곡괭이의 돌 파괴력이 증가합니다."),
    ("Facility_Computer_T0", "camp", "단말기 개통", "None", 0, False,
     FLAVOR,
     "거래소 단말기가 켜져 주식을 거래할 수 있게 됩니다."),
    ("MiningRange_T0_03", "dig", "넓은 삽날 III", "MiningRangeUp", 0.2, False,
     stat_fx(mining_range=lambda v: v + 0.2),
     "한 번에 파내는 범위가 +0.2 넓어집니다."),
    ("MineralPrice_Copper", "trade", "구리 정련 계약", "MineralPriceUp", 20.0, False,
     price_fx("Copper", 20),
     "구리 판매가가 +20골드 오릅니다."),
    ("RareMineral_T0_01", "camp", "감별사의 눈 I", "RareMineralChance", 20.0, False,
     FLAVOR,       # 같은 후보 안에서 무엇이 뽑히는지만 바꾼다 — 모델 수입엔 아직 미반영
     "돌에서 비싼 광물이 나올 확률이 오릅니다."),
    ("MineralPrice_Iron", "trade", "제철소 직거래", "MineralPriceUp", 30.0, False,
     price_fx("Iron", 30),
     "철 판매가가 +30골드 오릅니다."),
    ("MiningSpeed_T0_03", "dig", "빠른 차징 III", "MiningSpeedMultiplier", 1.18, True,
     FLAVOR,
     "삽 차징이 18% 더 빨리 차오릅니다."),
    # 빠른 차징 II 옆 곁가지 (2026-08-24 사용자 지정). 곁가지는 바로 앞 척추 노드에 붙으므로
    # 이 줄의 위치가 곧 붙는 자리다 — 순서를 바꾸면 붙는 노드도 바뀐다.
    ("Nap_T0_01", "trade", "짧은 낮잠 I", "NapCount", 1.0, False,
     FLAVOR,
     "하루에 낮잠을 1회 잘 수 있습니다. 시세가 한 틱 흐릅니다."),
    ("InventoryWeight_T0_04", "camp", "가방 확장 IV", "InventoryWeightUp", 3.0, False,
     stat_fx(weight_limit=lambda v: v + 3.0),
     "가방 무게 한도가 +3 늘어납니다."),
    ("PickaxeStamina_T0_02", "dig", "가벼운 곡괭이질 II", "PickaxeStaminaReduce", -0.1, False,
     FLAVOR,
     "곡괭이질 1회 비용이 0.05 줄어듭니다(기준 0.5)."),
    ("ClimbSpeed_T0_02", "body", "등반 요령 II", "ClimbSpeedMultiplier", 1.15, True,
     FLAVOR,
     "벽 타기 속도가 15% 더 증가합니다."),
    ("MiningRange_T0_04", "dig", "넓은 삽날 IV", "MiningRangeUp", 0.3, False,
     stat_fx(mining_range=lambda v: v + 0.3),
     "한 번에 파내는 범위가 +0.3 넓어집니다."),
    ("MiningLevel_T0_Final", "license", "채광 면허 I", "MiningLevel", 1.0, False,
     LICENSE,
     "얼음 지층을 팔 수 있는 면허입니다."),
]


# ── 2지층(얼음) 구성 ──────────────────────────────────────────────────────
# T0와 같은 규칙: 순서 = 예상 구매 순서, 라인 안에서만 선행이 이어진다.
# 통행권은 드릴 입수(중간)와 채광 면허 II(끝) 둘.
# 판매가 노드는 2단계 광물(운석·화석·은·토파즈)을 겨냥하고 전부 곁가지다.
TEMPLATE_T1 = [
    # ── 드릴 입수 전 ──
    ("ShovelStamina_T1_01", "dig", "삽질 효율 I", "ShovelStaminaReduce", -0.1, False,
     stat_fx(shovel_stamina_mult=lambda v: v - 0.1),
     "삽질 1회 비용이 0.05 줄어듭니다(기준 0.5)."),
    ("InventoryWeight_T1_01", "body", "가방 확장 V", "InventoryWeightUp", 4.0, False,
     stat_fx(weight_limit=lambda v: v + 4.0),
     "가방 무게 한도가 +4 늘어납니다."),
    ("MineralPrice_Fossil", "trade", "화석 감정", "MineralPriceUp", 20.0, False,
     price_fx("Fossil", 20),
     "화석 판매가가 +20골드 오릅니다."),
    ("MaxStamina_T1_01", "body", "지구력 강화 III", "MaxStaminaUp", 10.0, False,
     stat_fx(max_stamina=lambda v: v + 10.0),
     "최대 스태미나가 +10 증가합니다."),
    ("MiningRange_T1_01", "dig", "넓은 삽날 V", "MiningRangeUp", 0.12, False,
     stat_fx(mining_range=lambda v: v + 0.12),
     "한 번에 파내는 범위가 +0.12 넓어집니다."),
    ("InventorySlot_T1_01", "camp", "수납 주머니 II", "InventorySlotUp", 1.0, False,
     FLAVOR,
     "소모품을 넣을 수 있는 칸이 1개 늘어납니다."),
    # 통행권 — 드릴. T0의 곡괭이 입수와 같은 자리(새 도구 = 새 자원 경로)
    ("DrillCapacity_T1_01", "dig", "드릴 입수", "DrillBatteryCapacity", 50.0, False,
     FLAVOR,       # 드릴은 스태미나가 아니라 배터리를 쓴다 — 모델 밖
     "드릴을 쓸 수 있게 됩니다. 최대 배터리 용량 +50."),

    # ── 드릴 입수 후 ──
    ("MineralPrice_Silver", "trade", "은 정련 계약", "MineralPriceUp", 25.0, False,
     price_fx("Silver", 25),
     "은 판매가가 +25골드 오릅니다."),
    ("DrillRegen_T1_01", "dig", "드릴 충전기 I", "DrillBatteryRegen", 2.0, False,
     FLAVOR,
     "드릴 배터리가 초당 +2 더 회복됩니다."),
    ("InventoryWeight_T1_02", "body", "가방 확장 VI", "InventoryWeightUp", 4.0, False,
     stat_fx(weight_limit=lambda v: v + 4.0),
     "가방 무게 한도가 +4 늘어납니다."),
    ("RareMineral_T1_01", "camp", "감별사의 눈 II", "RareMineralChance", 20.0, False,
     FLAVOR,
     "돌에서 비싼 광물이 나올 확률이 더 오릅니다."),
    ("ShovelStamina_T1_02", "dig", "삽질 효율 II", "ShovelStaminaReduce", -0.1, False,
     stat_fx(shovel_stamina_mult=lambda v: v - 0.1),
     "삽질 1회 비용이 0.05 줄어듭니다(기준 0.5)."),
    # 통행권 — 코인. 드릴 다음 관문이다 (2026-08-24 사용자 결정).
    # MarketSceneController.CoinUnlockNodeId 가 이 id를 그대로 본다 — 바꾸면 코인이 영영 잠긴다.
    ("Facility_Coin_T1", "camp", "코인 거래 개통", "None", 0, False,
     FLAVOR,       # 도박판이라 기대수익이 마이너스 — 모델 수입엔 0이 맞다
     "단말기에 코인 거래 계열이 열립니다. 단판 베팅으로 판돈을 걸 수 있게 됩니다."),
    ("MiningRange_T1_02", "dig", "넓은 삽날 VI", "MiningRangeUp", 0.15, False,
     stat_fx(mining_range=lambda v: v + 0.15),
     "한 번에 파내는 범위가 +0.15 넓어집니다."),
    ("MineralPrice_Meteorite", "trade", "운석 감정", "MineralPriceUp", 35.0, False,
     price_fx("Meteorite", 35),
     "운석 판매가가 +35골드 오릅니다."),
    ("StaminaCost_T1_01", "body", "효율적인 호흡 II", "StaminaCostMultiplier", 0.85, True,
     stat_fx(stamina_cost_mult=lambda v: v * 0.85),
     "행동 시 스태미나 소모가 15% 더 감소합니다."),
    ("Nap_T1_01", "trade", "깊은 낮잠 II", "NapCount", 1.0, False,
     FLAVOR,
     "하루에 낮잠을 한 번 더 잘 수 있습니다."),
    ("MiningSpeed_T1_01", "dig", "빠른 차징 IV", "MiningSpeedMultiplier", 1.12, True,
     FLAVOR,
     "삽 차징이 12% 더 빨리 차오릅니다."),
    ("MineralPrice_Topaz", "trade", "보석 세공 계약", "MineralPriceUp", 40.0, False,
     price_fx("Topaz", 40),
     "토파즈 판매가가 +40골드 오릅니다."),
    ("MaxStamina_T1_02", "body", "지구력 강화 IV", "MaxStaminaUp", 12.0, False,
     stat_fx(max_stamina=lambda v: v + 12.0),
     "최대 스태미나가 +12 증가합니다."),
    ("PickaxeStamina_T1_01", "dig", "가벼운 곡괭이질 III", "PickaxeStaminaReduce", -0.1, False,
     FLAVOR,
     "곡괭이질 1회 비용이 0.05 줄어듭니다(기준 0.5)."),
    ("InventoryWeight_T1_03", "body", "가방 확장 VII", "InventoryWeightUp", 4.0, False,
     stat_fx(weight_limit=lambda v: v + 4.0),
     "가방 무게 한도가 +4 늘어납니다."),
    ("MineralExtraDrop_T1_01", "camp", "광물 탐지기 II", "MineralExtraDropChance", 10.0, False,
     FLAVOR,
     "광물을 캘 때 추가로 하나 더 나올 확률이 10% 늘어납니다."),
    ("ClimbSpeed_T1_01", "body", "등반 요령 III", "ClimbSpeedMultiplier", 1.15, True,
     FLAVOR,
     "벽 타기 속도가 15% 더 증가합니다."),
    ("MiningRange_T1_03", "dig", "넓은 삽날 VII", "MiningRangeUp", 0.18, False,
     stat_fx(mining_range=lambda v: v + 0.18),
     "한 번에 파내는 범위가 +0.18 넓어집니다."),
    ("MiningLevel_T1_Final", "license", "채광 면허 II", "MiningLevel", 1.0, False,
     LICENSE,
     "용암 지층을 팔 수 있는 면허입니다."),
]

# 지층별 구성표. (tier, 행) 으로 펼쳐서 한 줄기로 굽는다.
TEMPLATES = [TEMPLATE_T0, TEMPLATE_T1]


def reprice(margin=ST.DEFAULT_MARGIN):
    layers = RM.load_layers()
    cur = RM.base_stats()
    reached = 0.0
    prev_price = {}    # lane -> 직전 가격 (램프 하한용)
    gate_floor = 0     # 직전 합류점 가격 x GATE_FLOOR_RATIO — 그 뒤 노드의 하한
    out = []

    flat_template = [(tier, row)
                     for tier, rows_ in enumerate(TEMPLATES) for row in rows_]
    for tier, (nid, lane, name, etype, evalue, pct, fx, desc) in flat_template:
        start = RM.elevator_start(cur, reached, RM.reachable_depth(cur, layers))
        r = RM.income(cur, layers, start_depth=start)
        income_now = r.gold

        kind = fx[0]
        if kind == "license":
            floor = max(prev_price.values(), default=0) * LICENSE_FLOOR_OVER_MAX
            price = floor_price(max(
                income_now * ST.TREE_SHARE * ST.LICENSE_MARGIN, floor), tier)
            price = PRICE_TABLE.get(nid, price)
        else:
            lane_mult = LANES[lane][2] if lane in LANES else 1.0
            raw = income_now * ST.TREE_SHARE * margin * lane_mult
            # 같은 라인의 직전 가격 x 램프를 하한으로 — 수입이 멈춰도 계속 오른다
            floor = prev_price.get(lane, 0) * LANE_RAMP
            price = floor_price(max(raw, floor, gate_floor), tier)
            price = PRICE_TABLE.get(nid, price)
            prev_price[lane] = price

        # 합류점(통행권)을 지나면 그 값이 이후 모든 노드의 바닥이 된다.
        # 관문보다 싼 노드가 그 위에 있으면 관문이 관문처럼 안 느껴진다
        # (2026-08-24 사용자 지적 — 단말기 350인데 그 위가 350·390이었다).
        if nid in CONVERGENCE and kind != "license":
            gate_floor = price * GATE_FLOOR_RATIO

        gain = 0.0
        if kind == "price":
            _, minerals, plus = fx
            bonus = dict(cur.mineral_price_bonus)
            for mineral in minerals:
                bonus[mineral] = bonus.get(mineral, 0) + plus
            new = cur.copy(mineral_price_bonus=bonus)
            gain = RM.income(new, layers, start_depth=start).gold - income_now
            cur = new
        elif kind == "stat":
            new = cur.copy(**{k: f(getattr(cur, k)) for k, f in fx[1].items()})
            gain = RM.income(new, layers, start_depth=start).gold - income_now
            cur = new
        elif kind == "model" and nid.startswith("Facility_Elevator"):
            cur = cur.copy(elevator_unlocked=True)
        elif kind == "license":
            cur = cur.copy(mining_level=cur.mining_level + 1)

        r2 = RM.income(cur, layers,
                       start_depth=RM.elevator_start(cur, reached,
                                                     RM.reachable_depth(cur, layers)))
        reached = max(reached, r2.detail.get("depth", reached))

        target = ";".join(fx[1]) if kind == "price" else ""
        out.append(dict(nid=nid, tier=tier, lane=lane, name=name, etype=etype, target=target,
                        evalue=evalue, pct=pct, kind=kind, price=price,
                        gain=gain, income=income_now,
                        depth=r2.detail.get("depth", 0),
                        bottleneck=r2.bottleneck, desc=desc))
    return out


OLD_PRICES = {   # 이전 수제 트리의 가격 (참고 표시용, 세션 기록에서)
    "MiningRange_T0_01": 60, "ShovelStamina_T0_01": 110, "MiningSpeed_T0_01": 170,
    "MaxStamina_T0_01": 230, "InventoryWeight_T0_01": 80, "MaxStaminaMultiplier_T0_01": 90,
    "PickaxeUnlock_T0_01": 180, "ClimbSpeed_T0_01": 80, "VisionRadius_T0_01": 590,
    "PickaxeStamina_T0_01": 80, "FallDamage_T0_01": 500,
    "RelicUnlock_ElevatorTracker_T0": 680, "Facility_Map_T0": 780,
    "Facility_Computer_T0": 890, "Nap_T0_01": 950, "MiningLevel_T0_Final": 1280,
}


def print_lanes(rows):
    print("라인 4개 구조  (구매 순서는 전체 사다리, 선행은 라인 안에서만)\n")
    root = next(r for r in rows if r["nid"] == ROOT_ID)
    print("루트: %s %dG — 여기서 네 라인이 갈라진다\n" % (root["name"], root["price"]))
    for lane_key, (lane_name, _x, mult) in LANES.items():
        lane_rows = [r for r in rows if r["lane"] == lane_key and r["nid"] != ROOT_ID]
        print("[%s]  (가격 x%.2f)" % (lane_name, mult))
        for r in lane_rows:
            old = OLD_PRICES.get(r["nid"])
            note = {"flavor": " (편의/풍미)", "model": " (엘베 램프)"}.get(r["kind"], "")
            print("   | %-24s %5dG  (이전 %s)  %+7.1fG%s"
                  % (r["name"], r["price"], old if old else "-", r["gain"], note))
        print()
    print("[합류점]  네 라인이 여기서 모였다가 다시 갈라진다")
    for r in rows:
        if r["nid"] in CONVERGENCE:
            print("   O== %-22s %5dG" % (r["name"], r["price"]))
    total = sum(r["price"] for r in rows)
    print("\nT0 합계 %dG   구매 순서 사다리: %s"
          % (total, " ".join(str(r["price"]) for r in rows)))


def is_branch(r):
    """곁가지(선택 노드)인가. BRANCH_IDS에 적은 것만 나간다."""
    if r["nid"] not in BRANCH_IDS:
        return False
    if r["nid"] in CONVERGENCE or r["nid"] == ROOT_ID:
        return False          # 합류점·루트는 필수라 곁가지가 될 수 없다
    if r["lane"] not in BRANCH_LANES:
        return False          # 가운데 라인은 곁가지를 받지 않는다
    return True


def to_csv_rows(rows):
    """다이아몬드 사슬 + 바깥 라인의 곁가지로 배치한다.

    루트 -> [네 라인 병렬] -> 합류점 -> [네 라인 병렬] -> ... -> 면허

    선이 꺾이지 않게 하는 규칙 (2026-08-24 사용자 요청):
      · 같은 라인의 노드는 **같은 x**에 쌓인다 -> 라인 안 연결선은 항상 수직 직선
      · 한 구간(합류점 사이)에서 네 라인이 **같은 행에서 시작하고 같은 행에서 끝난다**.
        라인마다 노드 수가 다르면 남는 여백을 가운데에 흩어 넣는다.
        -> 합류점으로 들어오는 양옆 선이 같은 y에서 꺾여 대칭이 되고,
           가운데 라인은 x가 같으니 그냥 위로 쭉 올라온다.

    그 밖의 규칙 (UI에서 어지러웠던 것을 고친 결과 — 2026-08-22):
      · 곁가지는 **부모와 같은 줄**에 옆으로 붙는다 -> 연결선이 직선이 된다
      · 곁가지는 **합류점·루트에 붙지 않는다** -> 중앙은 네 라인이 모이는 선만 지난다
      · 곁가지는 **항상 잎**이다 -> 그것을 사야 다음이 열리는 일이 없다
    """
    csv_rows = []

    def emit(r, x, yy, parents):
        csv_rows.append({
            "nodeId": r["nid"], "tier": r["tier"], "effectType": r["etype"],
            "effectValue": ("%g" % r["evalue"]),
            "isPercentage": "true" if r["pct"] else "false",
            "parentIds": ";".join(parents), "cost": r["price"],
            "uiX": x, "uiY": yy,
            "displayNameKey": r["name"], "descriptionKey": r["desc"],
            "locked": "false", "targetMineral": r.get("target", ""),
        })

    # ── 1) 구간으로 자른다: 루트 / [라인 노드들] 합류점 / ... ───────────────
    root = None
    segments = []           # [(lane -> [척추 r], lane -> {척추 nid: [곁가지 r]}, 합류점 r, 순서)]
    spine, branches = {}, {}
    last_spine = {}         # lane -> 그 라인의 마지막 척추 nid
    orphan_branches = {}    # lane -> 아직 붙을 척추가 없는 곁가지

    def new_segment():
        return ({k: [] for k in LANES}, {k: {} for k in LANES}, [])

    spine, branches, flat = new_segment()
    for r in rows:
        nid = r["nid"]
        if nid == ROOT_ID:
            root = r
            continue
        if nid in CONVERGENCE:
            segments.append((spine, branches, r, flat))
            spine, branches, flat = new_segment()
            last_spine = {}
            continue
        lane = r["lane"]
        if is_branch(r):
            if last_spine.get(lane):
                branches[lane].setdefault(last_spine[lane], []).append(r)
            else:
                # 구간 첫 척추가 아직 없다. 나오면 거기 붙인다.
                orphan_branches.setdefault(lane, []).append((branches[lane], r))
            continue
        spine[lane].append(r)
        flat.append(r)
        if lane in orphan_branches and orphan_branches[lane]:
            for tbl, br in orphan_branches.pop(lane):
                tbl.setdefault(nid, []).append(br)
        last_spine[lane] = nid
    if any(spine[k] for k in LANES):
        segments.append((spine, branches, None, flat))

    # ── 2) 배치 ────────────────────────────────────────────────────────────
    y = 150
    emit(root, 0, y, [])
    tips = {k: root["nid"] for k in LANES}
    taken = set()           # (x, y) — 곁가지 자리 충돌 방지

    for seg_i, (spine, branches, conv, flat) in enumerate(segments):
        counts = {k: len(spine[k]) for k in LANES}
        # 구간 길이는 "오프셋 + 노드 수"가 가장 큰 라인이 정한다.
        n = max(LANE_ROW_OFFSET.get(k, 0) + counts[k] for k in LANES)
        base = y

        # 한 척추 노드에 곁가지는 하나까지. 겹치면 그 라인의 다음 노드로 넘긴다.
        # (같은 자리에 둘을 쌓으면 아래로 밀려 내려가 연결선이 꺾인다 — 2026-08-24)
        for lane in LANES:
            order = [r["nid"] for r in spine[lane]]
            queue = [br for nid in order for br in branches[lane].get(nid, [])]
            if len(queue) > 1:
                # 원래 부모보다 앞으로는 가지 않고, 빈 척추 노드를 찾아 한 칸씩 채운다.
                first = {nid: i for i, nid in enumerate(order)}
                cursor = 0
                new_tbl = {}
                for nid in order:
                    for br in branches[lane].get(nid, []):
                        cursor = max(cursor, first[nid])
                        while cursor < len(order) and order[cursor] in new_tbl:
                            cursor += 1
                        target = order[min(cursor, len(order) - 1)]
                        new_tbl.setdefault(target, []).append(br)
                branches[lane] = new_tbl

        if (STAIR_SEGMENTS is None or seg_i in STAIR_SEGMENTS) and flat:
            # 계단식: 구매 순서대로 한 칸씩 올린다. 라인은 x만 정한다.
            lane_row = {}
            row = 0

            def place_stair(r, at_row):
                lane = r["lane"]
                lane_row[lane] = at_row
                ny = base + at_row * STAIR_DY
                emit(r, LANES[lane][1], ny, [tips[lane]] if tips.get(lane) else [])
                tips[lane] = r["nid"]
                for br in branches[lane].get(r["nid"], []):
                    sign = BRANCH_SIDE.get(lane, 1)
                    bx, by = LANES[lane][1] + BRANCH_DX * sign, ny
                    while (bx, by) in taken:
                        by += STAIR_DY
                    taken.add((bx, by))
                    emit(br, bx, by, [r["nid"]])

            # ⚠ 루트 아래 첫 행을 라인당 하나씩 나란히 세우는 예외가 잠깐 있었다
            #   (2026-08-26) — 뺐다. "몇 갈래로 갈라지는지"는 이제 **선이** 보여준다:
            #   UpgradeLaneRouter가 분기·합류마다 꺾임 높이를 하나로 공유해서
            #   한 줄로 나왔다 갈라진다. 높이를 데이터로 맞출 필요가 없어졌고,
            #   맞추면 **가격이 다른 노드가 같은 층에 서서** 계단 규칙이 깨진다
            #   (가벼운 삽질 I 30G과 엘리베이터 신호기 60G이 같은 줄에 섰다).
            pending = flat

            # 가격 오름차순 = 실제로 사게 되는 순서. **값이 같으면 같은 행**에 둔다
            # (2026-08-24 사용자 요청 — 같은 돈이면 같은 높이가 읽기 쉽다).
            # 라인 안 순서는 가격이 이미 오름차순이라 뒤집히지 않는다.
            groups = {}
            for i, r in enumerate(pending):
                groups.setdefault(r["price"], []).append((i, r))
            for price in sorted(groups):
                group = [r for _, r in sorted(groups[price])]
                # 같은 라인의 직전 노드와 최소 간격을 확보할 때까지 그룹째 내린다.
                row += 1
                for r in group:
                    prev = lane_row.get(r["lane"])
                    if prev is not None:
                        row = max(row, prev + STAIR_MIN_GAP)
                for r in group:
                    place_stair(r, row)
            y = base + max(lane_row.values()) * STAIR_DY + 150
            if conv is not None:
                emit(conv, 0, y, [tips[k] for k in LANES if tips.get(k)])
                for k in LANES:
                    tips[k] = conv["nid"]
            continue

        for lane in LANES:
            k = counts[lane]
            if k == 0:
                continue
            # 첫 노드는 (1 + 라인 오프셋)행, 마지막 노드는 n행. 나머지는 그 사이에 고르게.
            start_row = 1 + LANE_ROW_OFFSET.get(lane, 0)
            if k == 1:
                slots = [n]     # 합류를 맞추는 쪽이 우선이라 끝 행에 붙인다
            else:
                span = max(0, n - start_row)
                slots = [int(round(start_row + i * span / (k - 1))) for i in range(k)]
            lane_x = LANES[lane][1]
            prev = tips[lane]
            for r, slot in zip(spine[lane], slots):
                ny = base + slot * 150
                emit(r, lane_x, ny, [prev] if prev else [])
                prev = r["nid"]
                for br in branches[lane].get(r["nid"], []):
                    sign = BRANCH_SIDE.get(lane, 1)
                    bx, by = lane_x + BRANCH_DX * sign, ny
                    while (bx, by) in taken:
                        by += 150
                    taken.add((bx, by))
                    emit(br, bx, by, [r["nid"]])
            tips[lane] = prev

        y = base + (n + 1) * 150
        if conv is not None:
            emit(conv, 0, y, [tips[k] for k in LANES if tips.get(k)])
            for k in LANES:
                tips[k] = conv["nid"]

    return csv_rows


def main():
    ap = argparse.ArgumentParser(description="이전 T0 구성(라인 4개) + 배터리 모델 가격")
    # 2026-08-22 플레이 피드백: 첫 업그레이드 후 수입이 20 -> 60G로 뛰는데
    # 가격이 그대로라 하루 두세 개씩 사졌다. margin을 0.92 -> 1.0으로 올려
    # "하루 저축 = 노드 하나"에 더 가깝게 맞췄다.
    ap.add_argument("--margin", type=float, default=1.0)
    ap.add_argument("--write", action="store_true",
                    help="UpgradeTree.csv를 통째로 다시 굽는다 (--force 필요)")
    ap.add_argument("--force", action="store_true",
                    help="--write가 손으로 그린 선·배치·가격을 날린다는 것을 알고 있다")
    a = ap.parse_args()

    rows = reprice(a.margin)
    print_lanes(rows)

    if a.write and not a.force:
        print("\n[중단] --write는 UpgradeTree.csv를 자동 규칙으로 통째로 다시 굽는다.")
        print("       Tools/upgrade_tree_editor.html에서 그린 선·배치와 손으로 맞춘 가격이")
        print("       전부 사라진다. 처음부터 다시 짜는 게 맞다면 --force 를 같이 줄 것.")
        return 1

    if a.write:
        out = "Assets/GameData/UpgradeData/UpgradeTree.csv"
        csv_rows = to_csv_rows(rows)
        # 손으로 확정한 행(locked=true)은 덮지 않는다 — 합성기와 같은 규칙.
        # CSV 직접 수정이 평소 튜닝 루트이고, 이 도구는 초안 생성기일 뿐이다.
        csv_rows, n_locked = ST.merge_locked(csv_rows, out)
        ST.write_csv(csv_rows, out)
        print("\n저장: %s (%d행%s)"
              % (out, len(csv_rows),
                 ", locked 보존 %d행" % n_locked if n_locked else ""))
        print("Unity: Tools > Upgrade > Upgrade Tree Generator + Export Prices(값 보존) 실행")
        print("선·배치를 다시 손보려면 Tools/upgrade_tree_editor.html 로 이 CSV를 연다")
    return 0


if __name__ == "__main__":
    sys.exit(main())
