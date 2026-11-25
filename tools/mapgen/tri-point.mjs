#!/usr/bin/env node
// Tri-Point map generator (design: Notion "Map Design: Tri-Point (3-Player Skirmish)").
// Builds the WATER version of the map as a typed tile grid (land / water / cliff / bridge)
// with 120-degree rotational symmetry, measures the design's distance targets, and writes:
//   content/maps/tri-point.json          — typed tiles + features (the map itself)
//   content/scenarios/tri-point-30.json  — 30v30 T1 combined-arms scenario on it
//   --preview <file.svg>                 — optional top-down preview
// The sim has no water yet: the scenario's `blocked` = water + cliff. Bridges and land are
// walkable. Deterministic: same script, same output. Run: node tools/mapgen/tri-point.mjs
import { writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const args = process.argv.slice(2);
const previewPath = args.includes('--preview') ? args[args.indexOf('--preview') + 1] : null;

// ---- geometry (cells) -------------------------------------------------------------------
const W = 130, H = 116;
const C = { x: 65, y: 71 };               // map centre = atoll centre
const G = {
  atollIn: 4, atollOut: 8,                 // lagoon radius / ring outer radius
  natD: 40, natR: 6,                       // natural: distance from centre, radius
  mainD: 56, mainR: 8, cliff: 2,           // main: distance, radius, cliff band thickness
  seamD: 30, seamR: 6,                     // Seam expansion: distance, radius
  harbD: 50, harbAng: 15, harbR: 4,        // harbour: distance, angle offset (deg), radius
  lane: 2.0, causeway: 1.5, ramp: 1.5, harbPath: 1.5, // capsule half-widths
  strait: 1.5, channel: 1.5,               // water-band half-widths (bridged where land crosses)
};
// Players at the triangle corners (y grows down). Clockwise = +120 deg.
const PLAYERS = [
  { id: 'ascendant', ang: -90 },
  { id: 'hegemony', ang: 30 },
  { id: 'coalition', ang: 150 },
];
const rad = d => (d * Math.PI) / 180;
const polar = (r, deg) => ({ x: C.x + r * Math.cos(rad(deg)), y: C.y + r * Math.sin(rad(deg)) });
const dist = (a, b) => Math.hypot(a.x - b.x, a.y - b.y);
function segDist(p, a, b) {
  const vx = b.x - a.x, vy = b.y - a.y, wx = p.x - a.x, wy = p.y - a.y;
  const t = Math.max(0, Math.min(1, (wx * vx + wy * vy) / (vx * vx + vy * vy)));
  return Math.hypot(wx - t * vx, wy - t * vy);
}
// Signed position along a segment (0..len) and perpendicular distance.
function along(p, a, b) {
  const vx = b.x - a.x, vy = b.y - a.y, len = Math.hypot(vx, vy);
  const ux = vx / len, uy = vy / len, wx = p.x - a.x, wy = p.y - a.y;
  return { s: wx * ux + wy * uy, perp: Math.abs(wx * uy - wy * ux), len };
}

const nodes = PLAYERS.map(pl => ({
  ...pl,
  main: polar(G.mainD, pl.ang),
  natural: polar(G.natD, pl.ang),
  harbour: polar(G.harbD, pl.ang + G.harbAng),
  seamCw: polar(G.seamD, pl.ang + 60),     // Seam shared with the next player clockwise
  seamCcw: polar(G.seamD, pl.ang - 60),
  ringLanding: polar(G.atollOut, pl.ang),
}));
const seams = [0, 1, 2].map(i => ({
  between: [PLAYERS[i].id, PLAYERS[(i + 1) % 3].id],
  at: polar(G.seamD, PLAYERS[i].ang + 60),
  ang: PLAYERS[i].ang + 60,
}));

// ---- rasterise ---------------------------------------------------------------------------
const T = { LAND: '.', WATER: '~', CLIFF: '#', BRIDGE: '=' };
const tiles = Array.from({ length: H }, () => Array(W).fill(T.WATER));
for (let y = 0; y < H; y++) {
  for (let x = 0; x < W; x++) {
    const p = { x: x + 0.5, y: y + 0.5 };
    const dc = dist(p, C);
    let t = T.WATER;
    let land = dc > G.atollIn && dc <= G.atollOut;               // atoll ring
    for (const n of nodes) {
      if (dist(p, n.natural) <= G.natR || dist(p, n.main) <= G.mainR ||
          dist(p, n.harbour) <= G.harbR || dist(p, n.seamCw) <= G.seamR ||
          segDist(p, n.natural, n.seamCw) <= G.lane || segDist(p, n.natural, n.seamCcw) <= G.lane ||
          segDist(p, n.natural, n.ringLanding) <= G.causeway ||
          segDist(p, n.natural, n.harbour) <= G.harbPath ||
          segDist(p, n.main, n.natural) <= G.ramp) land = true;
    }
    if (land) t = T.LAND;
    for (const n of nodes) {
      // Cliffs ring each main; the one ramp toward the natural cuts through them.
      const dm = dist(p, n.main);
      if (dm > G.mainR && dm <= G.mainR + G.cliff && segDist(p, n.main, n.natural) > G.ramp) t = T.CLIFF;
      // Strait: a water band across the clockwise lane's midpoint; the lane bridges it.
      const a = along(p, n.natural, n.seamCw);
      if (Math.abs(a.s - a.len / 2) <= G.strait && t === T.LAND && a.perp <= G.lane) t = T.BRIDGE;
    }
    // Channels: the ring is cut toward each Seam so ships reach the lagoon; bridged for land.
    if (dc > G.atollIn && dc <= G.atollOut) {
      for (const s of seams) {
        const a = along(p, C, polar(G.atollOut + 2, s.ang));
        if (a.s > 0 && a.perp <= G.channel) t = T.BRIDGE;
      }
    }
    // Piers: lagoon walkways from the ring to the central rig (one per causeway direction).
    if (dc <= G.atollIn && dc >= 1.0) {
      for (const n of nodes) {
        const a = along(p, C, n.ringLanding);
        if (a.s > 0 && a.perp <= 0.5) t = T.BRIDGE;
      }
    }
    tiles[y][x] = t;
  }
}
const landWalk = t => t === T.LAND || t === T.BRIDGE;
const navalWalk = t => t === T.WATER || t === T.BRIDGE;

// ---- distances (octile Dijkstra, same 8-dir / no-corner-cut rule as sim A*) --------------
function dijkstra(sources, walk) {
  const D = new Float64Array(W * H).fill(Infinity);
  const q = [];
  for (const s of sources) { D[s.y * W + s.x] = 0; q.push([0, s.x, s.y]); }
  const ok = (x, y) => x >= 0 && y >= 0 && x < W && y < H && walk(tiles[y][x]);
  while (q.length) {
    let bi = 0; for (let i = 1; i < q.length; i++) if (q[i][0] < q[bi][0]) bi = i;
    const [d, x, y] = q[bi]; q[bi] = q[q.length - 1]; q.pop();
    if (d > D[y * W + x]) continue;
    for (let dy = -1; dy <= 1; dy++) for (let dx = -1; dx <= 1; dx++) {
      if (!dx && !dy) continue;
      const nx = x + dx, ny = y + dy;
      if (!ok(nx, ny)) continue;
      if (dx && dy && (!ok(x + dx, y) || !ok(x, y + dy))) continue; // no corner cutting
      const nd = d + (dx && dy ? Math.SQRT2 : 1);
      if (nd < D[ny * W + nx]) { D[ny * W + nx] = nd; q.push([nd, nx, ny]); }
    }
  }
  return D;
}
const cell = p => ({ x: Math.floor(p.x), y: Math.floor(p.y) });
const at = (D, p) => D[cell(p).y * W + cell(p).x];
const ringCells = [];
for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
  const dc = dist({ x: x + 0.5, y: y + 0.5 }, C);
  if (dc > G.atollIn && dc <= G.atollOut && landWalk(tiles[y][x])) ringCells.push({ x, y });
}
const rigCell = cell(C);
const report = [];
const fmt = v => (Number.isFinite(v) ? v.toFixed(1) : 'UNREACHABLE');
for (const n of nodes) {
  const fromNat = dijkstra([cell(n.natural)], landWalk);
  const fromMain = dijkstra([cell(n.main)], landWalk);
  const seamTrip = (at(fromNat, n.seamCw) + at(fromNat, n.seamCcw)) / 2;
  const toIsland = Math.min(...ringCells.map(c => fromNat[c.y * W + c.x]));
  const enemyMains = nodes.filter(o => o !== n).map(o => at(fromMain, o.main));
  // Naval: harbour shore water -> water next to the rig.
  const shore = [];
  for (let y = 0; y < H; y++) for (let x = 0; x < W; x++)
    if (tiles[y][x] === T.WATER && dist({ x: x + 0.5, y: y + 0.5 }, n.harbour) <= G.harbR + 1.5) shore.push({ x, y });
  const fromHarb = dijkstra(shore, navalWalk);
  let rigTrip = Infinity;
  for (let dy = -1; dy <= 1; dy++) for (let dx = -1; dx <= 1; dx++)
    rigTrip = Math.min(rigTrip, fromHarb[(rigCell.y + dy) * W + rigCell.x + dx]);
  report.push({
    player: n.id,
    'main->natural': fmt(at(fromMain, n.natural)),
    'natural->seam cw/ccw': `${fmt(at(fromNat, n.seamCw))} / ${fmt(at(fromNat, n.seamCcw))}`,
    'natural->island': fmt(toIsland),
    'island/seam': (toIsland / seamTrip).toFixed(2),
    'main->natural/seam': (at(fromMain, n.natural) / seamTrip).toFixed(2),
    'main->enemy mains/seam': enemyMains.map(v => (v / seamTrip).toFixed(2)).join(' / '),
    'harbour->rig (naval)': fmt(rigTrip),
  });
}
console.table(report);
const unreachable = report.some(r => Object.values(r).some(v => String(v).includes('UNREACHABLE')));
if (unreachable) { console.error('mapgen: a required route is unreachable'); process.exit(1); }

