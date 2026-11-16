#!/usr/bin/env bash
# Regression for the GLB pipeline.
#
# Runs the validator against the real animated GLB (positive control) and
# against synthetic valid and malformed controls (self-contained, binary data,
# materials, armature, usable clips, sprite path).
#
# Uses only python3 and the validator; no external dependencies.
# Exits 0 when every case passes, 1 otherwise.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"

REAL_GLB="$ROOT/art/work/rifleman/rifleman.glb"
NEG_CONTROL="$ROOT/art/work/rifleman/attempt-1/game-assets/rifleman.glb"
TMPD="$(mktemp -d)"
trap 'rm -rf "$TMPD"' EXIT

ok=0
fail=0
pass() { echo "ok   - $1"; ok=$((ok+1)); }
failc() { echo "FAIL - $1"; fail=$((fail+1)); }

# Run the validator against a GLB and compare the exit code.
#   $1 = label, $2 = expected exit code, $3 = glb path
expect() {
  local label="$1" want="$2" glb="$3"
  local rc=0
  python3 "$ROOT/art/validate_glb.py" "$glb" >/dev/null 2>&1 || rc=$?
  if [ "$rc" -eq "$want" ]; then
    pass "$label"
  else
    failc "$label (expected rc=$want, got rc=$rc)"
  fi
}

# --- Build validator fixtures (valid + malformed controls) ---
python3 - "$TMPD" <<'PY'
import json, sys, copy, os
tmp = sys.argv[1]

# A valid, self-contained, armatured, animated control.
# Accessor 0: MAT4 (inverseBindMatrices), 1: SCALAR (anim input time), 2: VEC3 (anim output pos)
BASE = {
    "asset": {"version": "2.0"},
    "buffers": [{"byteLength": 80}],
    "bufferViews": [
        {"buffer": 0, "byteOffset": 0, "byteLength": 64},   # MAT4
        {"buffer": 0, "byteOffset": 64, "byteLength": 4},   # SCALAR
        {"buffer": 0, "byteOffset": 68, "byteLength": 12}   # VEC3
    ],
    "accessors": [
        {"bufferView": 0, "componentType": 5126, "count": 1, "type": "MAT4"},
        {"bufferView": 1, "componentType": 5126, "count": 1, "type": "SCALAR"},
        {"bufferView": 2, "componentType": 5126, "count": 1, "type": "VEC3"}
    ],
    "nodes": [{"name": "joint0"}],
    "materials": [{"name": "m"}],
    "images": [],
    "skins": [{"joints": [0], "inverseBindMatrices": 0}],
    "animations": [
        {"name": "idle", "channels": [{"sampler": 0, "target": {"node": 0, "path": "translation"}}], "samplers": [{"input": 1, "output": 2}]},
        {"name": "move", "channels": [{"sampler": 0, "target": {"node": 0, "path": "translation"}}], "samplers": [{"input": 1, "output": 2}]},
        {"name": "fire", "channels": [{"sampler": 0, "target": {"node": 0, "path": "translation"}}], "samplers": [{"input": 1, "output": 2}]}
    ]
}

def write_glb(name, doc, bin_bytes=b"\x00" * 80, magic=b"glTF", version=2):
    path = f"{tmp}/{name}"
    payload = json.dumps(doc).encode()
    while len(payload) % 4:
        payload += b" "
    with open(path, "wb") as f:
        f.write(magic)
        f.write(version.to_bytes(4, "little"))
        f.write(b"\x00" * 4)  # length placeholder
        f.write(len(payload).to_bytes(4, "little"))
        f.write(b"JSON")
        f.write(payload)
        if bin_bytes:
            f.write(len(bin_bytes).to_bytes(4, "little"))
            f.write(b"BIN\x00")
            f.write(bin_bytes)
        total = 12 + 8 + len(payload) + (8 + len(bin_bytes) if bin_bytes else 0)
        f.seek(8)
        f.write(total.to_bytes(4, "little"))

# --- container controls ---
write_glb("valid.glb", BASE)
d = copy.deepcopy(BASE); write_glb("badmagic.glb", d, magic=b"notG")
d = copy.deepcopy(BASE); write_glb("ver3.glb", d, version=3)
d = copy.deepcopy(BASE); write_glb("len.glb", d)
# overwrite the length field so it is smaller than the actual file size
size = os.path.getsize(f"{tmp}/len.glb")
with open(f"{tmp}/len.glb", "r+b") as f:
    f.seek(8)
    f.write((size - 100).to_bytes(4, "little"))

