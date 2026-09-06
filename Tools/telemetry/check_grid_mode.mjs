// 편집기(upgrade_tree_editor.html)의 **격자 모드**가 트리를 그대로 되살리는지 검사한다.
//
// 격자 모드는 CSV를 격자에 맞춰 다시 앉히고, 자동으로 그려지던 선을 눈금 위로
// 옮겨 찍은 뒤, 그 눈금에서 부모·자식을 **되읽는다**. 이 왕복이 무손실이어야
// 격자에서 한 칸만 고치고 저장해도 나머지가 흔들리지 않는다.
//
// 지금 UpgradeTree.csv는 이미 격자 규칙을 그대로 따른다:
//   uiX = 열 × 87.5 · uiY = 층 × 120 · **모이는 노드(부모 둘 이상)가 있는 층만 +30**
// 그래서 "격자 모드에 들어갔다 나오면 좌표와 선행이 한 글자도 안 바뀐다"가
// 곧 규칙이 맞다는 증거다. 하나라도 어긋나면 exit 1.
//
//   node Tools/telemetry/check_grid_mode.mjs [다른.csv]
//
// 라우팅 규칙 자체(게임 C#과 같은 답을 내는가)는 check_editor_routing.mjs가 본다.

import { readFileSync, writeFileSync, mkdtempSync } from "node:fs";
import { fileURLToPath, pathToFileURL } from "node:url";
import { dirname, resolve, join } from "node:path";
import { tmpdir } from "node:os";

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..");
const HTML = resolve(ROOT, "Tools/upgrade_tree_editor.html");
const CSV = process.argv[2]
  ? resolve(process.cwd(), process.argv[2])
  : resolve(ROOT, "Assets/GameData/UpgradeData/UpgradeTree.csv");

/* ── 편집기 <script>를 돌리기 위한 최소 DOM 받침대 ──────────────────────
   편집기는 브라우저용 한 덩어리라 떼어낼 수가 없다. 진짜 그 코드를 돌려야
   검사가 의미가 있으므로, 필요한 만큼만 흉내 낸 DOM 위에 그대로 얹는다. */
const STUB = `
function fakeEl(tag = "div") {
  const e = {
    tagName: tag.toUpperCase(), children: [], style: {}, dataset: {},
    hidden: false, disabled: false, value: "", files: [], _t: "", _h: "",
    classList: { add(){}, remove(){}, toggle(){}, contains(){ return false; } },
    setAttribute(){}, removeAttribute(){}, getAttribute(){ return null; },
    addEventListener(){}, removeEventListener(){},
    append(...c){ e.children.push(...c); },
    appendChild(c){ e.children.push(c); return c; },
    getBoundingClientRect(){ return { left:0, top:0, width:1200, height:800 }; },
    setPointerCapture(){}, releasePointerCapture(){},
    querySelector(){ return fakeEl(); }, querySelectorAll(){ return []; },
    focus(){}, click(){},
    get textContent(){ return e._t; },
    set textContent(v){ e._t = v; if (v === "") e.children.length = 0; },
    get innerHTML(){ return e._h; }, set innerHTML(v){ e._h = v; },
  };
  return e;
}
const __reg = new Map();
globalThis.document = {
  querySelector(sel){ if (!__reg.has(sel)) __reg.set(sel, fakeEl()); return __reg.get(sel); },
  createElementNS(ns, tag){ return fakeEl(tag); },
  createElement(tag){ return fakeEl(tag); },
  addEventListener(){},
};
globalThis.window = { addEventListener(){} };
globalThis.confirm = () => true;
globalThis.alert = (m) => { throw new Error("alert: " + m); };
globalThis.URL = { createObjectURL: () => "", revokeObjectURL(){} };
globalThis.Blob = class {};
`;

const EXPORTS = `
export const T = {
  S, fromCSV, toCSV, refresh, gridEnter, gridApply, gridAddNode, gridToggleSeg,
  gridNodeAt, gridInsertRows, unseg, GRID_COL, GRID_ROW, lift: () => MERGE_LIFT,
};
`;