// ---- resource fields (T34) -----------------------------------------------------------------
// One field = a plus of 5 ore cells, FIELD_POOL credits split evenly (canon: 25,000 per field,
// docs/factions/economy.md). Main: behind the base, away from the ramp, inside the plateau.
// Natural: the natural's centre. Contested: each Seam, pushed outward from the map centre so the
// Seam's centre stays free for its radar. Veins sit at cell centres (sim: cell = floor).
const FIELD_POOL = 25000;
const unit = (a, b) => { const d = dist(a, b); return { x: (b.x - a.x) / d, y: (b.y - a.y) / d }; };
function field(kind, owner, centre) {
  const c = cell(centre);
  const cells = [[0, 0], [1, 0], [-1, 0], [0, 1], [0, -1]].map(([dx, dy]) => ({ x: c.x + dx, y: c.y + dy }));
  for (const v of cells)
    if (tiles[v.y]?.[v.x] !== T.LAND) { console.error(`mapgen: ${kind} field ore off land at ${v.x},${v.y}`); process.exit(1); }
  const pool = FIELD_POOL / cells.length;
  return { kind, ...owner, veins: cells.map(v => ({ at: [v.x + 0.5, v.y + 0.5], pool })) };
}
const fields = [];
for (const n of nodes) {
  const back = unit(n.natural, n.main); // natural -> main, continued behind the main
  fields.push(field('main', { player: n.id }, { x: n.main.x + back.x * 5, y: n.main.y + back.y * 5 }));
  fields.push(field('natural', { player: n.id }, n.natural));
}
for (const s of seams) {
  const out = unit(C, s.at);
  fields.push(field('contested', { between: s.between }, { x: s.at.x + out.x * 3.5, y: s.at.y + out.y * 3.5 }));
}

