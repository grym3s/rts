# RTS Project Foundation — Ultra Review

Date: 2026-08-17 · Status: review, pre-implementation · Author: Claude (Fable 5) with Richard
Scope: engine, architecture, ICM-aware repository design, GitHub workflow, first vertical slice, risks, validation, next action.

> How this was produced. Ultraplan (`/gsd:ultraplan-phase`) could not run: this folder is not a git repository, has no `.planning/`, and ultraplan needs a GitHub repo. That is fine — the deliverable asked for is a review, not a plan import. This document was written locally after reading the **installed** ICM Architect skill in full (`SKILL.md`, `references/core.md`, `forms.md`, `system-map.md`, `augmentation.md`, all templates) and checking it against `github.com/RinDig/icm-architect` (main). The installed copy is a superset of main: it adds invariant 11 ("runs don't rewrite the factory; promotion does") and `augmentation.md`. Both agree on the six forms and the System Map method. Engine facts were checked against current sources (Aug 2026); see §3.

---

## 0. The short version

- **Build a deterministic, engine-independent simulation library first; put the engine around it.** That single decision buys testability for agents, reversibility of the engine choice, replays as a debugging tool, and a clean door to multiplayer/campaign later — without building any of those now.
- **Engine: Godot 4.x with C#/.NET** for presentation and input, with the simulation as a plain .NET class library tested headlessly with `dotnet test`. Unity is the credible runner-up; the sim-first architecture makes switching cheap. Unreal and Bevy are ruled out for this team and this game (reasons in §3).
- **ICM: the repository is a System Map subject with a thin factory.** Contracts (`CONTEXT.md`) live in the code folders they govern; a small `map/` shelf holds the catalog, shared-noun cards and the change-impact index; `decisions/` (ADRs) and `docs/` are the factory; GitHub Issues/PRs are the working state. No `.planning/` skeleton in the walkable tree.
- **Vertical slice: "Skirmish 30".** One map, one player faction, two unit types, ~30 vs ~30 units, select/box/move/attack-move/stop, obstacle pathfinding, avoidance, combat, death, camera, debug overlays, deterministic scenario runner + replay. No economy, production, fog, tech, factions, multiplayer.
- **Next action:** create the GitHub repo from the skeleton in §10 (about 20 files), then open the first three issues in §11.

---

## 1. Interpretation of the goal

What we are actually trying to build, in order:

1. **A decision instrument** — a small RTS that answers "is commanding units in *this* game satisfying?" quickly and honestly. If the answer is no, we want to have spent weeks, not months.
2. **A foundation that survives a yes** — code, repo, and workflow that can grow into a systemic RTS with a campaign layer *without* being rewritten, and that agents can safely extend.
3. **A way of working** — two humans and several agents contributing in parallel with low coordination cost, where knowledge accumulates in the repo rather than in chat history.

Assumptions I am **deliberately leaving unresolved** (and the architecture must not force them):

| Unresolved | Why leave it | Where the door is kept open |
|---|---|---|
| 2D vs 3D presentation | Art direction is unknown; the *simulation* is 2D either way (RTS sims are planar with height fields) | Sim has no rendering; presentation is a swappable layer |
| Multiplayer | Not needed for the fun test | Command-stream + deterministic tick makes lockstep possible later; not built |
| Campaign / persistence | Speculative | Sim state is plain data; scenario files are the seam |
| Factions/economy/tech | Design not started | Data-driven unit definitions; no faction abstractions in v1 |
| ECS framework | Premature | Sim is data-oriented (entity ids + component arrays) without a framework |
| Fixed-point vs float sim math | Real trade-off, see §8 | Recommendation: fixed-point *now* — this is the one exception; it is cheap on day one and brutal later |
| Modding | Speculative | Content is plain files; nothing else |
| GSD / other methodology tooling | See §5.6 | Kept out of the walkable factory |

Things in the brief I would **challenge**:

- *"Consider building formations, fog of war, resources, production, enemy AI…"* — the list is fine as a menu, but the fun question is answered by movement + targeting + combat *feel* (response latency, unit spacing, path quality, kill feedback). Everything else is a distraction until that feels good. §7 cuts hard.
- *ICM as "foundational requirement" vs "the repository itself is the workspace".* Agreed in spirit, with one correction from the framework itself: ICM's own System Map guidance says the map **cites** the source tree and never becomes a second spec, and "if the tree is small enough that one `CONTEXT.md` plus an index answers what-is-X and what-moves, stop there." A brand-new repo has ~zero nouns. So the right first state is a *catalog and contracts*, not a populated map. The map fills in as code appears (§5).
- *Ultraplan/GSD as the way to run this.* Two methodology skeletons in one repo (`.planning/` and ICM) is exactly the "duplicated, drifting context" the brief warns about. Pick one owner of working state (§5.6).

---

## 2. ICM assessment

### 2.1 What ICM says that applies here (from the current skill)