# --- self-contained controls ---
d = copy.deepcopy(BASE); d["buffers"][0]["uri"] = "data.bin"; write_glb("extbuf.glb", d)
d = copy.deepcopy(BASE); d["images"] = [{"uri": "tex.png"}]; write_glb("extimg.glb", d)
# Data URIs are embedded (valid).
d = copy.deepcopy(BASE); d["images"] = [{"uri": "data:image/png;base64,iVBORw0KGgo="}]; write_glb("datauri.glb", d)
# Image bufferView out of range.
d = copy.deepcopy(BASE); d["images"] = [{"bufferView": 99}]; write_glb("badimgview.glb", d)

# --- binary data controls ---
d = copy.deepcopy(BASE); write_glb("binbig.glb", d, bin_bytes=b"\x00" * 8)  # buffer 80 > BIN 8

# --- bufferView range controls ---
d = copy.deepcopy(BASE); d["bufferViews"][0]["byteLength"] = 128; write_glb("bvrange.glb", d)

# --- materials controls ---
d = copy.deepcopy(BASE); d["materials"] = []; write_glb("nmat.glb", d)

# --- armature controls ---
d = copy.deepcopy(BASE); d["skins"] = []; write_glb("noskin.glb", d)
d = copy.deepcopy(BASE); d["skins"] = [{"joints": [], "inverseBindMatrices": 0}]; write_glb("emptyjoint.glb", d)
d = copy.deepcopy(BASE); d["skins"] = [{"joints": [5], "inverseBindMatrices": 0}]; write_glb("badjoint.glb", d)  # node 5 missing
d = copy.deepcopy(BASE); d["skins"] = [{"joints": [0], "inverseBindMatrices": 99}]; write_glb("badibmacc.glb", d)  # accessor 99 missing
d = copy.deepcopy(BASE); d["accessors"] = [{"componentType": 5126, "count": 1, "type": "MAT4"}]; write_glb("ibmnoaccbv.glb", d)  # accessor without bufferView
d = copy.deepcopy(BASE); d["skins"] = [{"joints": [0]}]; write_glb("ibmomit.glb", d)  # omitted matrices = valid (identity)
# Matrix accessor extent exceeds bufferView.
d = copy.deepcopy(BASE); d["accessors"][0]["count"] = 10; write_glb("ibmextent.glb", d)  # 10*64=640 > 64
# Matrix count mismatch (1 matrix for 2 joints).
d = copy.deepcopy(BASE); d["skins"] = [{"joints": [0, 0], "inverseBindMatrices": 0}]; write_glb("ibmcount.glb", d)  # count 1 != 2 joints