// ---- map file ----------------------------------------------------------------------------
const r1 = v => Math.round(v * 10) / 10;
const pt = p => [r1(p.x), r1(p.y)];
const features = [];
for (const n of nodes) {
  features.push({ type: 'main', player: n.id, at: pt(n.main) });
  features.push({ type: 'natural', player: n.id, at: pt(n.natural) });
  features.push({ type: 'harbour', player: n.id, at: pt(n.harbour) });
  features.push({ type: 'causeway', player: n.id, from: pt(n.natural), to: pt(n.ringLanding), destructible: true });
}
for (const s of seams) {
  features.push({ type: 'seam', between: s.between, at: pt(s.at) });
  features.push({ type: 'radar', between: s.between, at: pt(s.at), capturedBy: 'engineer' });
}
features.push({ type: 'rig', capture: 'engineer-or-hold', at: pt(C) });
for (const fl of fields) features.push({ type: 'field', ...fl });
const map = {
  schemaVersion: 1,
  id: 'tri-point',
  name: 'Tri-Point',
  generatedBy: 'tools/mapgen/tri-point.mjs',
  width: W,
  height: H,
  legend: { '.': 'land', '~': 'water', '#': 'cliff', '=': 'bridge' },
  rows: tiles.map(r => r.join('')),
  features,
};
writeFileSync(join(repo, 'content', 'maps', 'tri-point.json'), JSON.stringify(map, null, 1) + '\n');

