# art/work — editable asset sources and generation intermediates

Owns: editable Blender sources, per-asset build scripts, generated image inputs, intermediate geometry/textures, and per-asset manifests. Keep provenance and generated-vs-manual authorship explicit.
Reads: `art/references/`, the relevant faction/readability docs, `art/asset-manifest.template.json`, and `docs/asset-pipeline.md`.
Writes: `work/<asset-id>/` only. Build scripts may export deliverables to the matching `art/export/<asset-id>/` folder through the documented pipeline.
Do NOT: modify reference originals, put runtime-only copies here, claim raster image generation produced a mesh, or replace a source file without preserving its prior version.
Change impact: `art/CONTEXT.md`, `docs/asset-pipeline.md`, and `art/export/CONTEXT.md` when deliverable contracts change.
