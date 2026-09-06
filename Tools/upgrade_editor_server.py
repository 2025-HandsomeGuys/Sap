# -*- coding: utf-8 -*-
"""업그레이드 트리 편집기를 띄우고, 저장을 받아 파일에 **바로** 쓴다.

왜 서버가 필요한가
------------------
편집기(upgrade_tree_editor.html)는 브라우저 한 덩어리다. 브라우저는 `file://`로
연 페이지에 디스크 쓰기를 안 준다 — 그래서 지금까지 저장은 '내려받기'로 떨어졌고,
받은 파일을 사람이 Assets/GameData/UpgradeData/ 에 옮겨 덮어써야 했다.
그 한 걸음이 빠지면 편집기와 원본이 조용히 갈라진다.

이 서버는 저장소를 그대로 서빙하면서 저장 창구 하나를 더 연다.
받는 것은 CSV 본문 하나뿐이고, **쓰는 자리는 서버가 정한다**(아래 CSV_PATH·JSON_PATH).
클라이언트가 경로를 불러 주지 않으므로 아무 데나 쓰게 만들 수 없다.

무엇을 쓰는가 — 네 군데
-----------------------
1. Assets/GameData/UpgradeData/UpgradeTree.csv — 트리의 단일 원본.
2. Assets/StreamingAssets/priceData.json 의 `upgradeNodes` 절 — 런타임 가격.
   PriceApplier가 이 절을 UpgradeTreeSO 위에 덮어쓴다(_Core/Data/PriceApplier.cs).
   즉 **가격은 유니티를 안 거치고 바로 먹는다.** 노드를 새로 넣거나 선을 고친
   구조 변경은 여전히 유니티의 UpgradeTreeGenerator를 한 번 돌려야 에셋에 반영된다.
3. Assets/GameData/UpgradeData/UpgradeTierBands.csv — 지층 띠를 가르는 선의 uiY.
   비어 있으면 예전처럼 자동(두 지층 사이 중간값)이다. 이건 에셋(UpgradeTreeSO)에
   실려 가므로 **가격과 달리 유니티 생성기를 한 번 돌려야** 게임에 보인다.
4. Assets/GameData/UpgradeData/UpgradeSeries.csv — 계열(넓은 삽날 I·II·III…) 규칙.
   게임은 이 파일을 안 읽는다. 편집기가 계열 노드의 이름·번호·효과값·설명을
   다시 구울 때만 쓰는 편집 전용 파일이다 — 구운 결과는 UpgradeTree.csv에 실린다.

JSON은 통째로 다시 굽지 않고 `upgradeNodes` 절의 글자만 갈아 끼운다.
priceData.json은 사람이 손으로 튜닝하는 파일이라(PriceDataExporter.cs 주석),
다른 절의 값도 서식도 한 글자도 건드리지 않는 편이 안전하다.

    python Tools/upgrade_editor_server.py            # 띄우고 브라우저를 연다
    python Tools/upgrade_editor_server.py --port 9000 --no-browser

표준 라이브러리만 쓴다. 127.0.0.1에만 붙는다.
"""

import argparse
import csv
import io
import json
import os
import shutil
import sys
import threading
import time
import webbrowser
from datetime import datetime
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CSV_PATH = ROOT / "Assets/GameData/UpgradeData/UpgradeTree.csv"
JSON_PATH = ROOT / "Assets/StreamingAssets/priceData.json"
BANDS_PATH = ROOT / "Assets/GameData/UpgradeData/UpgradeTierBands.csv"
SERIES_PATH = ROOT / "Assets/GameData/UpgradeData/UpgradeSeries.csv"
EDITOR = "/Tools/upgrade_tree_editor.html"

# 되돌릴 자리. Assets/ 밖이라 유니티가 긁어 가지 않는다.
BACKUP_DIR = ROOT / "Tools/.upgrade_editor_backups"
BACKUP_KEEP = 10

CRLF = chr(13) + chr(10)             # CSV 줄바꿈. 편집기가 굽는 것과 같게 맞춘다
SPECIAL_CHARS = (",", chr(34), chr(13), chr(10))   # 이게 들어 있으면 칸을 따옴표로 감싼다

MAX_BODY = 4 * 1024 * 1024          # CSV 본문 상한

