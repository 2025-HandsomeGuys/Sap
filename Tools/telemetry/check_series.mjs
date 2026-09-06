// 편집기(upgrade_tree_editor.html)의 **계열 재계산**을 검사한다.
//
// 계열 = "넓은 삽날 I · II · III …"처럼 한 줄로 이어지는 노드 묶음. 중간에 하나를
// 끼우면 그 위가 전부 밀려야 한다 — 로마숫자도, 단계별 효과값도, 설명의 숫자도,
// nodeId 꼬리번호도. 여기서 틀리면 트리가 조용히 어긋난다(실제로 손으로 끼우다
// '지구력 강화 II'가 두 개 생긴 적이 있다).
//
// HTML에서 SERIES 블록을 **그대로 떼어** 진짜 트리에 돌린다. 하나라도 어긋나면 exit 1.
//
//   node Tools/telemetry/check_series.mjs
//   node Tools/telemetry/check_series.mjs --report   # 지금 트리가 규칙과 어디가 다른지만

import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, resolve } from "node:path";

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..");
const HTML = resolve(ROOT, "Tools/upgrade_tree_editor.html");
const TREE = resolve(ROOT, "Assets/GameData/UpgradeData/UpgradeTree.csv");
const SERIES = resolve(ROOT, "Assets/GameData/UpgradeData/UpgradeSeries.csv");

/* ───────────────────────────── 거들기 ───────────────────────────── */

/** RFC 4180. 계열 CSV의 설명 틀에는 쉼표가 흔해서 단순 split으로는 안 된다. */
function parseCSV(text) {
  const rows = [[""]];
  let q = false;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (q) {
      if (c === '"' && text[i + 1] === '"') { rows.at(-1).at(-1) === undefined; rows[rows.length - 1][rows.at(-1).length - 1] += '"'; i++; }
      else if (c === '"') q = false;
      else rows[rows.length - 1][rows.at(-1).length - 1] += c;
    } else if (c === '"') q = true;
    else if (c === ",") rows.at(-1).push("");
    else if (c === "\n") rows.push([""]);
    else if (c !== "\r") rows[rows.length - 1][rows.at(-1).length - 1] += c;
  }
  if (rows.at(-1).length === 1 && rows.at(-1)[0] === "") rows.pop();
  return rows;
}

function readTable(path) {
  const rows = parseCSV(readFileSync(path, "utf8").replace(/^﻿/, ""));
  const head = rows[0].map((h) => h.trim());
  return rows.slice(1).filter((r) => r.some((c) => c.trim())).map((r) => {
    const o = {};
    head.forEach((h, i) => (o[h] = r[i] === undefined ? "" : r[i]));
    return o;
  });
}

function fmtNum(v) {
  const n = Number(v);
  if (!isFinite(n)) return "0";
  return String(parseFloat(n.toPrecision(6)));
}

/** HTML에서 SERIES 블록을 떼어 온다. S와 fmtNum만 바깥에서 준다. */
function loadSeries(S) {
  const html = readFileSync(HTML, "utf8");
  const a = html.indexOf("// ===== SERIES-START");
  const b = html.indexOf("// ===== SERIES-END");
  if (a < 0 || b < 0) throw new Error("HTML에서 SERIES 블록 표시를 못 찾았다 — 마커를 지웠나?");
  const src = html.slice(html.indexOf("\n", a) + 1, b);
  return new Function("S", "fmtNum", src +
    "\nreturn {roman, seriesPlan, seriesApply, seriesMembers, seriesOf, seriesSteps," +
    " seriesValueAt, seriesDesc, seriesExcluded, renameNodes, bakeSeries," +
    " bakeSummary, guessTemplate, freeSeriesId, SERIES_ID};")(S, fmtNum);
}

function loadNodes() {
  return readTable(TREE).map((r) => ({
    nodeId: r.nodeId.trim(),
    tier: parseInt(r.tier) || 0,
    effectType: r.effectType || "None",
    effectValue: parseFloat(r.effectValue) || 0,
    parents: (r.parentIds || "").split(";").map((t) => t.trim()).filter(Boolean),
    cost: parseInt(r.cost) || 0,
    uiX: parseFloat(r.uiX) || 0,
    uiY: parseFloat(r.uiY) || 0,
    displayNameKey: r.displayNameKey || "",
    descriptionKey: r.descriptionKey || "",
    lineBends: Object.fromEntries(
      (r.lineBends || "").split(";").filter(Boolean).map((e) => {
        const eq = e.indexOf("=");
        return [e.slice(0, eq), e.slice(eq + 1)];
      })),
  }));
}

