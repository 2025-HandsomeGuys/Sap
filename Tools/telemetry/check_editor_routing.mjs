// 편집기(upgrade_tree_editor.html)의 선 라우팅이 게임과 같은 답을 내는지 검사한다.
//
// 라우팅 규칙이 C#(UpgradeLaneRouter + OrthogonalUILineRenderer)과 JS 두 벌로 있다.
// 어긋나면 편집기에서 멀쩡해 보이던 트리가 게임에서 노드를 관통한다 —
// 그게 바로 "낙법을 안 샀는데 밀착 등반이 사진다"로 신고됐던 그 버그다.
//
// 이 스크립트는 HTML에서 ROUTING 블록을 **그대로 떼어** 실제 트리에 돌린다.
// 관통이나 폭 초과가 하나라도 나오면 exit 1.
//
//   node Tools/telemetry/check_editor_routing.mjs
//
// EditMode의 UpgradeTreeLineRoutingTests가 C# 쪽에 같은 검사를 한다.
// 두 결과(연결 수·종류·관통 0)가 같아야 두 벌이 맞는 것이다.

import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, resolve } from "node:path";

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..");
const HTML = resolve(ROOT, "Tools/upgrade_tree_editor.html");
// 인자로 다른 CSV를 줄 수 있다 — 저장하기 전에 후보를 먼저 검사할 때 쓴다.
const CSV = process.argv[2]
  ? resolve(process.cwd(), process.argv[2])
  : resolve(ROOT, "Assets/GameData/UpgradeData/UpgradeTree.csv");

// UpgradeOverlayUI의 SerializeField 기본값
const GS = 1.5, NODE_W = 132, NODE_H = 146, OBSTACLE_PAD = 12, TRUNK_GAP = 22;
const CLEARANCE = NODE_H * 0.5 + TRUNK_GAP;
const DETOUR_LANE = NODE_W * 0.5 + 46;
const TREE_PAD_H = 100;

function loadRouting() {
  const html = readFileSync(HTML, "utf8");
  const a = html.indexOf("// ===== ROUTING-START");
  const b = html.indexOf("// ===== ROUTING-END");
  if (a < 0 || b < 0) throw new Error("HTML에서 ROUTING 블록 표시를 못 찾았다 — 마커를 지웠나?");
  const src = html.slice(html.indexOf("\n", a) + 1, b);
  return new Function(src + "\nreturn { makeRouter, buildPath, isBlocked };")();
}

function loadNodes() {
  const rows = readFileSync(CSV, "utf8").replace(/^﻿/, "").trim().split(/\r?\n/);
  const head = rows[0].split(",");
  const col = (name) => head.indexOf(name);
  const iId = col("nodeId"), iP = col("parentIds"), iX = col("uiX"), iY = col("uiY"),
        iName = col("displayNameKey"), iBends = col("lineBends");
  return rows.slice(1).filter(Boolean).map((line) => {
    // 이 CSV는 설명에 쉼표가 없어 단순 split으로 충분하다. 인용 부호가 생기면 여기부터 고칠 것.
    const c = line.split(",");
    // lineBends 칸은 나중에 생겼다 — 없는 CSV도 그대로 읽힌다(C# 리더와 같은 규칙).
    const bends = {};
    if (iBends >= 0) {
      for (const entry of (c[iBends] || "").split(";")) {
        const eq = entry.indexOf("=");
        if (eq <= 0) continue;
        const pts = [];
        for (const pt of entry.slice(eq + 1).split("|")) {
          const colon = pt.indexOf(":");
          if (colon <= 0) continue;
          const x = parseFloat(pt.slice(0, colon)), y = parseFloat(pt.slice(colon + 1));
          if (!Number.isNaN(x) && !Number.isNaN(y)) pts.push({ x, y });
        }
        if (pts.length) bends[entry.slice(0, eq).trim()] = pts;
      }
    }
    return {
      id: c[iId],
      name: c[iName] || c[iId],
      parents: (c[iP] || "").split(";").filter(Boolean),
      uiX: parseFloat(c[iX]), uiY: parseFloat(c[iY]),
      bends,
    };
  });
}

const R = loadRouting();
const nodes = loadNodes();

