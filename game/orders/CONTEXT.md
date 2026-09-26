# game/orders — input → Commands

`OrdersInput`: the ONLY place mouse/keyboard becomes intent. Emits `MoveCommand` / `AttackMoveCommand` (A held) / `StopCommand` (S) into Main's outbox; never touches sim state (rule 2). Camera rays map screen points onto the world X/Z ground plane; those coordinates convert to simulation X/Y only at this input edge. Box selection projects unit positions back to screen space. Control groups 1–3 (Ctrl assign, plain recall), hit-test last-spawned-first. Drag rectangle visuals live in `game/selection/SelectionOverlay.cs`.