# 노드 아이콘으로 고를 수 있는 것들. 유니티가 스프라이트로 읽는 확장자만.
IMAGE_EXT = (".png", ".jpg", ".jpeg", ".psd", ".tga")
IMAGE_TTL = 30.0                    # 훑는 데 ~150ms 걸린다 — 잠깐 재워 둔다
NODES_KEY = '"upgradeNodes"'


_images = {"at": -1e9, "list": []}


def list_images():
    """Assets/ 아래 그림들의 저장소 상대 경로. 편집기의 고르기 목록이 된다.

    편집기는 이 목록을 그대로 `<datalist>`에 붓고, 적힌 경로가 목록에 없으면
    검사창에서 경고한다 — 오타를 유니티까지 안 들고 가게.
    """
    now = time.monotonic()
    if now - _images["at"] < IMAGE_TTL and _images["list"]:
        return _images["list"]

    out = []
    base = ROOT / "Assets"
    for root, dirs, files in os.walk(str(base)):
        for f in files:
            if f.lower().endswith(IMAGE_EXT):
                out.append(rel(Path(root) / f))
    out.sort()
    _images["at"] = now
    _images["list"] = out
    return out


class SaveError(Exception):
    """저장을 거절한 이유. 이게 나면 아무 파일도 안 건드린 상태다."""


# ────────────────────────────── CSV ──────────────────────────────

def check_csv(text):
    """쓰기 전에 CSV를 뜯어본다. 원본 하나뿐인 파일이라 반쯤 맞는 건 안 받는다.

    돌려주는 것: [(nodeId, cost), ...] — 순서는 CSV 그대로.
    """
    if not text.strip():
        raise SaveError("CSV가 비었다")

    rows = list(csv.DictReader(io.StringIO(text, newline="")))
    if not rows:
        raise SaveError("행이 하나도 없다 (머리글만 있는가?)")

    head = rows[0].keys()
    for need in ("nodeId", "cost", "parentIds"):
        if need not in head:
            raise SaveError("머리글에 '%s' 열이 없다 — 열: %s" % (need, ", ".join(head)))

    out, seen = [], set()
    for i, r in enumerate(rows, start=2):          # 2 = 머리글 다음 줄
        nid = (r.get("nodeId") or "").strip()
        if not nid:
            raise SaveError("%d행: nodeId가 비었다" % i)
        if nid in seen:
            raise SaveError("%d행: nodeId '%s'가 두 번 나온다" % (i, nid))
        seen.add(nid)
        raw = (r.get("cost") or "").strip()
        try:
            cost = int(float(raw))
        except ValueError:
            raise SaveError("%d행 '%s': cost가 숫자가 아니다 (%r)" % (i, nid, raw))
        if cost < 0:
            raise SaveError("%d행 '%s': cost가 음수다 (%d)" % (i, nid, cost))
        out.append((nid, cost))

    for i, r in enumerate(rows, start=2):
        nid = (r.get("nodeId") or "").strip()
        for p in (r.get("parentIds") or "").split(";"):
            p = p.strip()
            if p and p not in seen:
                raise SaveError("%d행 '%s': 없는 부모 '%s'를 가리킨다" % (i, nid, p))
    return out


def backup(path):
    """덮어쓰기 전 원본을 한 벌 챙긴다. 최근 BACKUP_KEEP개만 남긴다."""
    if not path.exists():
        return None
    BACKUP_DIR.mkdir(parents=True, exist_ok=True)
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    dst = BACKUP_DIR / ("%s.%s%s" % (path.stem, stamp, path.suffix))
    shutil.copyfile(str(path), str(dst))
    old = sorted(BACKUP_DIR.glob("%s.*%s" % (path.stem, path.suffix)))
    for extra in old[:-BACKUP_KEEP]:
        try:
            extra.unlink()
        except OSError:
            pass
    return dst


def write_csv(text):
    """CSV를 그대로 쓴다 — 편집기가 이미 CRLF·BOM 없음으로 굽는다."""
    backup(CSV_PATH)
    CSV_PATH.parent.mkdir(parents=True, exist_ok=True)
    with io.open(str(CSV_PATH), "w", encoding="utf-8", newline="") as f:
        f.write(text)
    return CSV_PATH.stat().st_size


# ────────────────────────────── JSON ──────────────────────────────