async function loadEditor() {
  const html = readFileSync(HTML, "utf8");
  const m = html.match(/<script>\n([\s\S]*)\n<\/script>/);
  if (!m) throw new Error("HTML에서 <script> 블록을 못 찾았다");
  const dir = mkdtempSync(join(tmpdir(), "upgrade-grid-"));
  const file = join(dir, "editor.mjs");
  writeFileSync(file, STUB + "\n" + m[1] + "\n" + EXPORTS, "utf8");
  return (await import(pathToFileURL(file).href)).T;
}

/* ────────────────────────────── 검사 ────────────────────────────── */

const T = await loadEditor();
let fails = 0;
const ok = (cond, msg) => {
  console.log((cond ? "  ok   " : "  FAIL ") + msg);
  if (!cond) fails++;
};

const csv = readFileSync(CSV, "utf8");
const want = new Map(T.fromCSV(csv).map(n =>
  [n.nodeId, { x: n.uiX, y: n.uiY, p: n.parents.slice() }]));

T.S.nodes = T.fromCSV(csv);
T.refresh();
console.log(`노드 ${T.S.nodes.length} · 연결 ${T.S.layout.links.length}`);

T.gridEnter();
const geo = T.S.grid.geo;
console.log(`격자 — 눈금 ${T.S.grid.segs.size}칸 · 모이는 층 ${geo.mergeRow.size}개 · ` +
            `떠 있는 눈금 ${T.S.grid.orphans.length}칸`);

// 1) 좌표가 격자에 정확히 떨어지고, 원래 값 그대로인가
const off = T.S.nodes.filter(n =>
  Math.abs(n.uiX / T.GRID_COL - Math.round(n.uiX / T.GRID_COL)) > 1e-9);
ok(off.length === 0, `모든 uiX가 ${T.GRID_COL} 배수 — ${off.map(n => n.nodeId).slice(0, 3)}`);

const moved = [];
for (const n of T.S.nodes) {
  const w = want.get(n.nodeId);
  if (Math.abs(w.x - n.uiX) > 1e-6) moved.push(`${n.nodeId} x ${w.x}→${n.uiX}`);
  if (Math.abs(w.y - n.uiY) > 1e-6) moved.push(`${n.nodeId} y ${w.y}→${n.uiY}`);
}
ok(moved.length === 0, `격자에 앉혀도 좌표 그대로 — ${moved.slice(0, 4).join(" · ")}`);

// 2) 모이는 층만 한 칸이 길다
const gaps = [];
geo.mergeRow.forEach(r => {
  const gap = geo.rowY.get(r) - geo.rowY.get(r - 1);
  if (Math.abs(gap - (T.GRID_ROW + T.lift())) > 1e-6) gaps.push(`${r}층 ${gap}`);
});
ok(gaps.length === 0,
   `모이는 층 ${geo.mergeRow.size}개가 전부 아래보다 ${T.GRID_ROW + T.lift()} 위 — ${gaps.join(" · ")}`);

// 3) 부모·자식이 눈금에서 그대로 되읽히는가
//    (형제가 나눠 타는 줄기 때문에 서로 부모가 돼 버리는 게 여기서 잡힌다)
const lost = [], extra = [];
for (const n of T.S.nodes) {
  const w = new Set(want.get(n.nodeId).p), g = new Set(n.parents);
  for (const p of w) if (!g.has(p)) lost.push(`${n.nodeId}←${p}`);
  for (const p of g) if (!w.has(p)) extra.push(`${n.nodeId}←${p}`);
}
ok(lost.length === 0, `빠진 연결 없음 — ${lost.slice(0, 5).join(" · ")}`);
ok(extra.length === 0, `없던 연결 안 생김 — ${extra.slice(0, 5).join(" · ")}`);

// 4) 그린 모양이 게임 경로로 그대로 나오는가 (노드를 뚫으면 게임이 딴 길로 샌다)
const kinds = {};
for (const l of T.S.layout.links) kinds[l.kind] = (kinds[l.kind] || 0) + 1;
console.log(`경로 종류 ${JSON.stringify(kinds)}`);
const rejected = T.S.layout.links.filter(l => l.bends && l.bends.length && l.kind !== "drawn");
ok(rejected.length === 0,
   `그린 모양이 튕긴 선 없음 — ${rejected.slice(0, 4).map(l => l.parentId + "→" + l.childId).join(" · ")}`);
