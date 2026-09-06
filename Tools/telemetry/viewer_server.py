#!/usr/bin/env python3
"""텔레메트리 뷰어 + 플레이 일지 서버.

`viewer.bat` 이 띄운다. 하는 일 셋:

1. **정적 서빙** — `viewer.html` 을 http 오리진으로 연다(file: 오리진 문제 회피).
2. **롱폴링 tail** — 게임이 쓰는 중인 `*.jsonl` 을 이어읽어 브라우저에 밀어준다.
   브라우저가 요청 하나를 걸어두면 **서버가 붙잡고 있다가** 새 줄이 생기면 즉시 응답한다.
   3초 폴링 대신 이걸 쓰는 이유: 평소엔 왕복이 없고, 일차가 끝나는 순간엔 바로 간다.
3. **일지 저장** — `journal/<run_id>.json` 읽기/쓰기.

표준 라이브러리만 쓴다 — 아무 데서나 돌아야 한다.

사용법:
    python viewer_server.py [telemetry_dir] [--port 8777] [--no-open]

telemetry_dir 를 안 주면 AppData/LocalLow/<회사>/<제품>/telemetry 에서
가장 최근에 쓴 폴더를 자동으로 고른다.
"""

import argparse
import json
import os
import re
import sys
import threading
import time
import webbrowser
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlparse, parse_qs

# Windows 한국어 콘솔(cp949)은 em dash 등을 못 찍고 죽는다.
for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):
        pass

ROOT = Path(__file__).resolve().parent
JOURNAL_DIR = ROOT / "journal"

POLL_INTERVAL = 0.5        # 서버가 파일을 다시 보는 주기
LONG_POLL_TIMEOUT = 25.0   # 이 시간 안에 아무 일 없으면 빈 응답 (브라우저 타임아웃 회피)
MAX_BODY = 4 * 1024 * 1024

SAFE_NAME = re.compile(r"[^A-Za-z0-9_.-]")

TELEMETRY_DIR = None       # main() 에서 확정


# ============================ 텔레메트리 폴더 ============================

def find_telemetry_dir(explicit):
    """명시 경로 우선, 없으면 LocalLow 밑에서 가장 최근에 쓴 telemetry 폴더."""
    if explicit:
        p = Path(explicit).expanduser()
        return p if p.is_dir() else None

    root = Path.home() / "AppData" / "LocalLow"
    if not root.is_dir():
        return None

    cands = [d for d in root.glob("*/*/telemetry") if d.is_dir()]
    if not cands:
        return None

    # 회사/제품 폴더가 여러 개 남아 있는 게 흔하다 - 최근에 로그를 쓴 쪽을 고른다
    def newest(d):
        return max((f.stat().st_mtime for f in d.glob("*.jsonl")), default=0.0)

    cands.sort(key=newest, reverse=True)
    return cands[0]


# ============================ tail ============================

def collect_new(directory, offsets):
    """오프셋 이후로 새로 붙은 **완성된 줄만** 모은다.

    게임이 쓰는 중인 파일이라 마지막 줄이 잘려 있을 수 있다 -
    마지막 개행까지만 소비하고 오프셋도 거기까지만 올린다.
    """
    chunks = []
    next_off = {}

    for path in sorted(directory.glob("*.jsonl")):
        name = path.name
        try:
            size = path.stat().st_size
        except OSError:
            continue

        off = int(offsets.get(name, 0) or 0)
        if size < off:
            off = 0          # 파일이 줄었다 = 지웠다 다시 만든 것 -> 처음부터
        if size <= off:
            next_off[name] = off
            continue

        try:
            with path.open("rb") as f:
                f.seek(off)
                raw = f.read(size - off)
        except OSError:
            next_off[name] = off
            continue

        cut = raw.rfind(b"\n")
        if cut < 0:
            next_off[name] = off      # 아직 한 줄도 안 끝났다 - 다음번에
            continue

        chunks.append({"name": name, "text": raw[:cut + 1].decode("utf-8", "replace")})
        next_off[name] = off + cut + 1

    # 클라이언트가 알던 파일이 지금 안 보여도 오프셋은 유지한다(다시 나타나면 이어읽기)
    for k, v in offsets.items():
        next_off.setdefault(k, int(v or 0))

    return chunks, next_off


def wait_for_lines(directory, offsets, timeout):
    """새 줄이 생길 때까지 붙잡고 있다가 돌려준다. 시간 다 되면 빈 손으로."""
    deadline = time.monotonic() + timeout
    while True:
        chunks, next_off = collect_new(directory, offsets)
        if chunks or time.monotonic() >= deadline:
            return chunks, next_off
        time.sleep(POLL_INTERVAL)


# ============================ 일지 ============================

def journal_path(run_id):
    safe = SAFE_NAME.sub("_", str(run_id))[:80] or "unknown"
    return JOURNAL_DIR / (safe + ".json")


