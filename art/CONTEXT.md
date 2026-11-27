# art — source concepts and editable game assets

Owns: visual references, provenance, editable Blender sources, generation work, and approved runtime exports. It does not own faction canon or game/simulation behavior.
Reads: `docs/factions/`, `docs/readability.md`, `docs/design-direction.md`, `docs/asset-pipeline.md`, and the relevant `content/` unit identity.
Writes: concept references in `references/`; editable sources, a filled copy of `asset-manifest.template.json`, and intermediate work in `work/<asset-id>/`; approved or clearly-labelled prototype animated GLB exports in `export/<asset-id>/`; prompts in `prompts/`.
Tests: Blender source opens; exported `.glb` imports into the pinned Godot project; visual review at RTS camera scale. See `docs/asset-pipeline.md`.
Do NOT: overwrite reference images; treat generated geometry as canon; store temporary/cache output as source; change sim state or write Godot-specific code under `sim/`.
Change impact: `docs/asset-pipeline.md`, `game/render/CONTEXT.md` when the presentation contract changes, and `map/effects/CONTEXT.md` only when a listed shared system is affected.
Known limits (2026-09-27): `work/rifleman/` contains the first designed, rigged, animated rifleman (built by `build_rifleman.py`); `build_blockout.py` is the preserved manual primitive blockout. `work/rifleman-v2/` is rejected and must not be used at runtime. `work/rifleman-hunyuan/` contains a 69,819-vertex Hunyuan3D silhouette and separate material/detail studies; these remain visibly below the concept's fidelity and are not approved assets. Hunyuan's texture pass previously hung the R9700 under its separate ROCm 7.13 preview environment. Qwen Image 2.1 now runs on the R9700 through an isolated ComfyUI ROCm 7.2 process; its generated rear view is a modeling reference only. The compatible Qwen3VL 8B encoder is installed at `~/ComfyUI/models/text_encoders/qwen3vl_8b_int8_convrot.safetensors`. Blender 5.0.1 is available. Faction treatment remains provisional.

## Reference catalog

| File | Origin and role | Notes |
|---|---|---|
| `references/elite-infantry-chat-render.jpg` | User's generated unit render from the “Elite Unit Design” chat | Primary modeling target for the first close-match image-to-3D pass. Preserve the dark battle-worn armor, broad shoulder plates, blue visor, and detailed rifle silhouette. |
| `references/armoured-infantry-reference.jpg` | User-provided image attached in the “Elite Unit Design” chat | Blue-visored armored infantry. The supplied file is a photo/screenshot-style reference; it is not a clean model sheet. |
| `references/heavy-tank-reference.jpg` | User-provided image attached in the “Elite Unit Design” chat | Heavy tank with blue lights and multiple weapons. Use as silhouette/detail reference; lower hull is cut by the frame. |
| `references/elite-field-commander-concept.jpg` | Generated concept image from that chat | Larger elite soldier direction. Chat later refined this toward a shorter sculpted helmet and ivory/plum armor; this image predates that final description and should not override it. |

## Faction assessment — early concepts

These references remain **undecided for faction assignment**. The faction canon defines gameplay identity (Coalition precision and information, Hegemony mass and attrition, Ascendant exotic technology and mobility), but it does not yet define a visual palette or faction-specific armor language that can reliably identify these images. Do not treat blue lighting, apparent armor weight, or weapon detail alone as faction evidence. Readability requires distinct silhouettes and reserved team-colour use; the current references do not establish those tests at RTS scale. Keep each image as a concept reference only. Do not begin production modeling from an assumed faction identity.

| Reference | Faction status | Rationale |
|---|---|---|
| `references/elite-infantry-chat-render.jpg` | **Undecided** | This is the primary modeling-target image, with dark, detailed armor, broad shoulders, a blue visor, and a rifle. Those cues do not distinguish Coalition precision from Hegemony durability or Ascendant technology. The image remains an identity reference, not faction canon. |
| `references/armoured-infantry-reference.jpg` | **Undecided** | The screenshot-style blue-visored infantry resembles the first reference, but does not show a faction-specific role or visual language. Its presentation also limits reliable silhouette comparison. |
| `references/heavy-tank-reference.jpg` | **Undecided** | The broad, heavily armed tank could suggest Hegemony mass, but its lower hull is cropped and the image provides no faction marker. Coalition and Ascendant also field tanks, with different mechanical profiles. |
| `references/elite-field-commander-concept.jpg` | **Undecided** | The image predates the chat's later ivory/plum armor and shorter sculpted-helmet direction. The later description is not enough to assign a faction because the canon does not map those colors or forms to one faction. Do not let this older image override the refined direction. |

Faction references: [`Coalition`](../docs/factions/coalition.md) emphasizes precision, range, and information; [`Hegemony`](../docs/factions/hegemony.md) emphasizes mass, durability, and attrition; [`Ascendant`](../docs/factions/ascendant.md) emphasizes exotic technology and mobility. [`Design direction`](../docs/design-direction.md) sets readability and faction identity ahead of spectacle. [`Readability`](../docs/readability.md) requires silhouette, luminance, and team-colour checks that these source images have not passed. Revisit these assignments after the project defines faction-specific visual languages and the reference set can be evaluated against them.

## Local image generation available

ComfyUI is available at `http://127.0.0.1:8188` for the existing APU lane; check the active queue and assigned device before use. A temporary R9700 instance was used for the completed rear-view generation and has been stopped. The host's Qwen Image 2.1 stack is:

- `~/ComfyUI/models/diffusion_models/qwen_image_2.1-Q4_K_M.gguf`
- `~/ComfyUI/models/text_encoders/qwen3vl_8b_int8_convrot.safetensors` (compatible Qwen Image 2.1 encoder; `qwen_3_4b.safetensors` is incompatible)
- `~/ComfyUI/models/vae/qwen_image_2.1_vae_bf16.safetensors`

The installed ComfyUI nodes include `UnetLoaderGGUF` and `TextEncodeQwenImage21`. Use these for new raster concept or turnaround images, check the ComfyUI queue before submitting a generation, and preserve outputs with their prompts under `work/<asset-id>/`. This is image generation only; it does not create Blender geometry or rigging.