const clone = (v) => JSON.parse(JSON.stringify(v));

/* ───────────────────────────── 검사 ───────────────────────────── */

let fails = 0, passes = 0;
function ok(cond, name, detail) {
  if (cond) { passes++; console.log("  ok   " + name + (detail ? " — " + detail : "")); }
  else { fails++; console.log("  FAIL " + name + (detail ? " — " + detail : "")); }
}

function freshState() {
  const S = { nodes: loadNodes(), series: readTable(SERIES), seriesAuto: true,
              selNode: null, selEdge: null };
  return { S, F: loadSeries(S) };
}

/** 트리가 성한가 — 중복 id 없음, 부모/선 모양이 있는 노드만 가리킴. */
function integrity(S, name) {
  const ids = new Set(S.nodes.map((n) => n.nodeId));
  ok(ids.size === S.nodes.length, name + ": nodeId 중복 없음",
     S.nodes.length - ids.size ? "겹친 것 " + (S.nodes.length - ids.size) + "개" : "");
  const dangling = [];
  for (const n of S.nodes) {
    for (const p of n.parents) if (!ids.has(p)) dangling.push(n.nodeId + " -> " + p);
    for (const k of Object.keys(n.lineBends)) if (!ids.has(k)) dangling.push(n.nodeId + " 선모양 " + k);
  }
  ok(!dangling.length, name + ": 끊긴 참조 없음", dangling.slice(0, 4).join(", "));
}

console.log("계열 검사 — " + HTML.replace(ROOT + "\\", "").replace(/\\/g, "/"));

// ── 0) 로마숫자
{
  const { F } = freshState();
  const want = ["I","II","III","IV","V","VI","VII","VIII","IX","X",
                "XI","XII","XIII","XIV","XV","XVI","XVII","XVIII","XIX","XX"];
  const got = want.map((_, i) => F.roman(i + 1));
  ok(got.join(",") === want.join(","), "로마숫자 1~20", got.slice(0, 6).join(" "));
}

// ── 1) 지금 트리 상태 + 첫 굽기가 무엇을 바꾸나
const first = (() => {
  const { S, F } = freshState();
  console.log("\n노드 " + S.nodes.length + " · 계열 " + S.series.length);
  const plan = F.seriesPlan();
  console.log("규칙과 다른 노드 " + plan.items.length + "개" +
              (plan.blocked.length ? " · 재번호 막힘 " + plan.blocked.join(",") : ""));
  if (plan.items.length) console.log(F.bakeSummary(plan, 40));
  return plan.items.length;
})();

if (process.argv.includes("--report")) process.exit(0);

// ── 2) 굽기는 멱등이다 — 두 번째는 아무것도 안 바뀌어야 한다
console.log("");
{
  const { S, F } = freshState();
  F.bakeSeries({ force: true });
  const again = F.seriesPlan();
  ok(!again.items.length, "두 번 구워도 그대로(멱등)",
     again.items.length ? again.items.length + "개가 또 바뀐다" : "");
  integrity(S, "굽고 나서");
}

// ── 3) cost·uiX·uiY·부모는 굽기가 안 건드린다
{
  const { S, F } = freshState();
  const before = new Map(S.nodes.map((n) => [n.nodeId, clone(n)]));
  const plan = F.seriesPlan();
  const idMap = new Map(plan.items.filter((it) => "nodeId" in it.changes)
                            .map((it) => [it.changes.nodeId, it.node.nodeId]));
  F.bakeSeries({ force: true });
  const bad = [];
  for (const n of S.nodes) {
    const was = before.get(idMap.get(n.nodeId) || n.nodeId);
    if (!was) { bad.push(n.nodeId + " 짝을 못 찾음"); continue; }
    if (was.cost !== n.cost) bad.push(n.nodeId + " cost " + was.cost + "->" + n.cost);
    if (was.uiX !== n.uiX || was.uiY !== n.uiY) bad.push(n.nodeId + " 좌표");
    if (was.parents.length !== n.parents.length) bad.push(n.nodeId + " 부모 수");
  }
  ok(!bad.length, "cost·좌표·부모 수는 그대로", bad.slice(0, 4).join(", "));
}