def load_journal(run_id):
    p = journal_path(run_id)
    if not p.is_file():
        return {"run_id": run_id, "days": {}}
    try:
        data = json.loads(p.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return {"run_id": run_id, "days": {}}
    if not isinstance(data, dict) or not isinstance(data.get("days"), dict):
        return {"run_id": run_id, "days": {}}
    return data


def save_journal(data):
    """원자적으로 쓴다 - 반쯤 쓴 파일이 남으면 그날 기록이 통째로 날아간다."""
    JOURNAL_DIR.mkdir(exist_ok=True)
    run_id = data.get("run_id") or "unknown"
    p = journal_path(run_id)
    data["updated_at"] = time.strftime("%Y-%m-%dT%H:%M:%S")
    tmp = p.with_name(p.name + ".tmp")
    tmp.write_text(json.dumps(data, ensure_ascii=False, indent=1), encoding="utf-8")
    os.replace(tmp, p)
    return p


# ============================ HTTP ============================

class Handler(SimpleHTTPRequestHandler):
    def __init__(self, *a, **kw):
        super().__init__(*a, directory=str(ROOT), **kw)

    def log_message(self, fmt, *args):
        pass   # 롱폴링이 콘솔을 뒤덮는다

    # ---------- 헬퍼 ----------
    def _send_json(self, obj, code=200):
        body = json.dumps(obj, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        try:
            self.wfile.write(body)
        except (BrokenPipeError, ConnectionResetError):
            pass   # 브라우저가 롱폴링 도중 탭을 닫으면 흔하다

    def _read_json(self):
        length = int(self.headers.get("Content-Length") or 0)
        if length <= 0 or length > MAX_BODY:
            return None
        try:
            return json.loads(self.rfile.read(length).decode("utf-8"))
        except (json.JSONDecodeError, UnicodeDecodeError):
            return None

    # ---------- GET ----------
    def do_GET(self):
        u = urlparse(self.path)

        if u.path == "/":
            self.send_response(302)
            self.send_header("Location", "/viewer.html")
            self.end_headers()
            return

        if u.path == "/api/hello":
            return self._send_json({
                "ok": True,
                "dir": str(TELEMETRY_DIR) if TELEMETRY_DIR else None,
                "journal_dir": str(JOURNAL_DIR),
            })

        if u.path == "/api/journal":
            run = (parse_qs(u.query).get("run") or [""])[0]
            if not run:
                return self._send_json({"error": "run 없음"}, 400)
            return self._send_json(load_journal(run))

        return SimpleHTTPRequestHandler.do_GET(self)

    # ---------- POST ----------
    def do_POST(self):
        u = urlparse(self.path)
        body = self._read_json()
        if body is None:
            return self._send_json({"error": "본문을 못 읽었다"}, 400)

        if u.path == "/api/tail":
            if not TELEMETRY_DIR or not TELEMETRY_DIR.is_dir():
                return self._send_json({"error": "telemetry 폴더 없음",
                                        "chunks": [], "offsets": {}}, 200)
            offsets = body.get("offsets") or {}
            if body.get("wait"):
                chunks, nxt = wait_for_lines(TELEMETRY_DIR, offsets, LONG_POLL_TIMEOUT)
            else:
                chunks, nxt = collect_new(TELEMETRY_DIR, offsets)
            return self._send_json({"chunks": chunks, "offsets": nxt})

        if u.path == "/api/journal":
            if not body.get("run_id"):
                return self._send_json({"error": "run_id 없음"}, 400)
            try:
                p = save_journal(body)
            except OSError as ex:
                return self._send_json({"error": str(ex)}, 500)
            return self._send_json({"ok": True, "path": str(p)})

        return self._send_json({"error": "없는 엔드포인트"}, 404)


def main():
    global TELEMETRY_DIR

    ap = argparse.ArgumentParser(description="텔레메트리 뷰어 + 일지 서버")
    ap.add_argument("dir", nargs="?", help="telemetry 폴더 (생략하면 자동 탐색)")
    ap.add_argument("--port", type=int, default=8777)
    ap.add_argument("--no-open", action="store_true")
    args = ap.parse_args()

    TELEMETRY_DIR = find_telemetry_dir(args.dir)

    url = "http://127.0.0.1:%d/viewer.html" % args.port
    print()
    print("  텔레메트리 뷰어 + 일지  >>  " + url)
    if TELEMETRY_DIR:
        print("  로그 폴더 : " + str(TELEMETRY_DIR))
    else:
        print("  로그 폴더 : 못 찾음 - 브라우저에 폴더를 드래그하거나 인자로 경로를 넘기자")
    print("  일지 저장 : " + str(JOURNAL_DIR))
    print("  이 창을 닫으면 서버도 꺼진다.")
    print()

    try:
        srv = ThreadingHTTPServer(("127.0.0.1", args.port), Handler)
    except OSError as ex:
        print("포트 %d 를 못 열었다: %s" % (args.port, ex))
        print("이미 떠 있는 창이 있는지 보고, 아니면 --port 로 바꾸자.")
        return 1

    if not args.no_open:
        threading.Timer(0.4, lambda: webbrowser.open(url)).start()

    try:
        srv.serve_forever()
    except KeyboardInterrupt:
        print("\n종료")
    return 0


if __name__ == "__main__":
    sys.exit(main())