def find_block(text, key):
    """`"key": [` 부터 짝이 맞는 `]`까지의 (여는 대괄호, 닫는 대괄호 다음) 자리."""
    at = text.find(key)
    if at < 0:
        raise SaveError("priceData.json에 %s 절이 없다" % key)
    open_at = text.find("[", at)
    if open_at < 0:
        raise SaveError("%s 뒤에 배열이 없다" % key)

    depth, i, in_str, esc = 0, open_at, False, False
    while i < len(text):
        c = text[i]
        if in_str:
            if esc:
                esc = False
            elif c == "\\":
                esc = True
            elif c == '"':
                in_str = False
        elif c == '"':
            in_str = True
        elif c == "[":
            depth += 1
        elif c == "]":
            depth -= 1
            if depth == 0:
                return open_at, i + 1
        i += 1
    raise SaveError("%s 절의 대괄호가 닫히지 않았다" % key)


def render_nodes(pairs, indent):
    """JsonUtility.ToJson(pretty)와 같은 모양으로 굽는다 — 들여쓰기 4칸.

    정렬도 PriceDataExporter.ExportNodes와 같게 맞춘다: 가격 오름차순,
    같으면 id 순(CompareOrdinal = 코드포인트 순 = 파이썬 기본 문자열 비교).
    """
    items = sorted(pairs, key=lambda p: (p[1], p[0]))
    pad, pad2, pad3 = " " * indent, " " * (indent + 4), " " * (indent + 8)
    if not items:
        return "[]"
    body = (",\n").join(
        '%s{\n%s"id": %s,\n%s"cost": %d\n%s}' % (pad2, pad3, json.dumps(i, ensure_ascii=False),
                                                 pad3, c, pad2)
        for i, c in items)
    return "[\n%s\n%s]" % (body, pad)


def update_json(pairs):
    """priceData.json의 upgradeNodes 절만 갈아 끼우고, 무엇이 바뀌었는지 돌려준다."""
    if not JSON_PATH.exists():
        return {"ok": False, "note": "%s 이 없어 건너뛰었다" % rel(JSON_PATH)}

    raw = io.open(str(JSON_PATH), encoding="utf-8-sig").read()
    lo, hi = find_block(raw, NODES_KEY)

    try:
        before = {e["id"]: e["cost"] for e in json.loads(raw[lo:hi])}
    except (ValueError, KeyError, TypeError) as e:
        raise SaveError("priceData.json의 upgradeNodes를 읽지 못했다: %s" % e)

    after = dict(pairs)
    changed = [{"id": i, "from": before[i], "to": after[i]}
               for i in after if i in before and before[i] != after[i]]
    added = [i for i in after if i not in before]
    removed = [i for i in before if i not in after]

    # 절 앞의 들여쓰기 = `"upgradeNodes"`가 놓인 칸 수.
    line_at = raw.rfind("\n", 0, raw.find(NODES_KEY)) + 1
    indent = raw.find(NODES_KEY) - line_at

    new = raw[:lo] + render_nodes(pairs, indent) + raw[hi:]
    touched = new != raw
    if touched:
        backup(JSON_PATH)
        with io.open(str(JSON_PATH), "w", encoding="utf-8", newline="") as f:
            f.write(new)
    return {"ok": True, "touched": touched, "nodes": len(pairs),
            "changed": sorted(changed, key=lambda c: c["id"]),
            "added": sorted(added), "removed": sorted(removed)}


def read_bands():
    """지층 -> 띠 윗변 uiY. 안 적힌 지층은 None(자동)으로 담는다."""
    out = {}
    if not BANDS_PATH.exists():
        return out
    text = io.open(str(BANDS_PATH), encoding="utf-8-sig", newline="").read()
    for r in csv.DictReader(io.StringIO(text, newline="")):
        raw = (r.get("tier") or "").strip()
        if not raw.lstrip("-").isdigit():
            continue
        top = (r.get("bandTop") or "").strip()
        out[int(raw)] = float(top) if top else None
    return out


def csv_tiers(text):
    """CSV에 실제로 있는 지층 번호들. 경계 파일이 이 목록을 따라간다."""
    out = set()
    for r in csv.DictReader(io.StringIO(text, newline="")):
        raw = (r.get("tier") or "").strip()
        if raw.lstrip("-").isdigit():
            out.add(int(raw))
    return out