// ---- blocked rects (water + cliff) for today's land-only sim -----------------------------
const blockedCell = (x, y) => !landWalk(tiles[y][x]);
const rects = [];
const open = new Map(); // key "x0,x1" -> rect being grown downward
for (let y = 0; y < H; y++) {
  const runs = [];
  for (let x = 0; x < W;) {
    if (!blockedCell(x, y)) { x++; continue; }
    let x1 = x; while (x1 + 1 < W && blockedCell(x1 + 1, y)) x1++;
    runs.push([x, x1]); x = x1 + 1;
  }
  const next = new Map();
  for (const [x0, x1] of runs) {
    const k = `${x0},${x1}`;
    const r = open.get(k);
    if (r) { r[3] = y; next.set(k, r); open.delete(k); } else next.set(k, [x0, y, x1, y]);
  }
  for (const r of open.values()) rects.push(r);
  open.clear(); for (const [k, r] of next) open.set(k, r);
}
for (const r of open.values()) rects.push(r);
rects.sort((a, b) => a[1] - b[1] || a[0] - b[0]);

// ---- 30v30 roster: T1 combined arms (24 line infantry + 6 MBT per side) ------------------
function formation(n, infantry, tank) {
  // Toward the ramp: tanks in front, infantry block behind; all inside the main plateau.
  const toNat = { x: n.natural.x - n.main.x, y: n.natural.y - n.main.y };
  const L = Math.hypot(toNat.x, toNat.y); const f = { x: toNat.x / L, y: toNat.y / L };
  const s = { x: -f.y, y: f.x };
  const place = (fw, side) => ({ x: n.main.x + f.x * fw + s.x * side, y: n.main.y + f.y * fw + s.y * side });
  const out = [];
  for (let i = 0; i < 6; i++) out.push({ unit: tank, at: place(3.5, (i - 2.5) * 1.3) });
  for (let row = 0; row < 4; row++)
    for (let col = 0; col < 6; col++) out.push({ unit: infantry, at: place(1.5 - row * 1.1, (col - 2.5) * 1.1) });
  for (const u of out) {
    const c = cell(u.at);
    if (!landWalk(tiles[c.y][c.x])) { console.error(`spawn on non-land at ${c.x},${c.y}`); process.exit(1); }
    u.at = [r1(u.at.x), r1(u.at.y)];
  }
  return out;
}
const byId = id => nodes.find(n => n.id === id);
const coalition = byId('coalition'), hegemony = byId('hegemony');
// The Seam both of them border (an early Seam fight, per the design's pacing goal).
const shared = seams.find(s => s.between.includes('coalition') && s.between.includes('hegemony'));
const scenario = {
  schemaVersion: 2,
  seed: 3,
  ticks: 2400,
  map: { width: W, height: H, blocked: rects },
  veins: fields.flatMap(fl => fl.veins),
  units: [...formation(coalition, 'rifleman', 'lancer-mbt'), ...formation(hegemony, 'conscript', 'bulwark')],
  commands: [
    { tick: 1, faction: 0, type: 'attack-move', target: pt(shared.at) },
    { tick: 1, faction: 1, type: 'attack-move', target: pt(shared.at) },
  ],
};
writeFileSync(join(repo, 'content', 'scenarios', 'tri-point-30.json'), JSON.stringify(scenario, null, 1) + '\n');
console.log(`tri-point: ${W}x${H}, ${rects.length} blocked rects, ${scenario.units.length} units, ${fields.length} fields`);