// ── 4) 중간에 끼우면 그 위가 전부 한 칸씩 밀린다 (이 기능의 핵심)
{
  const { S, F } = freshState();
  F.bakeSeries({ force: true });                    // 규칙에 맞춘 상태에서 시작
  const def = S.series.find((d) => d.seriesKey === "MiningRange");
  const before = F.seriesMembers(def).map((n) => ({
    id: n.nodeId, name: n.displayNameKey, v: n.effectValue }));

  // 2단계와 3단계 사이(uiY 중간)에 새 노드를 꽂는다
  const mid = (before.length >= 3)
    ? (F.seriesMembers(def)[1].uiY + F.seriesMembers(def)[2].uiY) / 2 : 0;
  S.nodes.push({
    nodeId: F.freeSeriesId("MiningRange", 0), tier: 0, effectType: "MiningRangeUp",
    effectValue: 0, parents: [], cost: 999, uiX: 0, uiY: mid,
    displayNameKey: "새 노드", descriptionKey: "", lineBends: {},
  });
  F.bakeSeries({ force: true });
  const after = F.seriesMembers(def).map((n) => ({
    id: n.nodeId, name: n.displayNameKey, v: n.effectValue }));

  ok(after.length === before.length + 1, "끼운 뒤 멤버 +1",
     before.length + " -> " + after.length);
  ok(after[2].name === def.nameBase + " III", "끼운 노드가 3단계가 된다", after[2].name);
  ok(after[3].name === def.nameBase + " IV" && after[3].id === before[2].id.replace(/_\d+$/, "_04"),
     "그 위가 IV로 밀린다", before[2].id + " -> " + after[3].id + " (" + after[3].name + ")");
  ok(after.at(-1).name === def.nameBase + " " + F.roman(after.length),
     "맨 위까지 번호가 이어진다", after.at(-1).name + " / " + after.at(-1).id);

  const steps = F.seriesSteps(def);
  ok(after[3].v === steps[3], "밀린 노드가 4단계 값을 받는다",
     before[2].v + " -> " + after[3].v + " (steps[3]=" + steps[3] + ")");
  integrity(S, "끼운 뒤");
}

// ── 5) 재번호가 자리를 맞바꿔도 id가 겹치지 않는다 (두 단계 rename)
{
  const { S, F } = freshState();
  F.bakeSeries({ force: true });
  const def = S.series.find((d) => d.seriesKey === "InventoryWeight");
  const ms = F.seriesMembers(def);
  // 1단계와 2단계의 높이를 맞바꾼다 -> _01 <-> _02 가 서로의 자리로 간다
  const t = ms[0].uiY; ms[0].uiY = ms[1].uiY; ms[1].uiY = t;
  F.bakeSeries({ force: true });
  const now = F.seriesMembers(def);
  ok(now[0].nodeId.endsWith("_01") && now[1].nodeId.endsWith("_02"),
     "자리를 맞바꿔도 번호가 제대로 붙는다", now[0].nodeId + " / " + now[1].nodeId);
  integrity(S, "맞바꾼 뒤");
}

// ── 6) 제외한 노드는 손대지 않고, 나머지 번호가 그만큼 당겨진다
{
  const { S, F } = freshState();
  F.bakeSeries({ force: true });
  const def = S.series.find((d) => d.seriesKey === "MiningRange");
  const victim = F.seriesMembers(def)[1];
  const keepName = victim.displayNameKey, keepId = victim.nodeId, keepV = victim.effectValue;
  def.exclude = victim.nodeId;
  F.bakeSeries({ force: true });
  ok(victim.nodeId === keepId && victim.displayNameKey === keepName &&
     victim.effectValue === keepV, "제외한 노드는 그대로", keepId + " / " + keepName);
  const ms = F.seriesMembers(def);
  ok(!ms.includes(victim), "제외한 노드는 멤버가 아니다", "멤버 " + ms.length + "개");
  ok(ms[1].displayNameKey === def.nameBase + " II", "뒤가 당겨진다", ms[1].displayNameKey);
}

