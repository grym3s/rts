# rifleman-hunyuan — generated single-view mesh experiment

Owns: one experimental Hunyuan3D image-to-mesh output, a Blender-only material study, the exact preprocessed input, generation settings, and inspection renders.
Reads: `art/references/elite-infantry-chat-render.jpg`; Hunyuan3D-2.1 installation under `/home/grymes/.local/opt/hunyuan3d-2.1-rocm`.
Writes: this folder only.
Tests: visual inspection and mesh integrity checks; this is art work, not game code.
Do NOT: overwrite the reference, reuse rejected v2 assets, treat this generated mesh as approved, or copy it into runtime assets.
Change impact: none until a reviewed asset is approved for export.
Known limits (2026-09-27): single-view shape generation cannot guarantee hidden-side accuracy, clean topology, rigging, or animation. The Hunyuan PBR pass caused an R9700 GPU hang and driver reset under a separate PyTorch 2.11 / ROCm 7.13 preview environment. A Qwen Image 2.1 rear view has since completed successfully on the R9700 through isolated ComfyUI PyTorch 2.14 / ROCm 7.2; see `design-references/back-view-execution-report.json` and its PNG. This validates that image-generation workload/runtime pair only; it does not establish that Hunyuan texturing is fixed. The blue visor in the material-study GLB was assigned heuristically in Blender, not generated as a texture. The detail study adds manually-authored plates but remains a visual study, not a production model. No asset here is approved for runtime use.
