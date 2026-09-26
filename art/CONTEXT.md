# art — source concepts and editable game assets

Owns: visual references, provenance, editable Blender sources, generation work, and approved runtime exports. It does not own faction canon or game/simulation behavior.
Reads: `docs/factions/`, `docs/readability.md`, `docs/design-direction.md`, `docs/asset-pipeline.md`, and the relevant `content/` unit identity.
Writes: concept references in `references/`; editable sources, a filled copy of `asset-manifest.template.json`, and intermediate work in `work/<asset-id>/`; approved or clearly-labelled prototype animated GLB exports in `export/<asset-id>/`; prompts in `prompts/`.
Tests: Blender source opens; exported `.glb` imports into the pinned Godot project; visual review at RTS camera scale. See `docs/asset-pipeline.md`.
Do NOT: overwrite reference images; treat generated geometry as canon; store temporary/cache output as source; change sim state or write Godot-specific code under `sim/`.
Change impact: `docs/asset-pipeline.md`, `game/render/CONTEXT.md` when the presentation contract changes, and `map/effects/CONTEXT.md` only when a listed shared system is affected.
Known limits (2026-09-27): `work/rifleman/` remains a preserved manual primitive blockout. `work/rifleman-v2/` is rejected and must not be used at runtime. `work/rifleman-hunyuan/` contains a 69,819-vertex Hunyuan3D silhouette and separate material/detail studies; these remain visibly below the concept's fidelity and are not approved assets. Hunyuan's texture pass previously hung the R9700 under its separate ROCm 7.13 preview environment. Qwen Image 2.1 now runs on the R9700 through an isolated ComfyUI ROCm 7.2 process; its generated rear view is a modeling reference only. The compatible Qwen3VL 8B encoder is installed at `~/ComfyUI/models/text_encoders/qwen3vl_8b_int8_convrot.safetensors`. Blender 5.0.1 is available. Faction treatment remains provisional.

## Reference catalog

| File | Origin and role | Notes |
|---|---|---|
| `references/elite-infantry-chat-render.jpg` | User's generated unit render from the “Elite Unit Design” chat | Primary modeling target for the first close-match image-to-3D pass. Preserve the dark battle-worn armor, broad shoulder plates, blue visor, and detailed rifle silhouette. |
| `references/armoured-infantry-reference.jpg` | User-provided image attached in the “Elite Unit Design” chat | Blue-visored armored infantry. The supplied file is a photo/screenshot-style reference; it is not a clean model sheet. |
| `references/heavy-tank-reference.jpg` | User-provided image attached in the “Elite Unit Design” chat | Heavy tank with blue lights and multiple weapons. Use as silhouette/detail reference; lower hull is cut by the frame. |
| `references/elite-field-commander-concept.jpg` | Generated concept image from that chat | Larger elite soldier direction. Chat later refined this toward a shorter sculpted helmet and ivory/plum armor; this image predates that final description and should not override it. |

These early concepts are not yet assigned to Coalition, Hegemony, or Ascendant. Resolve that against faction visual identity before production modeling.

## Local image generation available

ComfyUI is available at `http://127.0.0.1:8188` for the existing APU lane; check the active queue and assigned device before use. A temporary R9700 instance was used for the completed rear-view generation and has been stopped. The host's Qwen Image 2.1 stack is:

- `~/ComfyUI/models/diffusion_models/qwen_image_2.1-Q4_K_M.gguf`
- `~/ComfyUI/models/text_encoders/qwen3vl_8b_int8_convrot.safetensors` (compatible Qwen Image 2.1 encoder; `qwen_3_4b.safetensors` is incompatible)
- `~/ComfyUI/models/vae/qwen_image_2.1_vae_bf16.safetensors`

The installed ComfyUI nodes include `UnetLoaderGGUF` and `TextEncodeQwenImage21`. Use these for new raster concept or turnaround images, check the ComfyUI queue before submitting a generation, and preserve outputs with their prompts under `work/<asset-id>/`. This is image generation only; it does not create Blender geometry or rigging.