def check_bands(raw):
    """편집기가 보낸 {지층: 값|null}을 뜯어본다. 값이 없는 지층 = 자동."""
    if raw is None:
        return None
    if not isinstance(raw, dict):
        raise SaveError("bands가 사전이 아니다")
    out = {}
    for k, v in raw.items():
        try:
            tier = int(k)
        except (TypeError, ValueError):
            raise SaveError("지층 번호가 숫자가 아니다 (%r)" % (k,))
        if tier < 0:
            raise SaveError("지층 번호가 음수다 (%d)" % tier)
        if v is None or v == "":
            out[tier] = None
            continue
        try:
            top = float(v)
        except (TypeError, ValueError):
            raise SaveError("지층 %d: 경계값이 숫자가 아니다 (%r)" % (tier, v))
        if top != top or top in (float("inf"), float("-inf")):
            raise SaveError("지층 %d: 경계값이 유한한 수가 아니다" % tier)
        out[tier] = top
    return out


def fmt_num(v):
    """4200.0이 아니라 4200으로 적는다 — CSV diff가 조용하게."""
    return str(int(v)) if float(v).is_integer() else repr(float(v))


def write_bands(bands, tiers):
    """지층 경계 CSV를 다시 굽고, 무엇이 바뀌었는지 돌려준다.

    줄은 **트리에 있는 지층 전부**를 적는다(값이 없으면 빈 칸 = 자동).
    빠뜨리면 파일만 봐서는 "자동"인지 "적다 만 것"인지 구분이 안 되고,
    나중에 지층을 하나 더 만들었을 때 여기 줄이 없어서 눈에 안 띈다.
    """
    if bands is None:
        return {"ok": True, "touched": False, "note": "보내오지 않아 손대지 않았다"}

    bands = dict(bands)
    for t in tiers:
        bands.setdefault(t, None)

    before = read_bands()
    lines = ["tier,bandTop"]
    for tier in sorted(bands):
        v = bands[tier]
        lines.append("%d,%s" % (tier, "" if v is None else fmt_num(v)))
    text = "\r\n".join(lines) + "\r\n"

    old = io.open(str(BANDS_PATH), encoding="utf-8-sig", newline="").read() \
        if BANDS_PATH.exists() else ""
    if text == old:
        return {"ok": True, "touched": False, "bands": bands}

    backup(BANDS_PATH)
    BANDS_PATH.parent.mkdir(parents=True, exist_ok=True)
    with io.open(str(BANDS_PATH), "w", encoding="utf-8", newline="") as f:
        f.write(text)

    changed = []
    for tier in sorted(bands):
        was, now = before.get(tier), bands[tier]
        if was != now:
            changed.append({"tier": tier,
                            "from": None if was is None else fmt_num(was),
                            "to": None if now is None else fmt_num(now)})
    return {"ok": True, "touched": True, "changed": changed}


# ───────────────────────────── 계열 ─────────────────────────────

SERIES_FIELDS = ("seriesKey", "nameBase", "renumberId", "steps",
                 "stepTail", "descTemplate", "exclude")


def read_series():
    """계열 규칙을 CSV에서 읽어 편집기가 쓸 사전 목록으로."""
    out = []
    if not SERIES_PATH.exists():
        return out
    text = io.open(str(SERIES_PATH), encoding="utf-8-sig", newline="").read()
    for r in csv.DictReader(io.StringIO(text, newline="")):
        key = (r.get("seriesKey") or "").strip()
        if not key:
            continue
        out.append({f: (r.get(f) or "").strip() for f in SERIES_FIELDS})
    return out


