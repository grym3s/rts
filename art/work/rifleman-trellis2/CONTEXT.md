# rifleman-trellis2 — high-detail image-to-3D candidate

Owns: isolated TRELLIS.2 image-to-3D generation from the canonical elite-infantry render, its local R9700/ROCm build notes, candidate GLBs, and review renders.
Reads: `art/references/elite-infantry-chat-render.jpg`; provisional rear, left-side, and battle-wear views under `../rifleman-hunyuan/design-references/`; `art/CONTEXT.md`; and `docs/readability.md`.
Writes: this folder only. The TRELLIS.2 source, weights, and virtual environment are outside the game repository under `/home/grymes/.local/opt/`.
Workflow: `generate_trellis2.py` runs the official 1024 cascade on the isolated R9700/ROCm environment, exports a PBR GLB, and writes a provenance/runtime report. Then inspect geometry/materials in Blender and review multiple angles before any promotion.
Do NOT: overwrite references, present generated geometry as canon or production-ready, reuse rejected `rifleman-v2` geometry, or copy candidates to runtime assets before visual review.
Change impact: `art/CONTEXT.md`; export/runtime contracts only after explicit promotion.
Known limits (2026-09-27): the R9700-specific TRELLIS.2 Python stack and native operators are still being prepared in isolated paths. The generation runner is written but has not executed; no candidate GLB exists yet. The generated additional views and battle-wear image are guidance only and can diverge from the approved front render.