// ---- skirmish start (T36): the canon opening, one MCV each on its main ---------------------
// economy.md: 5000 credits and one undeployed MCV. Unreal loads this without its commands; the
// commands are a scripted opening for the CI golden (both deploy, Coalition adds a reactor).
const centreOf = p => { const c = cell(p); return [c.x + 0.5, c.y + 0.5]; };
const ahead = (n, d) => { const f = unit(n.main, n.natural); return [Math.floor(n.main.x + f.x * d), Math.floor(n.main.y + f.y * d)]; };
const skirmish = {
  schemaVersion: 2,
  seed: 5,
  ticks: 900,
  map: { width: W, height: H, blocked: rects },
  veins: fields.flatMap(fl => fl.veins),
  units: [{ unit: 'mobile-command', at: centreOf(coalition.main) }, { unit: 'foundry-crawler', at: centreOf(hegemony.main) }],
  commands: [
    { tick: 1, faction: 0, type: 'deploy', unit: 0 },
    { tick: 1, faction: 1, type: 'deploy', unit: 1 },
    { tick: 205, faction: 0, type: 'place', building: 'coalition-reactor', at: ahead(coalition, 4) },
  ],
  assert: { buildingCountIs: { count: 3 } },
};
writeFileSync(join(repo, 'content', 'scenarios', 'tri-point-skirmish.json'), JSON.stringify(skirmish, null, 1) + '\n');
console.log(`tri-point-skirmish: MCVs at ${skirmish.units.map(u => u.at.join(',')).join(' / ')}`);

// ---- preview ------------------------------------------------------------------------------
if (previewPath) {
  const col = { '.': '#4f7a3a', '~': '#1f4e79', '#': '#6b6257', '=': '#a0784a' };
  const S = 6;
  let svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${W * S}" height="${H * S}" viewBox="0 0 ${W} ${H}" shape-rendering="crispEdges">`;
  for (let y = 0; y < H; y++) for (let x = 0; x < W;) {
    const t = tiles[y][x]; let x1 = x; while (x1 + 1 < W && tiles[y][x1 + 1] === t) x1++;
    svg += `<rect x="${x}" y="${y}" width="${x1 - x + 1}" height="1" fill="${col[t]}"/>`; x = x1 + 1;
  }
  const dot = (p, c, r = 0.6) => `<circle cx="${p[0]}" cy="${p[1]}" r="${r}" fill="${c}"/>`;
  for (const u of scenario.units) svg += dot(u.at, scenario.units.indexOf(u) < 30 ? '#e04030' : '#3070e0', 0.45);
  for (const fl of fields) for (const v of fl.veins)
    svg += `<rect x="${v.at[0] - 0.5}" y="${v.at[1] - 0.5}" width="1" height="1" fill="#d9b21c"/>`;
  for (const f of features) if (f.type === 'radar') svg += dot(f.at, '#ffd24a', 1.2);
  svg += dot(pt(C), '#ffffff', 1.0);
  svg += '</svg>';
  writeFileSync(previewPath, svg);
  console.log(`preview: ${previewPath}`);
}
