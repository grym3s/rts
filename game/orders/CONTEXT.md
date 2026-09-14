# game/orders — input → Commands

`OrdersInput`: the ONLY place mouse/keyboard becomes intent. Emits `MoveCommand` / `AttackMoveCommand` (A held) / `StopCommand` (S) into Main's outbox; never touches sim state (rule 2). Screen↔world via viewport canvas transform; Fix64 conversion at this edge. Control groups 1–3 (Ctrl assign, plain recall), hit-test topmost-last.