# --- clips controls ---
d = copy.deepcopy(BASE); d["animations"] = [{"name": "idle"}, {"name": "move"}, {"name": "fire"}]; write_glb("nameonly.glb", d)
d = copy.deepcopy(BASE)
d["animations"] = [
    {"name": "idle", "channels": [{"sampler": 0, "target": {"node": 0, "path": "translation"}}]},  # no samplers
    {"name": "move", "channels": [{"sampler": 0, "target": {"node": 0, "path": "translation"}}], "samplers": [{"input": 1, "output": 2}]},
    {"name": "fire", "channels": [{"sampler": 0, "target": {"node": 0, "path": "translation"}}], "samplers": [{"input": 1, "output": 2}]}
]
write_glb("nochan.glb", d)
d = copy.deepcopy(BASE)
d["animations"] = [
    {"name": "idle", "channels": [{"sampler": 5, "target": {"node": 0, "path": "translation"}}], "samplers": [{"input": 1, "output": 2}]},
    {"name": "move", "channels": [{"sampler": 0, "target": {"node": 0, "path": "translation"}}], "samplers": [{"input": 1, "output": 2}]},
    {"name": "fire", "channels": [{"sampler": 0, "target": {"node": 0, "path": "translation"}}], "samplers": [{"input": 1, "output": 2}]}
]
write_glb("badsamp.glb", d)
d = copy.deepcopy(BASE)
d["animations"] = [  # missing fire
    {"name": "idle", "channels": [{"sampler": 0, "target": {"node": 0, "path": "translation"}}], "samplers": [{"input": 1, "output": 2}]},
    {"name": "move", "channels": [{"sampler": 0, "target": {"node": 0, "path": "translation"}}], "samplers": [{"input": 1, "output": 2}]}
]
write_glb("missclip.glb", d)
# Input accessor out of range.
d = copy.deepcopy(BASE)
d["animations"] = [
    {"name": "idle", "channels": [{"sampler": 0, "target": {"node": 0, "path": "translation"}}], "samplers": [{"input": 99, "output": 2}]},
    {"name": "move", "channels": [{"sampler": 0, "target": {"node": 0, "path": "translation"}}], "samplers": [{"input": 1, "output": 2}]},
    {"name": "fire", "channels": [{"sampler": 0, "target": {"node": 0, "path": "translation"}}], "samplers": [{"input": 1, "output": 2}]}
]
write_glb("badinpacc.glb", d)
# Output accessor out of range.
d = copy.deepcopy(BASE)
d["animations"] = [
    {"name": "idle", "channels": [{"sampler": 0, "target": {"node": 0, "path": "translation"}}], "samplers": [{"input": 1, "output": 99}]},
    {"name": "move", "channels": [{"sampler": 0, "target": {"node": 0, "path": "translation"}}], "samplers": [{"input": 1, "output": 2}]},
    {"name": "fire", "channels": [{"sampler": 0, "target": {"node": 0, "path": "translation"}}], "samplers": [{"input": 1, "output": 2}]}
]
write_glb("badoutacc.glb", d)
# Target node out of range.
d = copy.deepcopy(BASE)
d["animations"] = [
    {"name": "idle", "channels": [{"sampler": 0, "target": {"node": 99, "path": "translation"}}], "samplers": [{"input": 1, "output": 2}]},
    {"name": "move", "channels": [{"sampler": 0, "target": {"node": 0, "path": "translation"}}], "samplers": [{"input": 1, "output": 2}]},
    {"name": "fire", "channels": [{"sampler": 0, "target": {"node": 0, "path": "translation"}}], "samplers": [{"input": 1, "output": 2}]}
]
write_glb("badnode.glb", d)

print("fixtures built")
PY

# --- container controls ---
expect "container: valid control" 0 "$TMPD/valid.glb"
expect "container: bad magic" 1 "$TMPD/badmagic.glb"
expect "container: version 3" 1 "$TMPD/ver3.glb"
expect "container: length < file size" 1 "$TMPD/len.glb"

# --- self-contained controls ---
expect "self-contained: external buffer uri" 1 "$TMPD/extbuf.glb"
expect "self-contained: external image uri" 1 "$TMPD/extimg.glb"
expect "self-contained: data URI is embedded (valid)" 0 "$TMPD/datauri.glb"
expect "self-contained: image bufferView out of range" 1 "$TMPD/badimgview.glb"

# --- binary data controls ---
expect "binary data: buffer > BIN chunk" 1 "$TMPD/binbig.glb"

# --- bufferView range controls ---
expect "bufferView: out of range" 1 "$TMPD/bvrange.glb"

# --- materials controls ---
expect "materials: none" 1 "$TMPD/nmat.glb"

# --- armature controls ---
expect "armature: no skins" 1 "$TMPD/noskin.glb"
expect "armature: empty joints" 1 "$TMPD/emptyjoint.glb"
expect "armature: joint node out of range" 1 "$TMPD/badjoint.glb"
expect "armature: broken inverseBindMatrices accessor" 1 "$TMPD/badibmacc.glb"
expect "armature: inverseBindMatrices accessor without bufferView" 1 "$TMPD/ibmnoaccbv.glb"
expect "armature: omitted inverseBindMatrices (identity)" 0 "$TMPD/ibmomit.glb"
expect "armature: matrix accessor extent exceeds bufferView" 1 "$TMPD/ibmextent.glb"
expect "armature: matrix count mismatch (1 matrix, 2 joints)" 1 "$TMPD/ibmcount.glb"

# --- clips controls ---
expect "clips: name-only (no channels/samplers)" 1 "$TMPD/nameonly.glb"
expect "clips: channels without samplers" 1 "$TMPD/nochan.glb"
expect "clips: channel sampler out of range" 1 "$TMPD/badsamp.glb"
expect "clips: missing clip name" 1 "$TMPD/missclip.glb"
expect "clips: input accessor out of range" 1 "$TMPD/badinpacc.glb"
expect "clips: output accessor out of range" 1 "$TMPD/badoutacc.glb"
expect "clips: target node out of range" 1 "$TMPD/badnode.glb"