let minY = Infinity, maxY = -Infinity, maxAbsX = 1;
for (const n of nodes) {
  minY = Math.min(minY, n.uiY); maxY = Math.max(maxY, n.uiY);
  maxAbsX = Math.max(maxAbsX, Math.abs(n.uiX));
}
const centerY = (minY + maxY) * 0.5;
const pos = new Map(nodes.map((n) => [n.id, { x: n.uiX * GS, y: (n.uiY - centerY) * GS }]));
const nameOf = new Map(nodes.map((n) => [n.id, n.name]));

const hw = NODE_W * 0.5 + OBSTACLE_PAD, hh = NODE_H * 0.5 + OBSTACLE_PAD;
const obstacles = nodes.map((n) => {
  const p = pos.get(n.id);
  return { x0: p.x - hw, y0: p.y - hh, x1: p.x + hw, y1: p.y + hh };
});

const childCount = new Map();
for (const n of nodes)
  for (const p of n.parents) childCount.set(p, (childCount.get(p) || 0) + 1);

const links = [];
for (const n of nodes) {
  const merges = n.parents.length > 1;
  for (const pid of n.parents) {
    if (!pos.has(pid)) { console.error(`부모 미해결: ${n.id} <- ${pid}`); process.exit(1); }
    const ui = n.bends[pid];
    links.push({
      parentId: pid, childId: n.id,
      start: pos.get(pid), end: pos.get(n.id),
      parentForks: (childCount.get(pid) || 0) > 1, childMerges: merges,
      // 그린 꺾임점은 ui 좌표로 저장된다 — 매핑으로 바꿔 넘긴다(UpgradeOverlayUI와 같은 변환).
      bends: ui ? ui.map((b) => ({ x: b.x * GS, y: (b.y - centerY) * GS })) : null,
    });
  }
}

const router = R.makeRouter(Array.from(pos.values()), links, CLEARANCE);
const halfContent = maxAbsX * GS + NODE_W * 0.5 + TREE_PAD_H;

const kinds = {};
const blocked = [], wide = [], ignoredJog = [];
for (const l of links) {
  const r = R.buildPath(l.start, l.end, obstacles, DETOUR_LANE, router.jogY(l), l.bends);
  kinds[r.kind] = (kinds[r.kind] || 0) + 1;
  const tag = `${nameOf.get(l.parentId)} -> ${nameOf.get(l.childId)}`;
  if (r.kind === "blocked" || R.isBlocked(r.path, obstacles, l.start, l.end)) blocked.push(tag);
  if (r.path.some((p) => Math.abs(p.x) > halfContent + 0.5)) wide.push(tag);
  // 그린 모양이 안 먹고 자동으로 돌아간 선 — 편집기에서 끌었는데 안 움직인 것.
  if (l.bends && l.bends.length && r.kind !== "drawn") ignoredJog.push(tag);
}

// 허브(분기·합류)마다 꺾이는 높이가 하나여야 한 줄로 나왔다 갈라지고 한 줄로 모인다.
const hubs = new Set();
for (const l of links) { if (l.parentForks) hubs.add(l.parentId); if (l.childMerges) hubs.add(l.childId); }
const split = [];
for (const h of hubs) {
  const heights = (pick) => {
    const set = new Set();
    for (const l of links) {
      if (pick(l)) { const j = router.jogY(l); if (!Number.isNaN(j)) set.add(Math.round(j * 10) / 10); }
    }
    return set;
  };
  const ins = heights((l) => l.childId === h), outs = heights((l) => l.parentId === h);
  if (ins.size > 1 || outs.size > 1) split.push(`${nameOf.get(h)} 들어옴 ${[...ins]} 나감 ${[...outs]}`);
}

console.log(`노드 ${nodes.length} · 연결 ${links.length} · 경로 ${JSON.stringify(kinds)}`);
console.log(`관통 ${blocked.length} · 폭초과 ${wide.length} · 허브 ${hubs.size}개 중 꺾임 여럿 ${split.length}` +
            (ignoredJog.length ? ` · 그린 모양 무시됨 ${ignoredJog.length}` : ""));
for (const t of blocked) console.log("  관통:", t);
for (const t of wide) console.log("  폭초과:", t);
for (const t of split) console.log("  꺾임 여럿:", t);
for (const t of ignoredJog) console.log("  그린 모양 무시됨(자동으로 돌아감):", t);

// 지정 무시는 경고지 실패가 아니다 — 선은 멀쩡히 그려진다.
process.exit(blocked.length || wide.length || split.length ? 1 : 0);
