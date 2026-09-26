#!/usr/bin/env python3
"""Generate an unreviewed PBR GLB candidate from the approved infantry render.

This runner intentionally avoids TRELLIS.2's example renderer imports and its
unsafe expandable_segments allocator setting. It writes only beneath this WIP
directory and records the exact runtime/model parameters beside the GLB.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import sys
import time
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[3]
DEFAULT_MODEL_ROOT = Path.home() / ".cache/huggingface/hub/models--microsoft--TRELLIS.2-4B/snapshots/af44b45f2e35a493886929c6d786e563ec68364d"
DEFAULT_INPUT = REPO_ROOT / "art/references/elite-infantry-chat-render.jpg"
OUTPUT_ROOT = Path(__file__).resolve().parent


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, default=DEFAULT_INPUT)
    parser.add_argument("--model-root", type=Path, default=DEFAULT_MODEL_ROOT)
    parser.add_argument("--output", type=Path, default=OUTPUT_ROOT / "rifleman-trellis2-candidate.glb")
    parser.add_argument("--pipeline-type", choices=("512", "1024", "1024_cascade", "1536_cascade"), default="1024_cascade")
    parser.add_argument("--seed", type=int, default=42)
    parser.add_argument("--decimation-target", type=int, default=1_000_000)
    parser.add_argument("--texture-size", type=int, choices=(1024, 2048, 4096), default=4096)
    return parser.parse_args()


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def prepare_model_overlay(model_root: Path) -> Path:
    """Use local TRELLIS weights with public replacement DINOv3/RMBG models."""
    overlay = OUTPUT_ROOT / "model-config"
    overlay.mkdir(parents=True, exist_ok=True)
    ckpts_link = overlay / "ckpts"
    if not ckpts_link.exists():
        ckpts_link.symlink_to(model_root / "ckpts", target_is_directory=True)

    config = json.loads((model_root / "pipeline.json").read_text())
    args = config["args"]
    args["image_cond_model"]["args"]["model_name"] = "PIA-SPACE-LAB/dinov3-vitl-pretrain-lvd1689m"
    args["rembg_model"]["args"]["model_name"] = "ZhengPeng7/BiRefNet"
    (overlay / "pipeline.json").write_text(json.dumps(config, indent=2) + "\n")
    return overlay


def main() -> int:
    args = parse_args()
    input_path = args.input.resolve()
    model_root = args.model_root.resolve()
    output_path = args.output.resolve()
    report_path = output_path.with_suffix(".run.json")

    if not input_path.is_file():
        raise SystemExit(f"Input reference is missing: {input_path}")
    if not (model_root / "pipeline.json").is_file() or not (model_root / "ckpts").is_dir():
        raise SystemExit(f"TRELLIS.2 model snapshot is incomplete: {model_root}")
    if args.decimation_target < 100_000:
        raise SystemExit("Refusing a decimation target below 100,000 faces for this fidelity pass.")

    # gfx1201 stack and safe attention/allocator configuration, before importing torch.
    os.environ["HIP_VISIBLE_DEVICES"] = "0"
    os.environ["ATTN_BACKEND"] = "sdpa"
    os.environ["SPARSE_ATTN_BACKEND"] = "sdpa"
    os.environ["SPARSE_CONV_BACKEND"] = "flex_gemm"
    os.environ["TORCH_BLAS_PREFER_HIPBLASLT"] = "0"
    os.environ.pop("PYTORCH_CUDA_ALLOC_CONF", None)
    os.environ.pop("PYTORCH_ALLOC_CONF", None)
    os.environ["TRELLIS2_ROWCHUNK"] = "262144"

    overlay = prepare_model_overlay(model_root)
    sys.path.insert(0, str(Path.home() / ".local/opt/TRELLIS.2-gfx1201"))

    import torch
    from PIL import Image
    import o_voxel
    from trellis2.pipelines import Trellis2ImageTo3DPipeline

    if not torch.cuda.is_available():
        raise SystemExit("PyTorch cannot see a ROCm device; refusing to fall back to CPU.")
    if torch.version.hip is None:
        raise SystemExit(f"Expected ROCm PyTorch, found torch {torch.__version__} without HIP.")
    device_name = torch.cuda.get_device_name(0)
    if "9700" not in device_name.lower():
        raise SystemExit(f"Visible device is not the expected R9700: {device_name}")

    output_path.parent.mkdir(parents=True, exist_ok=True)
    start = time.monotonic()
    print(f"Loading TRELLIS.2 from {model_root}", flush=True)
    pipeline = Trellis2ImageTo3DPipeline.from_pretrained(str(overlay))
    pipeline.cuda()
    print(f"Running {args.pipeline_type} on {torch.cuda.get_device_name(0)}", flush=True)
    with Image.open(input_path) as source:
        image = source.convert("RGBA")
    meshes = pipeline.run(image, seed=args.seed, pipeline_type=args.pipeline_type)
    if not meshes:
        raise RuntimeError("TRELLIS.2 returned no mesh candidates.")
    mesh = meshes[0]
    torch.cuda.synchronize()
    generation_seconds = time.monotonic() - start

    vertices = mesh.vertices
    faces = mesh.faces
    vertex_count = int(vertices.shape[0])
    face_count = int(faces.shape[0])
    if vertex_count < 10_000 or face_count < 10_000:
        raise RuntimeError(f"Candidate geometry is unexpectedly small: {vertex_count} vertices, {face_count} faces")

    print(f"Exporting PBR GLB ({vertex_count:,} vertices / {face_count:,} faces)", flush=True)
    glb = o_voxel.postprocess.to_glb(
        vertices=mesh.vertices,
        faces=mesh.faces,
        attr_volume=mesh.attrs,
        coords=mesh.coords,
        attr_layout=mesh.layout,
        voxel_size=mesh.voxel_size,
        aabb=[[-0.5, -0.5, -0.5], [0.5, 0.5, 0.5]],
        decimation_target=args.decimation_target,
        texture_size=args.texture_size,
        remesh=True,
        remesh_band=1,
        remesh_project=0,
        verbose=True,
    )
    glb.export(str(output_path), extension_webp=True)
    total_seconds = time.monotonic() - start
    report = {
        "status": "generated_unreviewed_candidate",
        "input": str(input_path),
        "input_sha256": sha256(input_path),
        "output": str(output_path),
        "output_sha256": sha256(output_path),
        "output_bytes": output_path.stat().st_size,
        "model": "microsoft/TRELLIS.2-4B",
        "model_snapshot": str(model_root),
        "model_config_overlay": str(overlay / "pipeline.json"),
        "pipeline_type": args.pipeline_type,
        "seed": args.seed,
        "raw_vertices": vertex_count,
        "raw_faces": face_count,
        "decimation_target": args.decimation_target,
        "texture_size": args.texture_size,
        "torch": torch.__version__,
        "rocm": torch.version.hip,
        "device": device_name,
        "generation_seconds": round(generation_seconds, 2),
        "total_seconds": round(total_seconds, 2),
        "notes": [
            "Single-view image-to-3D output; hidden surfaces are inferred.",
            "Not rigged or animated; inspect and correct in Blender before promotion.",
            "Faction remains unassigned; reference render is the visual source of truth.",
        ],
    }
    report_path.write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report, indent=2), flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