// ── 7) 자리 충돌이면 그 계열은 번호를 통째로 포기한다 (반쯤 바꾸면 더 나쁘다)
{
  const { S, F } = freshState();
  F.bakeSeries({ force: true });
  const def = S.series.find((d) => d.seriesKey === "MiningRange");
  const ms = F.seriesMembers(def);
  // 계열 밖(제외) 노드가 3단계 자리 이름을 먼저 차지하게 만든다
  const squatter = ms[2];
  def.exclude = squatter.nodeId;
  const wanted = "MiningRange_T" + ms[3].tier + "_03";
  F.renameNodes(new Map([[squatter.nodeId, wanted]]));
  def.exclude = wanted;
  const ids = S.nodes.map((n) => n.nodeId).join("|");
  const plan = F.seriesPlan("MiningRange");
  ok(plan.blocked.includes("MiningRange"), "충돌하면 blocked로 알린다",
     plan.blocked.join(",") || "(비었다)");
  ok(!plan.items.some((it) => "nodeId" in it.changes), "충돌하면 id를 하나도 안 바꾼다");
  F.seriesApply(plan);
  ok(S.nodes.map((n) => n.nodeId).join("|") === ids, "충돌 시 id는 그대로");
  integrity(S, "충돌 뒤");
}

// ── 8) 설명 자리표
{
  const { F } = freshState();
  ok(F.seriesDesc("+{v} 넓어집니다", 0.15) === "+0.15 넓어집니다", "{v}");
  ok(F.seriesDesc("{abs} 줄어듭니다", -0.1) === "0.1 줄어듭니다", "{abs}");
  ok(F.seriesDesc("{pct}% 오릅니다", 0.15) === "15% 오릅니다", "{pct} (부동소수 반올림 포함)");
  ok(F.seriesDesc("{abspct}%p 감소", -0.15) === "15%p 감소", "{abspct}");
  ok(F.seriesDesc("{abs*0.5} 줄어듭니다", -0.1) === "0.05 줄어듭니다",
     "{abs*0.5} — 기준 0.5짜리 배율 스탯(ShovelStaminaReduce)");
  ok(F.seriesDesc("{v*100}%", 0.2) === "20%", "{v*100}");
  ok(F.seriesDesc("", 1) === null && F.seriesDesc("   ", 1) === null,
     "빈 틀은 null — 설명을 안 건드린다");
  ok(F.guessTemplate("가방 무게 한도가 +5 늘어납니다.", 5) === "가방 무게 한도가 +{v} 늘어납니다.",
     "설명에서 틀을 되뽑는다");
  ok(F.guessTemplate("범위가 +0.15 넓어집니다(기준 1.0).", 0.15) ===
     "범위가 +{v} 넓어집니다(기준 1.0).", "1.0을 0.15로 오인하지 않는다");
}

// ── 9) 단계값 목록을 넘어서면 stepTail이 이어 붙는다
{
  const { F } = freshState();
  const steps = [0.1, 0.15, 0.2];
  ok(F.seriesValueAt(steps, 0.05, 0) === 0.1 && F.seriesValueAt(steps, 0.05, 2) === 0.2,
     "목록 안은 목록 값");
  ok(F.seriesValueAt(steps, 0.05, 3) === 0.25 && F.seriesValueAt(steps, 0.05, 5) === 0.35,
     "목록 밖은 마지막 + stepTail x n", "0.25 / 0.35");
  ok(F.seriesValueAt(steps, 0, 9) === 0.2, "stepTail 0이면 마지막 값이 이어진다");
}

// ── 10) MiningLevel처럼 _Final로 끝나는 id는 계열이 아니다
{
  const { S, F } = freshState();
  const lic = S.nodes.find((n) => n.nodeId === "MiningLevel_T0_Final");
  ok(lic && F.seriesOf(lic) === null, "면허 노드(_Final)는 계열 밖",
     lic ? lic.nodeId : "(없음)");
}

console.log("\n" + (fails ? "실패 " + fails + " / " : "") + "통과 " + passes);
if (first) console.log("참고: 지금 트리는 규칙과 " + first + "곳 다르다 (편집기에서 '다시 굽기')");
process.exit(fails ? 1 : 0);