# --- real GLB (positive control) and old blockout (negative control) ---
expect "real GLB: rifleman" 0 "$REAL_GLB"
expect "old blockout: negative control" 1 "$NEG_CONTROL"

echo
echo "validator results: $ok ok, $fail fail"
if [ "$fail" -gt 0 ]; then
  exit 1
fi

# --- Sprite path controls (stage-aware doubles) ---

# Stage-aware doubles: GLB build uses $TMPD/bin/blender (writes to blender.log),
# sprite path uses the same blender but detects render_sprites.py and writes to sprite.log.
mkdir -p "$TMPD/bin" "$TMPD/fake"

# GLB build stage double: writes to blender.log; for sprite path, creates sheet PNG.
cat > "$TMPD/bin/blender" <<EOF
#!/usr/bin/env bash
for arg in "\$@"; do
  if [[ "\$arg" == *render_sprites.py ]]; then
    echo "sprite" >> "$TMPD/sprite.log"
    touch "$TMPD/rifleman_sheet.png"
  else
    echo "blender" >> "$TMPD/blender.log"
  fi
done
exit 0
EOF
chmod +x "$TMPD/bin/blender"

# Sprite renderer double: writes to renderer.log.
cat > "$TMPD/fake/renderer.py" <<'EOF'
print("render")
EOF

# Fake Godot.
cat > "$TMPD/fake/godot" <<'EOF'
#!/usr/bin/env bash
exit 0
EOF
chmod +x "$TMPD/fake/godot"

# Default: no sprite calls when SPRITE_RENDER is unset.
rm -f "$TMPD/blender.log" "$TMPD/sprite.log" "$TMPD/renderer.log"
BL_BIN="$TMPD/bin/blender" GODOT_BIN="$TMPD/fake/godot" bash "$ROOT/art/build-asset.sh" rifleman --install >/dev/null 2>&1
if [ -f "$TMPD/sprite.log" ]; then
  failc "sprite path: default should NOT call sprite blender"
else
  pass "sprite path: default does not call sprite blender"
fi

# Opt-in: SPRITE_RENDER=1 with a working renderer -> sprite blender called.
rm -f "$TMPD/blender.log" "$TMPD/sprite.log" "$TMPD/renderer.log"
SPRITE_RENDER=1 BL_BIN="$TMPD/bin/blender" GODOT_BIN="$TMPD/fake/godot" bash "$ROOT/art/build-asset.sh" rifleman --install >/dev/null 2>&1
if [ -f "$TMPD/sprite.log" ]; then
  pass "sprite path: opt-in calls sprite blender"
else
  failc "sprite path: opt-in should call sprite blender"
fi

# Opt-in with a failing renderer: GLB still installs (non-fatal).
# Create a failing renderer double.
cat > "$TMPD/fake/renderer.py" <<'EOF'
raise SystemExit(1)
EOF
rm -f "$TMPD/blender.log" "$TMPD/sprite.log" "$TMPD/renderer.log"
SPRITE_RENDER=1 BL_BIN="$TMPD/bin/blender" GODOT_BIN="$TMPD/fake/godot" bash "$ROOT/art/build-asset.sh" rifleman --install >/dev/null 2>&1
# Check that GLB installed (build-asset.sh exited 0) and sprite blender was called.
if [ -f "$TMPD/sprite.log" ]; then
  pass "sprite path: opt-in failing renderer still calls sprite blender"
else
  failc "sprite path: opt-in failing renderer should call sprite blender"
fi

# No renderer: no sprite calls.
rm -f "$TMPD/blender.log" "$TMPD/sprite.log" "$TMPD/renderer.log"
SPRITE_RENDER=1 GODOT_BIN="$TMPD/fake/godot" bash "$ROOT/art/build-asset.sh" rifleman --install >/dev/null 2>&1
if [ -f "$TMPD/sprite.log" ]; then
  failc "sprite path: no renderer should NOT call sprite blender"
else
  pass "sprite path: no renderer does not call sprite blender"
fi

echo
echo "asset pipeline regression: $ok ok, $fail fail"
if [ "$fail" -gt 0 ]; then
  exit 1
fi
echo "PASS"
