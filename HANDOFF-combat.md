# HANDOFF — sim/combat slice (issue #19) — 2026-09-14

**Branch:** `ai/rts/feature/sim-combat` · **worktree:** `~/src/rts/sim-combat` · cut from main @ `0c86781` (#18)
**Status:** code complete, core gates green, NOT yet committed. Session hit iteration limit mid final-gate.

## What this slice does (issue #19)
Health + attack orders + deterministic weapon system + deaths, from catalog data, plus a pinned
skirmish scenario. Closes #19 when merged. Out of scope (per issue): buildings, projectiles as
entities, armor multipliers, fog, production.

## Changes inventory (all uncommitted on the branch)

### sim/
- `sim/world/UnitStore.cs` — REWRITE: `Unit` gains combat fields (`Faction`, `Hp` (settable),
  `MaxHp` (get-only, set from profile at spawn), `Sight`, `Damage`, `Range`, `CooldownTicks`,
  `CooldownRemaining`, `TargetId` (EntityId.None = none), `AttackMoving`). New `UnitProfile` record
  (immutable stat block, Faction/Hp/Sight/Damage/Range/CooldownTicks). `Spawn(..., UnitProfile? = null)`
  (null → hp 100, faction 0, unarmed — keeps old tests compiling). `DespawnDead()` (removes Hp<=0,
  store order). `Find(EntityId)`. **`Hash()` now mixes `u.Hp.Raw`** ← this is why every existing
  golden hash moved (intended; HP is fight state).
- `sim/units/UnitCatalog.cs` — `SpawnParams` extended: `+ Faction` (json `faction=="hegemony"` → 1, else 0),
  `+ Sight` (stats.sight, 0 default), `+ Damage/Range/CooldownTicks` from optional `weapon` block.
  All via `Fix64.FromDouble` (no implicit double→Fix64 exists — gotcha).
- `sim/orders/OrderSystem.cs` — `ApplyCommands(orders, dueCommands, Func<EntityId,Unit?>? unitOf = null)`.
  Now handles: MoveCommand (as before), **AttackMoveCommand** (MoveOrder + sets `AttackMoving=true`),
  **AttackCommand** (sets `TargetId`, no MoveOrder — combat issues chase), **StopCommand** (removes
  order, clears TargetId + AttackMoving). `unitOf` resolves ids to Units; passed as `units.Find`.
- `sim/combat/CombatSystem.cs` — NEW. `Step(units, orders, tick)`, tick step 4, store order, no RNG/floats:
  1. skip units with Hp<=0 (killed earlier this tick);
  2. if `AttackMoving` and target not live → `Acquire()` = nearest enemy (LengthSquared > sight²) ties→lowest id;
  3. decrement `CooldownRemaining`;
  4. no target / no damage → skip. Dead/gone target → clear (attack-mover keeps moving; plain attacker `orders.Remove`);
  5. `dist <= Range + a.Radius + b.Radius` → on cooldown expiry `target.Hp -= Damage`, reset cooldown;
  6. out of range and NOT attack-moving → issue/refresh chase `MoveOrder(target.Position)` if absent or target moved
     (navigation does the walking; first-shot lands SAME tick as the order when already in range).
- `sim/combat/CONTEXT.md` — NEW (folder rules). `sim/CONTEXT.md` — folder table: world/units/orders/navigation live, combat live.
- `sim/navigation/NavigationSystem.cs` — removed `EMPTYPATH` stderr spam (empty path = already at target, silent order removal). `UNREACHABLE` still logs.
- `sim/tests/CombatTests.cs` — NEW, 5 facts. **World() helper wires 4 systems: Order, Navigation (24x24 empty GameMap), Combat, DespawnDead — chase needs navigation or units never move.**
  - `AttackOrdersKillTargetOnCooldownTicks`: dmg10/hp25: after order tick Hp=15 (same-tick first shot!), +11 steps still 15, +1 step = 5, then despawn leaves 1 unit.
  - `ChaseThenFireWhenOutOfRange`: GOTCHA — radius-1 units spawned at (0,0) can NEVER move (wall-slide rejects every step; `IsPointWalkable` with radius). Spawn ≥2 cells from walls (a at (2,2), b at (12,2)).
  - `AttackMoveAcquiresNearestEnemyAndResumesRoute`: dmg60 one-shots; after kill+cleanup: TargetId=None, order.Target==dest, AttackMoving still true.
  - `StopClearsTargetAndOrder`.
  - `DeadUnitsHashStablyAcrossReplays`: mutual attack ×40 steps → identical `StateHash()`.

### content/
- `content/scenarios/skirmish-10v10.json` — NEW: 32×24 open map, seed 7, 900 ticks, 10 riflemen faction0 at (4–6,6–9) (json order = ids 0–9), 10 conscripts faction1 at (26–28,15–18) (ids 10–19), tick-1 mutual attack-move to enemy masses. Result: **`units=8 hash=268b437ce4f27416`** (riflemen win 8–2; rifleman 60hp/8dmg/4.0r vs conscript 55hp/7dmg beats HP-per-sec).

### .github/workflows/ci.yml
- chokepoint-30 pin `d7f1b66ed66d42a0` → **`1727e6d2adb5efb8`** (Hp-in-hash; observed live)
- ADDED skirmish-10v10 pin **`268b437ce4f27416`**

### game/
- `game/Main.cs` — spawns 6 riflemen (faction 0, x4–6,y12–13) + 6 conscripts (faction 1, x38–40) — map is 48×32 w/ wall rects at x20 — profiles wired. Systems: Order(+units.Find)→Navigation→Combat→DespawnDead. `_selection.Prune` each frame. Renderer `TargetPos` callback for attack lines. **Smoke rewritten**: attack-move faction-0 squad toward (40,16), loop ≤900 ticks until faction-1 count==0, PASS = moved>1 cell AND enemies wiped, prints "SMOKE PASS unit0 moved X cells, enemy squad wiped".
- `game/render/UnitRenderer.cs` — `TargetPos` callback prop; HP bar (dark bg + green/yellow/red by 0.6/0.3 thresholds) when Hp<MaxHp; red target line to `TargetPos(TargetId)`.
- `game/orders/OrdersInput.cs` — right-click ON enemy unit → `AttackCommand` (via HitTest + new required `EnemiesOf` callback); A+click attack-move unchanged; `SelfFactionId=-1` sentinel.
- NOTE OrdersInput/Main use `required` props + `Action<...>?` fields (not events — assignment from Main), Godot 4.7 C#: `OS.HasCmdlineUserArg`/`GetTicksMsec` DON'T exist; `OS.GetCmdlineUserArgs()` (plural) does; `Key._1` doesn't — use `(Key)'1'` char casts.

## Verification ledger (fresh, this branch)
| Gate | Result |
|---|---|
| `dotnet build RTS.sln -warnaserror` | ✅ 0 errors/warnings |
| `make test` | ✅ 44/44 (39 prior + 5 CombatTests) |
| `make check` | ✅ structure passed |
| skirmish run | ✅ hash 268b437ce4f27416, units=8 |
| chokepoint run | ✅ hash 1727e6d2adb5efb8 (new pin) |
| `make fmt` | ran (post-edit re-run pending) |
| `make godot-test` | ⚠️ NEVER COMPLETED on this branch — 400s combo cmd timed out before it printed. MUST rerun (background, notify). Watch for: new SMOKE combat assertion (could loop 900 ticks — fine headless) + godot import slowness. |

## Remaining steps (next session, in order)
1. `cd ~/src/rts/sim-combat && export DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:$PATH DOTNET_ROLL_FORWARD=LatestMajor` (godot 4.7.1-mono at ~/.local/bin/godot)
2. `make fmt` then `make godot-test` (background+notify, timeout ≥600s). Expect `SMOKE PASS ... enemy squad wiped`.
3. If smoke fails: 6v6 at x4 vs x38 across a 48-wide map with wall gap at y13–18 — check units path through gap (blocks are `BlockRect(20,0,21,12)` + `(20,19,21,31)`, gap y13–18) — riflemen speed 1.8, 900 ticks ≈ enough; if not, widen loop or move spawns.
4. `make check` after any CONTEXT touch.
5. Commit message suggestion: `feat(sim/combat): health, weapons, attack orders, deaths; skirmish-10v10 golden` + game bullets. Push branch, `gh pr create -R grym3s/rts --fill` referencing `Closes #19`.
6. `gh pr checks <n>` → both green → **`gh pr merge <n> --squash --delete-branch`** (owner standing order 2026-09-14: self-merge green PRs; repo is squash-only). Verify issue #19 CLOSED + main CI green after.
7. Update director state: `~/.hermes/projects/rts/rts-continuous-improvement/` — RESUME.md, CYCLE_LOG.md (cycle 2), CURRENT_STATE.md, BACKLOG.json (issue-3 done, add issue-19 done, next candidate below).
8. Delete this HANDOFF file in the same commit that merges… actually it can ride into main and get removed by the next docs sweep; low priority.

## Next-slice candidates (after #19)
- Right-click attack feedback: fire flashes/marks (`marksOnHit` verb exists in content JSON), death anim placeholder, kill-feed in overlay.
- Armor-type multiplier table (damageType vs armor, simple 3×3) — playbook says spread/warheads later.
- Production/economy tick step 2 (harvester "vulnerable spatial income" per playbook) — big slice, own issue.
- Vendoring gdUnit4 for real game-layer unit tests (smoke harness covers seam today).
- Control-group UI feedback (group box on select), shift-queue (Command.Queue plumbed but unused).

## Environment gotchas (recap)
- dotnet: `DOTNET_ROOT=~/.dotnet`, `PATH=~/.dotnet:...`, `DOTNET_ROLL_FORWARD=LatestMajor` (SDK 10.0.401).
- Godot: use `~/.local/bin/godot` (4.7.1-mono); pacman godot is NON-mono. `make godot-test` = import + run + smoke grep.
- xUnit analyzer rejects `Assert.Equal(actual, literal)` — constant FIRST.
- Fix64: no implicit double conversions; `Fix64.FromDouble/FromInt/Ratio`; `FixVec2.Length/LengthSquared`; comparison on `.Raw` for ints.
- Tirith blocks compound destructive commands (rm+branch-delete+curl chains) — keep those single and explicit.
- Worktrees: `~/src/rts` (main), `~/src/rts/sim-combat` (this), stale `~/src/rts-wt/sim-nav` (PR #17 merged — `git worktree remove` it when convenient; also `~/src/rts-wt/sim-game3` is a STRAY non-worktree dir with duplicate game/*.cs from a path mistake — safe to delete).
