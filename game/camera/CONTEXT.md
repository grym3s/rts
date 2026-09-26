# game/camera — pan/zoom

`RtsCamera` (Camera3D): perspective high-angle RTS camera with fixed 52° elevation, arrow/edge-scroll pan, wheel zoom anchored to the ground point under the cursor, and clamping to the 48×32 test map. It exposes ground-plane ray intersection for input. Simulation X/Y corresponds to Godot world X/Z. WASD remains unbound (A and S are order hotkeys). No sim types here.