def check_series(raw):
    """편집기가 보낸 계열 목록을 뜯어본다. 안 보내왔으면 None(= 손대지 않는다).

    계열 파일은 게임이 안 읽으므로 트리 CSV만큼 조일 필요는 없다. 다만 여기서
    막지 않으면 편집기가 다음에 열 때 조용히 이상한 규칙으로 트리를 굽는다.
    """
    if raw is None:
        return None
    if not isinstance(raw, list):
        raise SaveError("series가 목록이 아니다")
    out, seen = [], set()
    for i, d in enumerate(raw, start=1):
        if not isinstance(d, dict):
            raise SaveError("%d번째 계열이 사전이 아니다" % i)
        key = str(d.get("seriesKey") or "").strip()
        if not key:
            raise SaveError("%d번째 계열: seriesKey가 비었다" % i)
        if not key.replace("_", "").isalnum():
            raise SaveError("계열 '%s': seriesKey는 nodeId 앞머리라 글자/숫자만 된다" % key)
        if key in seen:
            raise SaveError("계열 '%s'가 두 번 나온다" % key)
        seen.add(key)
        if not str(d.get("nameBase") or "").strip():
            raise SaveError("계열 '%s': nameBase가 비었다" % key)
        steps = [t for t in str(d.get("steps") or "").split(";") if t.strip()]
        if not steps:
            raise SaveError("계열 '%s': steps가 비었다 - 단계값이 최소 하나는 있어야 한다" % key)
        for t in steps:
            try:
                float(t)
            except ValueError:
                raise SaveError("계열 '%s': steps에 숫자가 아닌 값 (%r)" % (key, t))
        try:
            float(str(d.get("stepTail") or "0"))
        except ValueError:
            raise SaveError("계열 '%s': stepTail이 숫자가 아니다 (%r)"
                            % (key, d.get("stepTail")))
        out.append({
            "seriesKey": key,
            "nameBase": str(d.get("nameBase")).strip(),
            "renumberId": "false" if str(d.get("renumberId")).lower() == "false" else "true",
            "steps": ";".join(t.strip() for t in steps),
            "stepTail": str(d.get("stepTail") or "0").strip(),
            "descTemplate": str(d.get("descTemplate") or ""),
            "exclude": ";".join(t.strip() for t in
                                str(d.get("exclude") or "").split(";") if t.strip()),
        })
    return out


def csv_cell(v):
    """쉼표/따옴표/줄바꿈이 있으면 감싼다(RFC 4180). 설명 템플릿에 쉼표가 흔하다."""
    s = "" if v is None else str(v)
    if any(c in s for c in SPECIAL_CHARS):
        return '"%s"' % s.replace('"', '""')
    return s


def write_series(series):
    """계열 CSV를 다시 굽는다. 보내오지 않았으면 손대지 않는다."""
    if series is None:
        return {"ok": True, "touched": False, "note": "보내오지 않아 손대지 않았다"}

    before = {d["seriesKey"] for d in read_series()}
    lines = [",".join(SERIES_FIELDS)]
    for d in series:
        lines.append(",".join(csv_cell(d[f]) for f in SERIES_FIELDS))
    text = CRLF.join(lines) + CRLF

    old = ""
    if SERIES_PATH.exists():
        old = io.open(str(SERIES_PATH), encoding="utf-8-sig", newline="").read()
    if text == old:
        return {"ok": True, "touched": False, "count": len(series)}

    backup(SERIES_PATH)
    SERIES_PATH.parent.mkdir(parents=True, exist_ok=True)
    with io.open(str(SERIES_PATH), "w", encoding="utf-8", newline="") as f:
        f.write(text)

    now = {d["seriesKey"] for d in series}
    return {"ok": True, "touched": True, "count": len(series),
            "added": sorted(now - before), "removed": sorted(before - now)}


def rel(p):
    return str(p.relative_to(ROOT)).replace("\\", "/")


def safe_console():
    """윈도우 콘솔(cp949)이 못 찍는 글자 하나에 응답이 죽지 않게.

    실제로 그랬다: 저장은 다 끝났는데 결과를 찍다가 UnicodeEncodeError가 나서
    브라우저에는 'Failed to fetch'만 돌아갔다. 파일은 이미 바뀐 뒤라 제일 나쁜
    거짓말이다 — 사람은 저장이 안 된 줄 알고 다시 누른다.
    콘솔 인코딩은 그대로 두고 못 찍는 글자만 흘려보낸다. 아래 print들도
    아스키만 쓴다(브라우저에 돌아가는 보고문은 한글 그대로다).
    """
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(errors="replace")
        except (AttributeError, ValueError):
            pass


# ────────────────────────────── 서버 ──────────────────────────────

