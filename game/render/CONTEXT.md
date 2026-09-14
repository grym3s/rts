# game/render — sim → pixels

`UnitRenderer`: reads UnitStore (never writes), double-buffers positions per completed tick, interpolates with `Main.DrawAlpha`. Fix64→float happens only here (`ToWorld`). Selection rings + drag box drawn from callbacks Main supplies. 32 px per sim cell at zoom 1.