- The eleven invariants; the important ones for a codebase: one folder one job (1), small stable entry file (2), explicit per-folder contract (4), factory vs product (5), load only what the step needs (7), one home per fact / a link beats a copy (8), filesystem is the state machine (9), runs don't rewrite the factory — promotion does (11).
- Five-layer hierarchy: L0 `CLAUDE.md` (routing, 300–800 tokens) → L1 root `CONTEXT.md` → L2 folder `CONTEXT.md` (**the control point**) → L3 factory (rules, decisions, methods) → L4 product (per-run artifacts). Token band per task: 2k–8k.
- **System Map form** (`references/system-map.md`): for "a folder later agents must edit". A record library of nouns (object cards citing `path:line`), a short shelf of verbs (process cards), and an `effects/CONTEXT.md` change-impact index. Universes: live / leftover / ghost. Human-gated slices: inventory → catalog → nouns → verbs → impact → re-verify. "Do not scaffold objects/processes/effects for a dozen files." "Do not drop a map inside `src/`."
- **Augmentation** (`references/augmentation.md`): most workspaces need *no* method shelf; observations live with the run; promotion is human-gated; the giant instruction file is a named failure mode.
- Guardrails: don't over-structure; ICM loses at real-time multi-agent collaboration (which is not what a Git repo is — Git handles concurrency, ICM handles context).

### 2.2 The composition that fits a game codebase

The repeating units of work here are **(a) a change to a system** and **(b) a task run by a human or agent**. So:

| Layer | ICM role | Where | Form |
|---|---|---|---|
| Root `CLAUDE.md` (+generated `AGENTS.md`) | L0 catalog | repo root | — |
| Root `CONTEXT.md` | L1: how to walk the repo, the universes, name collisions (product word ↔ code name) | repo root | — |
| Per-system `CONTEXT.md` inside `sim/<system>/`, `game/<area>/`, `tools/<tool>/` | L2 contract: owns / reads / writes / tests / do-not-touch | **in the code folder** | System Map contracts co-located with the subject |
| `map/` | catalog + object cards for *shared* nouns (Unit, Order, Tick, MapGrid, Command stream) + `effects/CONTEXT.md` change-impact index | `map/` next to root | System Map |
| `decisions/` (ADRs) and `docs/` (design rules, conventions) | L3 factory: knowledge | `decisions/`, `docs/` | factory shelf |
| GitHub Issues + PRs, plus `work/` for scenario notes only if needed | L4 product: working state | GitHub | product |
| CI | mechanical gate | `.github/workflows/` | — |

Why this shape and not "a `map/` that documents everything":

- **A code folder is already a folder.** ICM's own invariant 4 says every working folder carries a contract. For a repo the *working folders are the system folders*. Putting the L2 contract in `sim/navigation/CONTEXT.md` means the agent that opens the folder to edit it lands on the contract for free, and the contract can be reviewed in the same PR as the code — which is what keeps it from going stale. This is not scattering *cards* through the tree (which the System Map warns against); cards stay in `map/`.
- **`map/` holds only what a folder cannot say about itself**: nouns that cross folders (a `Unit` is touched by movement, combat, selection, rendering), and the effects index ("changing the tick order → open these"). This is precisely the System Map's job: "what is X, what else moves if I change X."
- **ADRs are the promotion gate made concrete.** An agent that discovers something writes it in the PR description (observation, product). A human accepts it by merging a one-file change to `decisions/NNNN-*.md` or `docs/*.md` (promotion into factory). Superseding an ADR is a new ADR with `supersedes:` — stale decisions cannot stay silently authoritative because status is in frontmatter and the index is generated.
- **Method shelf: none at first.** The augmentation guidance is explicit — a shelf must be earned. When the team keeps rediscovering "the diagnostic order for a desync" three times, `docs/playbooks/` gets its first card. Not before.

### 2.3 What ICM does *not* need to do here

- Coordinate concurrent contributors — Git and PRs do that.
- Replace tests — the "human check" in a code contract is *the tests + a named manual check*, not prose review.
- Sequence a pipeline — there is no fixed 01→05 pipeline in software development; numbering is used only where order matters (the sim tick order is documented once, in `sim/CONTEXT.md`, and enforced in code).

Walk test for this repo (§9 has the checklist): cold agent opens `CLAUDE.md` → routes to `sim/navigation/CONTEXT.md` → reads `map/effects/CONTEXT.md` row for navigation → edits → runs `dotnet test` → reports. Three reads before touching code, well under 8k tokens.

---

## 3. Engine assessment

### 3.0 Facts checked (Aug 2026)

