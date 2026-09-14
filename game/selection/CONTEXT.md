# game/selection — what's selected

`SelectionController`: pure selection state (EntityId ints, no sim refs). Click toggles with shift, box select replaces or adds, control groups 1–3 (`AssignGroup`/`GetGroup`), `Prune` drops dead ids. Input widgets live in game/orders; this class is headless-testable.
