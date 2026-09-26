#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "Usage: art/build-asset.sh <asset-id> [--install]" >&2
  echo "Build runs the asset's Blender blockout and sprite scripts. --install copies the" >&2
  echo "result into game/assets/<asset-id> and runs Godot's headless import." >&2
}

if [[ $# -lt 1 || $# -gt 2 ]]; then usage; exit 2; fi
asset_id="$1"
install_runtime=false
if [[ $# -eq 2 ]]; then
  [[ "$2" == "--install" ]] || { usage; exit 2; }
  install_runtime=true
fi
[[ "$asset_id" =~ ^[a-z0-9]+(-[a-z0-9]+)*$ ]] || { echo "Invalid asset id: $asset_id" >&2; exit 2; }

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
work_dir="$repo_root/art/work/$asset_id"
export_dir="$repo_root/art/export/$asset_id"
blender_bin="$(command -v blender || true)"
if [[ -z "$blender_bin" && -x "$HOME/.local/bin/blender" ]]; then blender_bin="$HOME/.local/bin/blender"; fi
[[ -n "$blender_bin" ]] || { echo "Blender executable not found in PATH or ~/.local/bin" >&2; exit 1; }
[[ -f "$work_dir/build_blockout.py" && -f "$work_dir/render_sprites.py" ]] || {
  echo "Expected build_blockout.py and render_sprites.py in $work_dir" >&2; exit 1;
}

export RTS_ASSET_ID="$asset_id"
mkdir -p "$export_dir"
echo "Building $asset_id with $blender_bin"
"$blender_bin" --background --factory-startup --python-exit-code 1 --python "$work_dir/build_blockout.py"
"$blender_bin" --background --python-exit-code 1 --python "$work_dir/render_sprites.py"

for file in "$asset_id.glb" "${asset_id}_sheet.png" sprite-manifest.json; do
  [[ -s "$export_dir/$file" ]] || { echo "Missing or empty export: $export_dir/$file" >&2; exit 1; }
done
echo "Exports ready: $export_dir"

if [[ "$install_runtime" == true ]]; then
  runtime_dir="$repo_root/game/assets/$asset_id"
  mkdir -p "$runtime_dir"
  cp "$export_dir/$asset_id.glb" "$export_dir/${asset_id}_sheet.png" "$export_dir/sprite-manifest.json" "$runtime_dir/"
  godot_bin="$(command -v godot || command -v godot4 || true)"
  [[ -n "$godot_bin" ]] || { echo "Exports copied, but Godot was not found for import." >&2; exit 1; }
  if [[ -x "$HOME/.dotnet/dotnet" ]]; then
    export DOTNET_ROOT="$HOME/.dotnet"
    export PATH="$HOME/.dotnet:$PATH"
  fi
  "$godot_bin" --path "$repo_root/game" --headless --import --quit
  echo "Runtime copy imported: $runtime_dir"
fi