ok(T.S.grid.orphans.length === 0, `떠 있는 눈금 없음 — ${T.S.grid.orphans.length}칸`);

// 5) 눈금 하나를 끄면 그 연결만 끊기고, 켜면 돌아온다
const some = T.S.layout.links.find(l => l.bends && l.bends.length);
const child = T.S.nodes.find(n => n.nodeId === some.childId);
const key = [...T.S.grid.segs].find(k => {
  const s = T.unseg(k);
  return s.o === "V" && s.c === child.gc && s.h === child.gr * 2 - 1;
});
if (key) {
  const before = child.parents.length;
  T.gridToggleSeg(key, false); T.gridApply();
  const cut = T.S.nodes.find(n => n.nodeId === child.nodeId).parents.length;
  T.gridToggleSeg(key, true); T.gridApply();
  const back = T.S.nodes.find(n => n.nodeId === child.nodeId).parents.length;
  ok(cut === before - 1 && back === before,
     `${child.nodeId}: 눈금을 끄면 부모 ${before}→${cut}, 켜면 ${back}`);
} else {
  ok(false, "시험할 세로 눈금을 못 찾음");
}

// 6) 다시 구운 CSV를 되읽을 수 있는가
const out = T.toCSV(T.S.nodes);
const reread = T.fromCSV(out);
ok(reread.length === T.S.nodes.length, `다시 구운 CSV ${reread.length}행`);
ok(out.split("\r\n")[0].includes("lineBends"), "lineBends 열이 있다");
console.log(`lineBends가 적힌 노드 ${reread.filter(n => Object.keys(n.lineBends).length).length}` +
            ` / ${reread.length}`);

// 7) 새 노드는 격자 모서리에 놓인다
const n0 = T.S.nodes.length;
T.gridAddNode(5, geo.maxR - 1);
const added = T.gridNodeAt(5, geo.maxR - 1);
ok(T.S.nodes.length === n0 + 1 && added && Math.abs(added.uiX - 5 * T.GRID_COL) < 1e-6,
   `격자 모서리에 노드가 놓인다 (uiX ${added ? added.uiX : "?"})`);

// 8) 층 삽입 — 자리만 벌어지고 선행·아래 좌표는 그대로여야 한다
{
  const mid = Math.round((geo.minR + geo.maxR) / 2);
  const before = new Map(T.S.nodes.map(n => [n.nodeId, { r: n.gr, y: n.uiY, p: n.parents.slice() }]));
  const N = 2;
  T.gridInsertRows(mid, N);
  T.gridApply();

  const bad = [];
  for (const n of T.S.nodes) {
    const b = before.get(n.nodeId);
    if (!b) continue;
    if (b.r >= mid) {
      if (n.gr !== b.r + N) bad.push(`${n.nodeId} 층 ${b.r}→${n.gr}`);
      if (n.uiY <= b.y) bad.push(`${n.nodeId} 안 올라감 ${b.y}→${n.uiY}`);
    } else if (n.gr !== b.r || Math.abs(n.uiY - b.y) > 1e-6) {
      bad.push(`${n.nodeId} 아래인데 움직임 ${b.y}→${n.uiY}`);
    }
  }
  ok(bad.length === 0, `${mid}층부터 위만 ${N}칸 올라감 — ${bad.slice(0, 4).join(" · ")}`);

  const broke = [];
  for (const n of T.S.nodes) {
    const b = before.get(n.nodeId);
    if (!b) continue;
    if (b.p.slice().sort().join(",") !== n.parents.slice().sort().join(","))
      broke.push(`${n.nodeId}: [${b.p}] → [${n.parents}]`);
  }
  ok(broke.length === 0, `밀어 올려도 선행 그대로 — ${broke.slice(0, 5).join(" · ")}`);
  ok(T.S.grid.orphans.length === 0, `밀어 올려도 떠 있는 눈금 없음 — ${T.S.grid.orphans.length}칸`);
}

console.log(fails ? `\n실패 ${fails}건` : "\n전부 통과");
process.exit(fails ? 1 : 0);
