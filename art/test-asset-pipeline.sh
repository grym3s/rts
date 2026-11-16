#!/usr/bin/env bash
# Regression tests for the asset build pipeline (validate_glb.py + build-asset.sh).
#
# Usage:
#   art/test-asset-pipeline.sh            # run all cases
#   art/test-asset-pipeline.sh <case>     # run one case:
#     clips | valid | self-contained | binary | skins |
#     sprites-default | sprites-optin
#
# Each validator case generates a minimal GLB fixture in a temp dir, runs
# validate_glb.py, and asserts the expected exit code. Each sprite case runs
# build-asset.sh with a failing renderer and asserts the GLB path outcome.
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VALID="$ROOT/art/work/rifleman/rifleman.glb"
FAIL=0

# Python helper: build a minimal GLB from a JSON dict and optional BIN size.
make_glb() {
  python3 - "$1" "$2" <<'PY'
import sys, json, struct
js_path, bin_size = sys.argv[1], int(sys.argv[2])
with open(js_path, "rb") as f:
    js = json.load(f)
json_bytes = json.dumps(js).encode("utf-8")
pad = (4 - len(json_bytes) % 4) % 4
json_bytes += b" " * pad
json_chunk = struct.pack("<I", len(json_bytes)) + b"JSON" + json_bytes
bin_chunk = b""
if bin_size > 0:
    bin_chunk = struct.pack("<I", bin_size) + b"BIN\x00" + b"\x00" * bin_size
body = json_chunk + bin_chunk
total = 12 + len(body)
sys.stdout.buffer.write(b"glTF" + struct.pack("<I", 2) + struct.pack("<I", total) + body)
PY
}

# write_json: write a JSON dict (as a Python literal string) to a temp file.
write_json() {
  python3 - "$1" <<'PY'
import sys, json
with open(sys.argv[1], "w") as f:
    f.write(sys.argv[2])
PY
}

# run_validator: run validate_glb.py on a path; expect_exit is the expected code.
run_validator() {
  local path="$1" expect_exit="$2" label="$3"
  local rc=0
  python3 "$ROOT/art/validate_glb.py" "$path" >/dev/null || rc=$?
  if [[ "$rc" -eq "$expect_exit" ]]; then
    echo "  ok: $label (exit $rc == expected $expect_exit)"
  else
    echo "  FAIL: $label (exit $rc != expected $expect_exit)"
    FAIL=1
  fi
}

# fixture: create a GLB fixture from a JSON python-literal and bin_size.
fixture() {
  local json="$1" bin_size="$2" out="$3"
  local js_tmp
  js_tmp="$(mktemp)"
  printf '%s' "$json" > "$js_tmp"
  python3 - "$js_tmp" "$bin_size" "$out" <<'PY'
import sys, json, struct
js_path, bin_size, out = sys.argv[1], int(sys.argv[2]), sys.argv[3]
with open(js_path, "rb") as f:
    js = json.load(f)
json_bytes = json.dumps(js).encode("utf-8")
pad = (4 - len(json_bytes) % 4) % 4
json_bytes += b" " * pad
json_chunk = struct.pack("<I", len(json_bytes)) + b"JSON" + json_bytes
bin_chunk = b""
if bin_size > 0:
    bin_chunk = struct.pack("<I", bin_size) + b"BIN\x00" + b"\x00" * bin_size
body = json_chunk + bin_chunk
total = 12 + len(body)
with open(out, "wb") as f:
    f.write(b"glTF" + struct.pack("<I", 2) + struct.pack("<I", total) + body)
PY
  rm -f "$js_tmp"
}

case_clips() {
  local tmp; tmp="$(mktemp -d)"
  # valid GLB with all three clips
  fixture '{"asset":{"version":"2.0"},"materials":[{"name":"m"}],"buffers":[{"byteLength":16}],"bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":16}],"skins":[{"joints":[0],"inverseBindMatrices":0}],"animations":[{"name":"idle"},{"name":"move"},{"name":"fire"}]}' 16 "$tmp/valid.glb"
  # idle-only GLB (missing move, fire)
  fixture '{"asset":{"version":"2.0"},"materials":[{"name":"m"}],"buffers":[{"byteLength":16}],"bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":16}],"skins":[{"joints":[0],"inverseBindMatrices":0}],"animations":[{"name":"idle"}]}' 16 "$tmp/idle_only.glb"
  run_validator "$tmp/idle_only.glb" 1 "idle-only GLB rejected (missing move/fire)"
  run_validator "$tmp/valid.glb" 0 "valid GLB accepted (all clips)"
  rm -rf "$tmp"
}

case_valid() {
  run_validator "$VALID" 0 "real animated rifleman GLB accepted"
  # also reject the old blockout (no armature, no clips)
  local rc=0
  python3 "$ROOT/art/validate_glb.py" "$ROOT/art/work/rifleman/attempt-1/game-assets/rifleman.glb" >/dev/null || rc=$?
  if [[ "$rc" -ne 0 ]]; then
    echo "  ok: old blockout GLB rejected (exit $rc)"
  else
    echo "  FAIL: old blockout GLB not rejected (exit $rc)"
    FAIL=1
  fi
}

