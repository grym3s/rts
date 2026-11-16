#!/usr/bin/env bash
# Build and optionally install a game asset.
#
# Usage:
#   art/build-asset.sh <asset-id>            # export a validated animated GLB to art/export/<asset-id>/
#   art/build-asset.sh <asset-id> --install  # also copy to game/assets/<asset-id>/ and run Godot import
#
# Environment:
#   BL_BIN        optional explicit Blender binary path
#   GODOT_BIN     optional explicit Godot binary path
#   SPRITE_RENDER optional: set to 1 to run sprite rendering (opt-in, non-fatal)
#
# The pipeline is GLB-first: it builds a self-contained glTF 2.0 .glb (materials,
# armature, named clips), validates it, and installs the GLB. Sprite rendering
# is optional and not a required stage. The build script for a unit is looked up
# by convention (build_<asset_id>.py or build_blockout.py).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"

build_asset() {
  local asset_id="${1}"
  local mode="${2:-build}"
  local work_dir="$ROOT/art/work/$asset_id"
  local export_dir="$ROOT/art/export/$asset_id"
  [[ -d "$work_dir" ]] || { echo "work folder missing: $work_dir"; return 1; }
  mkdir -p "$export_dir"

  # Locate the per-asset build script (GLB authoring).
  local build_script=""
  if [[ -f "$work_dir/build_${asset_id}.py" ]]; then
    build_script="$work_dir/build_${asset_id}.py"
  elif [[ -f "$work_dir/build_blockout.py" ]]; then
    build_script="$work_dir/build_blockout.py"
  else
    echo "no build script found in $work_dir (expected build_${asset_id}.py)"
    return 1
  fi

  # Locate the Blender binary (authoring tool).
  local blender_bin=""
  if [[ -n "${BL_BIN:-}" ]]; then
    blender_bin="$BL_BIN"
  elif command -v blender >/dev/null 2>&1; then
    blender_bin="$(command -v blender)"
  fi

  # Run the build script via Blender (if available) to produce the GLB.
  if [[ -n "$blender_bin" ]]; then
    "$blender_bin" --background --factory-startup --python-exit-code 1 --python "$build_script"
  else
    echo "SKIP (no blender): using any existing GLB export"
  fi

  # Locate the produced GLB (work dir first, then export dir).
  local glb_src=""
  if [[ -s "$work_dir/$asset_id.glb" ]]; then
    glb_src="$work_dir/$asset_id.glb"
  elif [[ -s "$export_dir/$asset_id.glb" ]]; then
    glb_src="$export_dir/$asset_id.glb"
  else
    echo "missing GLB export: $asset_id.glb (build with Blender to produce it)"
    return 1
  fi

  # Validate the GLB (self-contained glTF 2.0, materials, armature, named clips).
  if ! python3 "$ROOT/art/validate_glb.py" "$glb_src"; then
    echo "GLB validation failed: $glb_src"
    return 1
  fi

  # Copy the GLB to the canonical export location.
  if [[ "$glb_src" != "$export_dir/$asset_id.glb" ]]; then
    cp "$glb_src" "$export_dir/$asset_id.glb"
  fi
  echo "exported: $export_dir/$asset_id.glb"

  # Optional sprite rendering: opt-in via SPRITE_RENDER=1, and non-fatal — a
  # sprite failure never breaks the GLB path.
  if [[ "${SPRITE_RENDER:-0}" == "1" && -f "$work_dir/render_sprites.py" && -n "$blender_bin" ]]; then
    if "$blender_bin" --background --python-exit-code 1 --python "$work_dir/render_sprites.py"; then
      if [[ -s "$work_dir/${asset_id}_sheet.png" ]]; then
        cp "$work_dir/${asset_id}_sheet.png" "$export_dir/"
        echo "sprite sheet (optional): $export_dir/${asset_id}_sheet.png"
      fi
    else
      echo "sprite render failed (non-fatal): continuing GLB path"
    fi
  fi

  if [[ "$mode" == "install" ]]; then
    local install_dir="$ROOT/game/assets/$asset_id"
    mkdir -p "$install_dir"
    cp "$export_dir/$asset_id.glb" "$install_dir/"
    if [[ -s "$export_dir/${asset_id}_sheet.png" ]]; then
      cp "$export_dir/${asset_id}_sheet.png" "$install_dir/"
    fi
    echo "installed: $install_dir/$asset_id.glb"

    # Godot headless import of the runtime asset.
    local godot_bin=""
    if [[ -n "${GODOT_BIN:-}" ]]; then
      godot_bin="$GODOT_BIN"
    elif command -v godot >/dev/null 2>&1; then
      godot_bin="$(command -v godot)"
    fi
    if [[ -n "$godot_bin" && -f "$ROOT/game/project.godot" ]]; then
      (cd "$ROOT/game" && "$godot_bin" --headless --import >/dev/null 2>&1)
      echo "godot import: ok"
    fi
  fi
  return 0
}

main() {
  [[ $# -ge 1 ]] || { echo "usage: $0 <asset-id> [--install]"; exit 2; }
  local asset_id="${1}"
  local mode="build"
  [[ "${2:-}" == "--install" ]] && mode="install"
  build_asset "$asset_id" "$mode"
}

main "$@"