- **Godot**: 4.7.1 (.NET build) is the current stable, released 14 Jul 2026 ([godot-builds releases](https://github.com/godotengine/godot-builds/releases), [download page](https://godotengine.org/download/windows/)). C# **web export is still not officially supported**; it was proposed alongside .NET 10 for 4.6/4.7 and remains community/experimental ([proposal #13075](https://github.com/godotengine/godot-proposals/issues/13075), [forum thread](https://forum.godotengine.org/t/is-there-an-update-on-exporting-c-projects-to-web/128821)). Desktop C# export is fine. Verify the exact minor version at repo init and pin it.
- **Unity**: Runtime Fee cancelled Sept 2024 ([Unity blog](https://unity.com/blog/unity-is-canceling-the-runtime-fee)); Personal is free under US$200k trailing-12-month revenue, Pro required above that (to ~$25M) with a further 5 % Pro/Enterprise price rise from 12 Jan 2026 ([Unity pricing updates](https://unity.com/products/pricing-updates), [licence compliance](https://unity.com/pages/license-compliance)). Roughly $2.0–2.4k/seat/year for Pro. Not a blocker for us; it is a trust/volatility note.
- **Bevy**: 0.18 (Mar 2026) with an editor *preview*, 0.19 announced; editor still not something you block levels out in ([Bevy news](https://bevy.org/news/), [StraySpark 2026 guide](https://www.strayspark.studio/blog/bevy-rust-game-engine-2026-indie-guide)). API churn between minors persists.
- **Dust Front RTS** (rtsDimon, Steam, release TBA): classic RTS + grand-strategy map layer, procedural missions, three factions, asymmetric "outnumbered" battles; engine not stated on the store page ([Steam](https://store.steampowered.com/app/2610770/Dust_Front_RTS/)). Useful as inspiration for the *campaign feeds battles* door we keep open, not for v1 scope.
- **Godot, further**: 4.7 added C# hot-reload and wasm64 web exports; `--headless` is production-grade for CI; **gdUnit4 v6.2** supports GDScript + C#, JUnit XML output and ships an official GitHub Action ([gdUnit4](https://github.com/godot-gdunit-labs/gdUnit4)); Godot's built-in physics is float-based and **unsuitable for deterministic lockstep** — the standard answer is your own fixed-point sim, which is what §4 proposes ([GDQuest on determinism](https://school.gdquest.com/glossary/deterministic_simulation)). No public benchmark exists for NavigationServer avoidance at 1000+ agents — another reason to own navigation in the sim. godot-rust/gdext 0.5.x is a mature escape hatch if C# presentation ever becomes the bottleneck ([gdext](https://github.com/godot-rust/gdext/releases)).
- **Unity, further**: Entities/DOTS 1.4 (docs still `1.4.0-pre`) is widely treated as production-ready for RTS crowds; **Sanctuary: Shattered Sun** is Unity DOTS + HDRP, chosen for unit counts ([modding docs](https://sanctuaryshatteredsun.wiki/mechanics/modding/)). CI needs per-machine licence activation (`game-ci/unity-license-activate` is the usual workaround) — the main agent-loop friction ([Unity CLI tests](https://docs.unity3d.com/6000.3/Documentation/Manual/test-framework/run-tests-from-command-line.html), [game-ci](https://github.com/game-ci/unity-license-activate)). Unity-MCP exists.
- **Unreal**: 5.7 (Nov 2025), 5.8 imminent; Mass Entity still **Beta**; a community Mass RTS template advertises ~250 skeletal units at 60 fps ([roadmap](https://portal.productboard.com/epicgames/1-unreal-engine-public-roadmap/c/862-massentity-beta), [forum](https://forums.unrealengine.com/t/rts-unit-template-a-multiplayer-rts-framework-for-ue5-mass-entity-gas-250-units-60fps/2735388)). 5 % royalty above $1M (3.5 % on EGS). Binary `.uasset` — weakest agent fit.
- **Recoil Engine** (Spring fork; Beyond All Reason, Zero-K): proven at 10,000 units / 100 players, ARM64 added Jul 2026 ([recoilengine.org](https://recoilengine.org/)). Considered below as the "inherit a proven RTS engine" option.
- Shipped datapoints: Tempest Rising (UE5), Kaiserpunk (Unity), Sanctuary (Unity DOTS), BAR/Zero-K (Recoil). Dust Front's engine is deliberately unstated by its developer — do not assume.
- MCP/agent tooling exists for Godot (IvanMurzak/Godot-MCP, Coding-Solo/godot-mcp), Unity and Unreal; Godot and Rust have the cleanest text-first + CLI loops.

### 3.1 Candidates and verdicts

Evaluated against the criteria in the brief, weighted for *this* team: 2 humans, agents doing much of the typing, RTS with potentially large unit counts, need for headless deterministic testing, text-mergeable projects, low licence friction, and a possible later campaign layer.

| | Godot 4 + C# | Unity 6 (C#) | Unreal 5 | Bevy (Rust) | Custom (Rust/C++ + raylib/SDL) |
|---|---|---|---|---|---|
| Sim performance for 1–5k units | Sim is outside the engine (§4) → engine-neutral. Presentation via MultiMesh / servers is fine at this scale; GDExtension escape hatch | Same via plain C# assembly; DOTS/ECS available if presentation needs it | Mass Entity, but heavy | Excellent, ECS-native | Excellent, everything is yours |
| Headless automated testing | `dotnet test` on the sim with zero engine; `godot --headless` for scene/integration tests; gdUnit4/GUT | `dotnet test` on the sim; Unity Test Framework in batchmode works but is slower and licence-bound | Automation framework, slow iteration | Native, trivial | Native |
| Source control / mergeability | Text `.tscn/.tres`, `project.godot`; small binaries only | YAML if forced; scenes/prefabs merge badly without UnityYAMLMerge; `Library/` churn | `.uasset`/`.umap` binary; Blueprints not diffable | Pure text | Pure text |
| Agent compatibility | Open source, text-first, CLI build/run/headless, small API surface, MCP servers exist | Good C# tooling, but editor-driven state, closed engine, larger surface | Weak (binary assets, C++ compile loop) | Very good for code, no editor | Very good, but agents must also build tooling |
| Tooling / editor / debugging | Adequate; profiler, remote scene tree; weaker than Unity | Best-in-class | Best-in-class, heaviest | Minimal (early editor) | None |
| Licence / business | MIT | Free below revenue threshold; Pro seats above; runtime fee cancelled (2024) but licence has changed twice in three years | 5% royalty over $1M | MIT/Apache | none |
| Ecosystem for RTS specifics | Thin (nav server ok for tens–hundreds; you will write flow fields yourself either way) | Rich asset store; still write your own sim | Rich, but wrong weight class | Small | none |
| 2D/2.5D/3D flexibility | All three, cheap 2D | All three | 3D-first | 2D/3D | as built |
| Risk of the engine itself blocking us | Low–medium (C# web export still unofficial as of 4.7; desktop fine) | Low technical, medium licence/trust | Low technical, high friction | Medium (API churn between minors) | High (time sink) |

**Recoil (SpringRTS) — considered and set aside.** It is the only option already proven at 10k units, but you would inherit a large C++/Lua codebase, a Lua modding-style content model, and an engine whose architecture *is* the game architecture. That contradicts the brief (find the natural architecture, agent-walkable, small root context) and forecloses the ARPG/second-game option. If the fun test says "yes, and we want SupCom-scale battles as the identity of the game", it deserves a second look — noted in §3.2.

**Verdict: Godot 4.x with C#/.NET, simulation as an engine-independent .NET library.**

Why, in one paragraph: the thing that most determines whether agents can work safely here is *the validation loop* — can a change be built and checked from the command line in seconds without a human clicking? A pure .NET sim gives that unconditionally, and Godot is the engine that adds the least friction around it: open source, text project files, headless CLI, MIT, and small enough that an agent can hold the relevant API in context. Unity would also host the same sim and has better tooling and ecosystem, but costs more in merge friction, licence uncertainty, CI seat licensing, and editor-centric state that agents cannot see. Since the sim is engine-independent, **the engine choice is reversible at the cost of the presentation layer only**, which is deliberately thin in the vertical slice.

Why **C# and not GDScript**: static typing and a real test runner outside the editor; agents produce excellent C#; one language across sim, tools and presentation; the sim must not depend on the engine anyway.

Why **fixed-point sim math from day one** (the one "future" investment I recommend): deterministic replay is the most valuable debugging tool an agent-driven RTS project can have (a failing scenario is a file, not a description); float determinism across machines/compilers is not guaranteed; retro-fitting fixed-point touches every line of the sim. A `Fix64` struct + vector type is a few hundred lines and a day of work. Cross-machine determinism is a *bonus* (multiplayer/campaign door), not the reason.

### 3.2 What evidence would change the recommendation

- The collaborator has deep Unity experience and none in Godot → Unity becomes the recommendation (same sim architecture; presentation in Unity). Human familiarity beats marginal engine differences for a two-person team.
- We decide early on **3D with significant art fidelity** (PBR terrain, animated meshes, VFX-heavy) → Unity's tooling and store shift the balance; Godot 4 3D is workable but you author more yourself.
- Godot's C# export to a platform we need (e.g. web/mobile) is missing/broken at the time we need it → check at repo init; only web is at real risk and web is not a v1 target.
- Presentation performance in Godot for our target unit count fails a benchmark we run in the first two weeks (see §9) and GDExtension is judged too costly → revisit.
- Bevy ships a stable editor and slows its API churn → becomes viable for a sim-first team; not today.
- The fun test says the game's identity is **very** large-scale battles (thousands of units per side as the default experience) → Unity DOTS (Sanctuary's route) or Recoil become serious; the sim-first split still holds, so this is a presentation/engine swap, not a rewrite.

### 3.3 Not now / later / migration / defer

| Need | Now | Later | Would need migration | Safe to defer |
|---|---|---|---|---|
| Deterministic sim + command stream | ✔ | | (retrofit = rewrite) | |
| Fixed-point math | ✔ | | (retrofit = rewrite) | |
| Flow-field / large-count navigation | grid A* + local avoidance | flow fields | no (behind `sim/navigation/`) | ✔ |
| Fog/visibility | | ✔ | no | ✔ |
| Networking | | maybe | no if command stream exists | ✔ |
| ECS framework | | maybe never | no (sim is data-oriented already) | ✔ |
| 3D presentation | | maybe | presentation only | ✔ |
| Modding | | maybe | no if content is files | ✔ |

---

## 4. Technical architecture

### 4.1 Two halves and a seam

```
 input (mouse/keys)  ──►  Commands  ──►  ┌────────────────────┐
                                        │  sim (.NET lib)     │  fixed tick, deterministic,
 presentation (Godot) ◄── SimState ────  │  no engine refs     │  fixed-point, seeded RNG
 debug overlays       ◄── SimEvents ──   └────────────────────┘
 tools (headless)     ──► Scenario/Replay ──►  same sim
```

- **`sim/`** — a .NET class library. Owns all game state and rules. Advances by `Tick(commands[])` at a fixed rate (e.g. 20 Hz). Exposes read-only state and an event stream. Depends on nothing but the BCL. Fully unit-testable.
- **`game/`** — the Godot project. Owns camera, input, selection, UI, rendering, audio, debug overlays. Converts input to `Command`s; renders `SimState`; never mutates sim state directly. Runs the sim at fixed steps and interpolates for display.
- **`tools/`** — headless console apps over the sim: scenario runner (load scenario → run N ticks → assert/print), replay verifier, perf bench.
- **`content/`** — plain-text data: unit definitions, maps/scenarios. Read by sim and game.

The seam is the *only* rule that must never be broken: **`sim` never references Godot; `game` never writes sim state except through commands.** CI enforces the first (assembly reference check); code review enforces the second.

### 4.2 Inside the sim — natural boundaries, not speculative ones

Start with these folders (each with a `CONTEXT.md`), and only these:

| Folder | Owns | Depends on |
|---|---|---|
| `sim/core/` | `Fix64`, `FixVec2`, `Rng`, `EntityId`, tick loop, `Command` types, `SimState` container, events | — |
| `sim/world/` | map grid, terrain passability, spatial index (grid buckets) | core |
| `sim/units/` | unit definitions (data-loaded), unit components (position, health, faction, unit type), spawn/despawn | core, world |
| `sim/orders/` | order queue per unit: Move, AttackMove, Attack, Stop; command → order translation | core, units |
| `sim/navigation/` | pathfinding (grid A* now, flow fields later), local avoidance/steering, movement integration | core, world, units |
| `sim/combat/` | targeting/acquisition, weapons, damage, death | core, world, units |
| `sim/tests/` | xUnit tests + golden replay tests | all |

Not created yet (they become folders when they become code): `visibility/`, `economy/`, `production/`, `ai/` (beyond a trivial aggro rule living in `combat/`), `factions/`, `campaign/`, `persistence/`.

Design stance:
- **Data-oriented without a framework**: entity ids indexing into typed arrays per component group; systems are static functions run in a fixed order documented in `sim/CONTEXT.md`. This is 80 % of an ECS's benefits with none of the framework lock-in; a real ECS can be adopted later if profiling says so.
- **Everything the player does is a `Command`** (tick-stamped, faction-stamped). AI and tests issue commands through the same door. Replays are command logs + scenario seed.
- **Events out, not callbacks in**: the sim emits `UnitDied`, `ProjectileFired`, etc. into a per-tick buffer; presentation consumes them. No presentation code inside the sim.

### 4.3 Presentation

- Godot 4.x, **2D top-down** for the slice using placeholder shapes/sprites (art cost ~zero; the fun question is about behaviour). 3D remains open — sim doesn't care.
- Rendering many units: `MultiMeshInstance2D` or direct `RenderingServer` use once counts exceed a few hundred; ordinary nodes are fine for the slice's ~60 units.
- Debug overlays are first-class from day one (§9): paths, avoidance vectors, target lines, unit ids/state, tick counter, sim step time.

---

## 5. ICM-aware repository architecture

### 5.1 The tree (target shape once the slice is under way)

```
rts/                                   ← repo root = subject of the map
├─ CLAUDE.md                           L0: identity + routing table (< 60 lines). AGENTS.md generated from it.
├─ CONTEXT.md                          L1: how to walk; the two halves; name collisions; universes
├─ README.md                           humans: what this is, how to run it (points at CLAUDE.md for structure)
├─ map/                                System Map shelf — cites code, never restates it
│  ├─ CLAUDE.md                        catalog: what nouns/effects exist; how cards are verified
│  ├─ CONTEXT.md                       universes (live/leftover/ghost) + collisions ("Unit" = `UnitId`+arrays, not a class)
│  ├─ _meta/schema.md                  card types, statuses, naming
│  ├─ _templates/{object.md,process.md}
│  ├─ objects/_index.md                one line per shared noun; generated
│  ├─ objects/<card>.md                only cross-folder nouns: unit, command, order, tick, map-grid, scenario
│  ├─ processes/                       created only when a real movement exists (e.g. tick, load-scenario)
│  └─ effects/CONTEXT.md               "changing X → open these contracts/cards/tests"
├─ decisions/                          factory: ADRs, one per file, frontmatter status; _index.md generated
├─ docs/                               factory: durable design rules & conventions (short files, one topic each)
│  ├─ CONTEXT.md                       what lives here vs decisions/ vs map/
│  ├─ game-design-pillars.md           the few rules the fun test is measured against
│  ├─ conventions.md                   code style, naming, commit/PR conventions
│  └─ workflow.md                      branches, PRs, review, CI, agents
├─ sim/                                .NET class library (see §4.2); CONTEXT.md per system folder
├─ game/                               Godot project; CONTEXT.md per area (input, camera, selection, render, debug, ui)
├─ tools/                              scenario runner, replay verify, bench; CONTEXT.md each
├─ content/                            units/*.json, maps/*.json, scenarios/*.json (+ CONTEXT.md schema pointer)
├─ _scripts/                           regenerate indexes (map/objects/_index.md, decisions/_index.md, AGENTS.md); CI helpers
├─ .github/                            workflows, PR/issue templates, CODEOWNERS
├─ .gitattributes / .gitignore / .editorconfig
└─ RTS.sln
```

### 5.2 How the six things relate

| # | Thing | Where it lives | Relation |
|---|---|---|---|
| 1 | Runtime architecture | the sim tick order and the sim/game seam | described *once* in `sim/CONTEXT.md` and `CONTEXT.md`, enforced by code + CI |
| 2 | Source organisation | `sim/<system>/`, `game/<area>/` | folder = system; the runtime boundary and the folder boundary are the same thing on purpose |
| 3 | ICM navigation | `CLAUDE.md` → `CONTEXT.md` → folder `CONTEXT.md` → `map/effects` | routes to 2; never duplicates 2 |
| 4 | Dev workflow | `docs/workflow.md` + `.github/` | one page, enforced by branch protection & CI |
| 5 | Knowledge/reference | `decisions/`, `docs/` | factory; changes by PR = promotion gate |
| 6 | Working state | GitHub Issues/PRs (+ branch names) | product; not mirrored into the repo |

They do not mirror exactly; 2 and 1 are made to coincide (that's the architecture), 3 is a thin catalog over 2, 5 is stable, 6 lives outside the tree.

### 5.3 The per-folder contract for code (L2)

Every `sim/<system>/`, `game/<area>/`, `tools/<tool>/` folder gets a `CONTEXT.md` of ~20–40 lines:

```markdown
# sim/navigation — get units from A to B without walking through each other

Owns: pathfinding (grid A*), local avoidance, movement integration.
Reads: world grid (../world), unit positions/orders (../units, ../orders).
Writes: unit positions & velocities only. Never health, never orders.
Runs at: tick step 3 (see ../CONTEXT.md).
Tests: ../tests/Navigation*.cs; golden replay content/scenarios/nav-*.json
Do NOT touch from here: combat, presentation. If you need a new unit field, add it in ../units and cite it here.
Change impact: see /map/effects/CONTEXT.md#navigation
Known limits / leftover / ghost: (kept short; dated)
```

Reviewed in the same PR as the code; CI fails if a folder in `sim/` or `game/` lacks one.

### 5.4 Keeping documentation from becoming a second stale codebase

- Contracts describe **boundaries and where to look**, not behaviour. Behaviour lives in code and tests. If a contract starts explaining an algorithm, that text belongs in a comment at the algorithm.
- Cards in `map/` cite `path:line` and carry `status: verified` only with a commit hash; the `_index.md` is generated by `_scripts/`, never hand-edited.
- One home per fact: tick order in `sim/CONTEXT.md`; unit stats in `content/units/`; workflow in `docs/workflow.md`; the design pillars in one file. `CLAUDE.md` links, it does not restate.
- ADR supersession is explicit (`status: superseded`, `superseded_by:`), and the generated `decisions/_index.md` shows only accepted ones at the top.
- PR template asks: *which folders' CONTEXT.md did this change affect, and did you update them?* That is the maintenance mechanism; it is small and it happens where the change happens.

### 5.5 Root `CLAUDE.md` (sketch, ≈45 lines)

```markdown
# <Game name> — RTS

A real-time strategy game. Deterministic simulation in `sim/` (.NET, no engine), presented by Godot in `game/`.
Built on ICM: folders carry architecture, each working folder has a CONTEXT.md, the map cites code. Load only what the task needs.

## Where things live
| Folder | Holds |
| `sim/` | game rules & state, tick-based, engine-free — the thing that matters |
| `game/` | Godot presentation, input, camera, UI, debug overlays |
| `tools/` | headless scenario runner, replay verify, bench |
| `content/` | unit/map/scenario data (plain JSON) |
| `map/` | System Map: shared nouns + change-impact index |
| `decisions/` | ADRs (accepted decisions; superseded ones marked) |
| `docs/` | design pillars, conventions, workflow |

## Route by task
| If asked to… | Read first | Then |
| change unit movement/pathing | `sim/navigation/CONTEXT.md`, `map/effects/CONTEXT.md#navigation` | edit, `dotnet test`, run nav scenarios |
| change combat/targeting/damage | `sim/combat/CONTEXT.md`, effects#combat | same |
| add a unit type | `content/CONTEXT.md`, `sim/units/CONTEXT.md` | data first; code only if a new behaviour |
| change how it looks/controls | `game/CONTEXT.md` → the area | run `godot --headless` scene tests + manual check |
| answer "what does changing X hit" | `map/effects/CONTEXT.md` | open the named cards |
| record a decision | `decisions/CONTEXT.md` | new ADR by PR |
| status of a task | GitHub issue/PR | — (not stored here) |

## Rules that are not negotiable
1. `sim/` never references Godot. 2. Presentation changes sim only via Commands. 3. Update the CONTEXT.md of any folder whose boundary you changed, in the same PR. 4. Don't load `map/objects/` wholesale — use the index.
```

### 5.6 GSD, `.planning/`, and other agent methodologies

Recommendation: **do not initialise a `.planning/` tree in this repo.** It would be a second catalog with its own state files, and the brief's stated risk (duplicated, drifting context) applies. If you want GSD's phase/plan machinery, run it *outside* the walkable tree (a sibling folder or a `.gsd/` that CI and `CLAUDE.md` ignore) and treat its outputs as product that gets promoted into `decisions/`/issues by hand. Working state has one home: GitHub Issues/PRs.

---

## 6. GitHub collaboration model

Proportionate for 2 humans + agents:

- **One repository**, monorepo of `sim/`, `game/`, `tools/`, `content/`. Splitting sim into its own repo is possible later (it is already a separate assembly) but coordination cost is not worth it now.
- **Trunk-based, short-lived branches.** `main` protected: PR required, 1 approving review, CI green, linear history (squash merge). No direct pushes, humans included.
- **Branch names**: `feat/<system>-<slug>`, `fix/…`, `agent/<who>/<slug>` for agent-created branches. Cheap, greppable, tells you who to blame.
- **Review rules**: a human reviews every PR that touches `sim/core/`, `sim/CONTEXT.md`, `decisions/`, or the seam; anything else may be reviewed by an agent (Claude Code review / Copilot review) plus CI, with the human as approver of record. Agents can open PRs; agents never merge.
- **Issues are the task queue.** Issue template asks for: system(s) affected (folder names), the observable outcome, how to validate. Labels = folder names (`sim/navigation`, `game/selection`) — the same vocabulary as the tree.
- **PR template**: what changed, which CONTEXT.md files were touched/why not, tests run (`dotnet test`, scenarios, headless scene tests), any observation worth promoting (a candidate ADR/doc line). That last field is invariant 11 in practice.
- **Commits**: conventional-ish (`feat(nav): …`, `fix(combat): …`); scope = folder. Squash-merge makes this cheap.
- **CI (GitHub Actions)**: (1) `dotnet build` + `dotnet test` on `sim/` and `tools/` — fast, no engine, runs on every push; (2) Godot headless job: import project, run scene/integration tests, export a Linux build as an artifact — runs on PR; (3) structure checks from `_scripts/`: every `sim/*`, `game/*` folder has a `CONTEXT.md`, generated indexes are up to date, `sim` has no engine references, formatting (`dotnet format`), `.gitattributes` LFS patterns match binaries.
- **Git LFS** from day one for `*.png *.jpg *.wav *.ogg *.ttf *.glb` etc.; keep the slice's binaries tiny. `.gitattributes` also forces `text eol=lf` for `.tscn/.tres/.cs/.json`.
- **Godot merge hygiene**: one scene per feature/area, small scenes; no giant "main.tscn"; resources as `.tres` text; UIDs committed. Avoid two people editing one scene in parallel — ownership per `game/<area>/`.
- **CODEOWNERS**: optional; can name a human for `sim/core/` and `decisions/`.
- **Releases**: not needed yet; CI export artifact per PR is the "build". Tag `v0.1-slice` when §7 is done.
- **Reproducible dev env**: `global.json` pins .NET SDK; a documented Godot version (and `godotenv` or a pinned download in CI); a `Makefile`/`justfile` with `test`, `run`, `bench`, `scenario` targets so agents have one command vocabulary.

---

## 7. Vertical slice — "Skirmish 30"

The question: *is selecting units, telling them where to go, and watching them fight satisfying?* Everything below serves that; nothing else is built.

**In:**
- One hand-authored map (grid, some impassable blocks and chokepoints), 2D top-down, placeholder visuals.
- Two unit types with different feel: fast/fragile (short range) and slow/tough (longer range). Data-defined.
- Player: ~30 units. Enemy: ~30 units with a trivial rule (hold position; acquire and chase targets within range; return). No production.
- Camera: pan (edge/keys/drag), zoom. Selection: click, shift-add, box select, control groups (1–3) — control groups are cheap and central to feel.
- Orders: Move, Attack-Move, Attack target, Stop; queued with shift. Order feedback (marker, ack).
- Navigation: grid A* + local avoidance/steering; group move keeps rough cohesion (arrive-and-spread), *no* formal formations.
- Combat: target acquisition, ranged attack (hitscan or simple projectile — pick hitscan for the slice), health, death, corpse removal.
- Win/lose: eliminate all enemies / lose all units. Restart.
- **Debug & tooling (non-negotiable for the slice)**: overlays for paths, targets, unit ids/state; tick counter and sim step ms; F-keys to toggle; headless scenario runner; replay record/playback; a `bench` scenario with 500 units to test scale early.

**Deliberately out:** economy/resources, production/buildings, fog of war, tech, factions, formations, projectiles with travel (unless hitscan feels flat), veterancy, multiplayer, campaign, save/load, sound (beyond a click), real art, menus beyond restart, modding, any AI beyond the aggro rule.

**Success criteria for the fun test** (written into `docs/game-design-pillars.md` and judged by both humans): orders acknowledge within one frame; groups arrive without stringing out or clumping; units don't jitter or push through each other; targeting is legible; fights are decided by positioning, not RNG; a 30v30 engagement is readable. If two of these fail after tuning, the direction changes.

Approximate scope: 3–5 weeks of part-time work with agent help. If it takes materially longer the architecture is too heavy — that is a signal, not a schedule slip.

---

## 8. Risk register

**Genuine architectural risks (act now)**

| Risk | Why it hurts later | Mitigation now |
|---|---|---|
| Sim/presentation coupling creeps in (a Godot type in sim, a direct state write from UI) | kills headless testing, replays, determinism, engine reversibility | assembly boundary + CI reference check + review rule |
| Float non-determinism | replays diverge; later multiplayer impossible; heisenbugs | fixed-point (`Fix64`) from day one; seeded RNG; golden replay tests in CI |
| Navigation architecture doesn't scale (per-unit A* × hundreds) | rewrite of the most tuning-sensitive system | keep A* behind `sim/navigation/`; run the 500-unit bench in week 2; flow fields are the known next step |
| Godot C# platform gaps or version churn | blocked export / breaking upgrade | pin Godot version; verify C# export for desktop at init; web/mobile not v1 |
| Scene/asset merge conflicts | lost work, fear of parallel work | small scenes per area, text resources, LFS, ownership per folder |
| Contracts/map go stale | agents act on wrong boundaries | contracts live with code and are reviewed in the same PR; CI presence check; cards carry commit hash |
| Two methodology trees (`.planning/` + ICM) | duplicated drifting context | one home for working state (GitHub); §5.6 |
| Content schema without versioning | every unit JSON breaks on a field rename | `schemaVersion` in content files from the first file |

**Hypothetical / future concerns (note, don't build)**

- Lockstep networking, desync detection, input delay — the command stream + determinism keep the door open; do nothing else.
- Campaign persistence and army carry-over — sim state is plain data; scenario files are the seam; do nothing else.
- Full ECS / job-system parallelism — profile first; the data-oriented layout makes it adoptable.
- Modding — content is files; do nothing else.
- Unity/Unreal migration — presentation only; do nothing.

---

## 9. Validation strategy

"Working" means: tests pass, scenarios pass, replays match, the bench meets budget, and a human says the feel criteria hold.

1. **Unit tests** on the sim (xUnit) — math (`Fix64`), grid, pathfinding, order translation, targeting, damage. Run on every push in < 1 minute.
2. **Scenario tests** — `content/scenarios/*.json` + expected outcomes (`tools/scenario`): "30 units move through chokepoint, all arrive within N ticks, no overlap > tolerance"; "10 v 10, side A wins with ≥ K survivors" (checks balance stays in a sane band). Agents write one per behaviour they add.
3. **Golden replays** — recorded command logs + final state hash; CI re-runs and diffs. Any change to sim behaviour must update the golden with an explanation in the PR (this is the desync tripwire).
4. **Perf bench** — `tools/bench` runs 500 and 2000 units for 1000 ticks headless; CI reports ms/tick and fails on a regression above a threshold. Presentation bench: 500 units on screen in Godot at ≥ 60 fps on both devs' machines (manual, recorded in the PR).
5. **Godot headless scene tests** — scenes load, input → command mapping works, the debug overlays exist (gdUnit4 or a small C# harness).
6. **Structure checks** (`_scripts/check.sh`) — CONTEXT.md presence, generated indexes fresh, no engine refs in `sim/`, LFS attributes.
7. **Human check** — the pillar criteria in §7, played by both humans, written up as a short dated note in the PR that closes the slice; if a pillar fails, an issue is opened against the system named in the note.
8. **Walk test for the repo** (repeat at each milestone, by a cold agent): open `CLAUDE.md`; take a task; reach the right folder contract in ≤ 2 more reads; state what the change hits from `map/effects`; make the change; validate with one command; report. Token cost of that context ≤ 8k. If any step fails, move/split files rather than adding explanation.

---

## 10. Proposed first repository state

Immediately after `git init` + ICM setup — the smallest workspace that carries the work (≈ 22 files, no placeholders for systems that don't exist yet):

```
rts/
├─ CLAUDE.md                        routing (§5.5)
├─ CONTEXT.md                       how to walk; the sim/game seam; universes
├─ README.md                        run/test instructions
├─ .gitignore  .gitattributes  .editorconfig  global.json  RTS.sln
├─ justfile (or Makefile)           test / run / scenario / bench / check
├─ decisions/
│  ├─ CONTEXT.md                    how ADRs work; statuses
│  ├─ 0001-sim-is-engine-independent.md
│  ├─ 0002-engine-godot4-csharp.md
│  ├─ 0003-fixed-point-deterministic-sim.md
│  └─ 0004-repo-is-icm-system-map-subject.md
├─ docs/
│  ├─ CONTEXT.md
│  ├─ game-design-pillars.md        the §7 feel criteria
│  ├─ conventions.md
│  └─ workflow.md                   §6 in one page
├─ map/
│  ├─ CLAUDE.md                     catalog (stub lines for unit, command, order, tick, map-grid, scenario)
│  ├─ CONTEXT.md                    universes + collisions
│  ├─ _meta/schema.md
│  ├─ _templates/object.md, process.md
│  └─ effects/CONTEXT.md            initially: the seam rule + "navigation/combat/units → open their CONTEXT.md"
├─ sim/  Sim.csproj, CONTEXT.md (tick order), core/CONTEXT.md, core/Fix64.cs, core/Tick.cs (skeleton), tests/Sim.Tests.csproj + Fix64Tests.cs
├─ game/ project.godot, Game.csproj, CONTEXT.md, main.tscn (empty world + camera)
├─ tools/ scenario/ (Program.cs, CONTEXT.md)
├─ content/ CONTEXT.md, units/scout.json, units/tank.json, scenarios/smoke.json
├─ _scripts/ check.sh, gen-indexes.sh
└─ .github/ workflows/ci.yml, PULL_REQUEST_TEMPLATE.md, ISSUE_TEMPLATE/task.md
```

`map/objects/` and `map/processes/` are **not** created yet — the System Map guidance forbids empty shelves; the catalog carries stub lines until the nouns exist in code (first fill after the slice's core loop lands, as a gated slice).

---

## 11. Recommended next action

1. **Confirm the two human decisions** this review cannot make: (a) Godot 4 + C# vs Unity — only your collaborator's experience should overturn it; (b) fixed-point from day one — I recommend yes.
2. **Install tooling**: .NET SDK (LTS), Godot 4.x .NET build, pin both versions in `global.json` / `docs/workflow.md`. (Neither is on this machine yet.)
3. **Create the repository** at the §10 state — public or private on GitHub, `main` protected, LFS enabled, CI green on the empty sim. Do this as one PR from a bootstrap branch so the workflow is exercised once by a human before any agent touches it.
4. **Open the first three issues** (each is one agent-sized PR):
   - `sim/core`: `Fix64`/`FixVec2` + tests + seeded RNG + tick loop + `Command` types.
   - `sim/world` + `sim/navigation`: grid, A*, movement, avoidance; scenario "chokepoint-30" + golden replay; overlays in `game/debug`.
   - `game/`: camera, click/box selection, move/attack-move orders wired to commands; render units from sim state.
   Then `sim/combat` and the enemy rule; then the bench; then the fun test.
5. **Run the walk test** on the bootstrap repo with a cold agent before issue 1 is started; fix structure, not prose.
6. If you still want GSD/ultraplan for phase planning after that, run it against the GitHub repo and keep its `.planning/` out of the walkable tree (§5.6).