case_self_contained() {
  local tmp; tmp="$(mktemp -d)"
  # external buffer uri
  fixture '{"asset":{"version":"2.0"},"materials":[{"name":"m"}],"buffers":[{"byteLength":16,"uri":"data.bin"}],"bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":16}],"skins":[{"joints":[0],"inverseBindMatrices":0}],"animations":[{"name":"idle"},{"name":"move"},{"name":"fire"}]}' 16 "$tmp/ext_uri.glb"
  run_validator "$tmp/ext_uri.glb" 1 "external buffer uri rejected (not self-contained)"
  rm -rf "$tmp"
}

case_binary() {
  local tmp; tmp="$(mktemp -d)"
  # buffer byteLength > 0 but no BIN chunk
  fixture '{"asset":{"version":"2.0"},"materials":[{"name":"m"}],"buffers":[{"byteLength":16}],"bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":16}],"skins":[{"joints":[0],"inverseBindMatrices":0}],"animations":[{"name":"idle"},{"name":"move"},{"name":"fire"}]}' 0 "$tmp/no_bin.glb"
  run_validator "$tmp/no_bin.glb" 1 "missing BIN data rejected (buffer byteLength > 0, no BIN)"
  # bufferView out of range
  fixture '{"asset":{"version":"2.0"},"materials":[{"name":"m"}],"buffers":[{"byteLength":16}],"bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":64}],"skins":[{"joints":[0],"inverseBindMatrices":0}],"animations":[{"name":"idle"},{"name":"move"},{"name":"fire"}]}' 16 "$tmp/oor_view.glb"
  run_validator "$tmp/oor_view.glb" 1 "bufferView out of range rejected"
  rm -rf "$tmp"
}

case_skins() {
  local tmp; tmp="$(mktemp -d)"
  # empty joints
  fixture '{"asset":{"version":"2.0"},"materials":[{"name":"m"}],"buffers":[{"byteLength":16}],"bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":16}],"skins":[{"joints":[]}],"animations":[{"name":"idle"},{"name":"move"},{"name":"fire"}]}' 16 "$tmp/empty_joints.glb"
  run_validator "$tmp/empty_joints.glb" 1 "empty skin joints rejected"
  # unusable inverseBindMatrices (index out of range)
  fixture '{"asset":{"version":"2.0"},"materials":[{"name":"m"}],"buffers":[{"byteLength":16}],"bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":16}],"skins":[{"joints":[0],"inverseBindMatrices":99}],"animations":[{"name":"idle"},{"name":"move"},{"name":"fire"}]}' 16 "$tmp/bad_ibm.glb"
  run_validator "$tmp/bad_ibm.glb" 1 "unusable inverseBindMatrices rejected"
  rm -rf "$tmp"
}

case_sprites() {
  local mode="$1"
  local asset_id="testsprite"
  local work_dir="$ROOT/art/work/$asset_id"
  mkdir -p "$work_dir"
  echo '# build_blockout.py (no-op)' > "$work_dir/build_blockout.py"
  cp "$VALID" "$work_dir/$asset_id.glb"
  echo 'import sys; sys.exit(1)' > "$work_dir/render_sprites.py"
  local fake_blender="$ROOT/art/.test-fake-blender.sh"
  cat > "$fake_blender" <<'EOF'
#!/bin/bash
python_script=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --python) python_script="$2"; shift 2;;
    *) shift;;
  esac
done
if [[ -n "$python_script" ]]; then
  exec python3 "$python_script"
fi
exit 0
EOF
  chmod +x "$fake_blender"
  local rc=0
  if [[ "$mode" == "optin" ]]; then
    SPRITE_RENDER=1 BL_BIN="$fake_blender" "$ROOT/art/build-asset.sh" "$asset_id" >/dev/null || rc=$?
  else
    BL_BIN="$fake_blender" "$ROOT/art/build-asset.sh" "$asset_id" >/dev/null || rc=$?
  fi
  # cleanup
  rm -rf "$work_dir"
  rm -rf "$ROOT/art/export/$asset_id"
  rm -f "$fake_blender"
  if [[ "$rc" -eq 0 ]]; then
    echo "  ok: $mode (exit 0)"
  else
    echo "  FAIL: $mode (exit $rc)"
    FAIL=1
  fi
}

case "${1:-all}" in
  clips) case_clips ;;
  valid) case_valid ;;
  self-contained) case_self_contained ;;
  binary) case_binary ;;
  skins) case_skins ;;
  sprites-default) case_sprites default ;;
  sprites-optin) case_sprites optin ;;
  all)
    case_clips
    case_valid
    case_self_contained
    case_binary
    case_skins
    case_sprites default
    case_sprites optin
    ;;
  *) echo "unknown case: $1"; exit 2 ;;
esac

if [[ "$FAIL" -eq 0 ]]; then
  echo "PASS"
  exit 0
else
  echo "FAIL"
  exit 1
fi