class Handler(SimpleHTTPRequestHandler):
    def __init__(self, *a, **kw):
        SimpleHTTPRequestHandler.__init__(self, *a, directory=str(ROOT), **kw)

    def end_headers(self):
        # 편집기를 고치고 새로고침하면 바로 보이게.
        self.send_header("Cache-Control", "no-store")
        SimpleHTTPRequestHandler.end_headers(self)

    def log_message(self, fmt, *args):
        if self.path.startswith("/api/"):
            sys.stderr.write("  %s %s\n" % (self.command, self.path))

    def reply(self, code, payload):
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path.split("?")[0] == "/api/images":
            imgs = list_images()
            return self.reply(200, {"ok": True, "images": imgs, "count": len(imgs)})
        if self.path.split("?")[0] == "/api/tree":
            if not CSV_PATH.exists():
                return self.reply(404, {"ok": False, "error": "%s 이 없다" % rel(CSV_PATH)})
            text = io.open(str(CSV_PATH), encoding="utf-8-sig", newline="").read()
            return self.reply(200, {"ok": True, "csv": text, "path": rel(CSV_PATH),
                                    "json": rel(JSON_PATH), "bandsPath": rel(BANDS_PATH),
                                    "bands": {str(k): v for k, v in read_bands().items()},
                                    "seriesPath": rel(SERIES_PATH),
                                    "series": read_series()})
        return SimpleHTTPRequestHandler.do_GET(self)

    def do_POST(self):
        if self.path.split("?")[0] != "/api/save":
            return self.reply(404, {"ok": False, "error": "그런 창구는 없다"})
        try:
            n = int(self.headers.get("Content-Length") or 0)
            if n <= 0 or n > MAX_BODY:
                raise SaveError("본문 크기가 이상하다 (%d바이트)" % n)
            body = json.loads(self.rfile.read(n).decode("utf-8"))
            text = body.get("csv")
            if not isinstance(text, str):
                raise SaveError("csv 본문이 없다")

            # 검사를 먼저 다 끝낸다 — 하나라도 걸리면 아무 파일도 안 건드린 상태다.
            pairs = check_csv(text)
            bands = check_bands(body.get("bands"))
            series = check_series(body.get("series"))

            size = write_csv(text)
            report = update_json(pairs)
            band_report = write_bands(bands, csv_tiers(text))
            series_report = write_series(series)
        except SaveError as e:
            return self.reply(400, {"ok": False, "error": str(e)})
        except Exception as e:                                    # noqa: BLE001
            return self.reply(500, {"ok": False, "error": "%s: %s" % (type(e).__name__, e)})

        print("  saved: %s (%d nodes, %d bytes) + %s %s" % (
            rel(CSV_PATH), len(pairs), size, rel(JSON_PATH),
            "updated" if report.get("touched") else "unchanged"))
        return self.reply(200, {"ok": True,
                                "csv": {"path": rel(CSV_PATH), "bytes": size, "nodes": len(pairs)},
                                "json": dict(report, path=rel(JSON_PATH)),
                                "bands": dict(band_report, path=rel(BANDS_PATH)),
                                "series": dict(series_report, path=rel(SERIES_PATH))})


def main():
    ap = argparse.ArgumentParser(description="업그레이드 트리 편집기 서버")
    ap.add_argument("--port", type=int, default=8788)
    ap.add_argument("--no-browser", action="store_true")
    args = ap.parse_args()
    safe_console()

    url = "http://127.0.0.1:%d%s" % (args.port, EDITOR)
    try:
        srv = ThreadingHTTPServer(("127.0.0.1", args.port), Handler)
    except OSError as e:
        sys.exit("port %d is taken: %s\nuse --port to pick another one." % (args.port, e))

    print("")
    print("  Upgrade Tree Editor  >>  %s" % url)
    print("  Save writes straight to:")
    print("    - %s" % rel(CSV_PATH))
    print("    - %s   (upgradeNodes section)" % rel(JSON_PATH))
    print("    - %s" % rel(BANDS_PATH))
    print("    - %s   (editor-only series rules)" % rel(SERIES_PATH))
    print("  Backups: %s/   (last %d kept)" % (rel(BACKUP_DIR), BACKUP_KEEP))
    print("  Closing this window stops the server. (Ctrl+C)")
    print("")

    if not args.no_browser:
        threading.Timer(0.4, lambda: webbrowser.open(url)).start()
    try:
        srv.serve_forever()
    except KeyboardInterrupt:
        print("\n  stopped.")
        srv.server_close()


if __name__ == "__main__":
    os.chdir(str(ROOT))
    main()
