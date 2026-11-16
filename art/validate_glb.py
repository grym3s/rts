#!/usr/bin/env python3
"""Validate a self-contained glTF 2.0 GLB asset.

Checks:
  - GLB container (magic "glTF", version 2, length matches file size, JSON chunk present and parseable)
  - self-contained (no external buffer `uri`)
  - binary data (BIN chunk present when a buffer has data; bufferView ranges within the buffer)
  - materials (non-empty `materials` array)
  - armature (non-empty `skins`, each skin with non-empty `joints` and a usable `inverseBindMatrices` reference)
  - named clips (an `animations` array containing all of `idle`, `move`, `fire`)

Exits 0 when all required elements are present, 1 otherwise.
"""
import json
import struct
import sys

REQUIRED_CLIPS = ("idle", "move", "fire")


def read_glb(path):
    """Return (version, json_dict, bin_size) for a GLB file, or raise ValueError."""
    with open(path, "rb") as f:
        data = f.read()
    if len(data) < 12:
        raise ValueError("file too short to be a GLB")
    magic = data[:4]
    if magic != b"glTF":
        raise ValueError("not a GLB (magic bytes != 'glTF')")
    version, = struct.unpack("<I", data[4:8])
    length, = struct.unpack("<I", data[8:12])
    if version != 2:
        raise ValueError(f"unsupported glTF version {version} (need 2)")
    if length != len(data):
        raise ValueError(f"container length {length} != file size {len(data)}")
    json_bytes = None
    bin_size = 0
    off = 12
    while off < len(data):
        if off + 8 > len(data):
            raise ValueError("truncated chunk header")
        chunk_len, = struct.unpack("<I", data[off:off + 4])
        chunk_type = data[off + 4:off + 8]
        off += 8
        if off + chunk_len > len(data):
            raise ValueError(f"truncated {chunk_type!r} chunk")
        payload = data[off:off + chunk_len]
        if chunk_type == b"JSON":
            json_bytes = payload
        elif chunk_type[:3] == b"BIN":
            bin_size = chunk_len
        off += chunk_len
    if json_bytes is None:
        raise ValueError("no JSON chunk")
    js = json.loads(json_bytes)
    return version, js, bin_size


def main():
    if len(sys.argv) != 2:
        print("usage: validate_glb.py <path-to.glb>")
        return 2
    path = sys.argv[1]
    try:
        version, js, bin_size = read_glb(path)
    except ValueError as e:
        print(f"GLB validation: {path}")
        print(f"  container: {e}")
        print("RESULT: FAIL")
        return 1
    print(f"GLB validation: {path}")
    print(f"  glTF version: {version}")

    problems = []
    buffers = js.get("buffers", [])

    # Self-contained: no external buffer references.
    external = [b.get("uri") for b in buffers if b.get("uri")]
    if external:
        print(f"  self-contained: external buffer uri {external} (not self-contained)")
        problems.append("self-contained")
    else:
        print(f"  self-contained: {len(buffers)} embedded buffer(s) (ok)")

    # Binary data: required BIN chunk present when any buffer has data.
    max_byte = max((b.get("byteLength", 0) for b in buffers), default=0)
    if max_byte > 0 and bin_size < max_byte:
        print(f"  binary data: BIN chunk {bin_size} < buffer byteLength {max_byte} (missing data)")
        problems.append("binary-data")
    else:
        print(f"  binary data: BIN chunk {bin_size} (ok)")

    # bufferView references: byteOffset + byteLength within the buffer.
    buffer_views = js.get("bufferViews", [])
    bad_views = 0
    for v in buffer_views:
        buf_idx = v.get("buffer", 0)
        buf_len = buffers[buf_idx].get("byteLength", 0) if buf_idx < len(buffers) else 0
        if v.get("byteOffset", 0) + v.get("byteLength", 0) > buf_len:
            bad_views += 1
    if bad_views:
        print(f"  bufferViews: {bad_views} out of range (missing data)")
        problems.append("bufferViews")
    else:
        print(f"  bufferViews: {len(buffer_views)} (ok)")

    # materials (non-empty).
    materials = js.get("materials", [])
    if materials:
        print(f"  materials: {len(materials)} (ok)")
    else:
        print("  materials: none (missing)")
        problems.append("materials")

    # armature: non-empty skins, each with non-empty joints and a usable inverseBindMatrices.
    skins = js.get("skins", [])
    bad_skins = []
    for i, s in enumerate(skins):
        if not s.get("joints"):
            bad_skins.append(f"skin{i}: empty joints")
            continue
        ibm = s.get("inverseBindMatrices")
        if ibm is None or ibm >= len(buffer_views):
            bad_skins.append(f"skin{i}: missing/invalid inverseBindMatrices")
        else:
            bv = buffer_views[ibm]
            buf_idx = bv.get("buffer", 0)
            buf_len = buffers[buf_idx].get("byteLength", 0) if buf_idx < len(buffers) else 0
            if bv.get("byteOffset", 0) + bv.get("byteLength", 0) > buf_len:
                bad_skins.append(f"skin{i}: inverseBindMatrices out of range")
    if skins and not bad_skins:
        joints_total = sum(len(s.get("joints", [])) for s in skins)
        print(f"  armature: {len(skins)} skin(s), {joints_total} joints (ok)")
    elif not skins:
        print("  armature: none (missing)")
        problems.append("armature")
    else:
        print(f"  armature: {bad_skins} (unusable)")
        problems.append("armature")

    # clips: all required clips present.
    animations = js.get("animations", [])
    clip_names = [a.get("name") for a in animations]
    missing_clips = [n for n in REQUIRED_CLIPS if n not in clip_names]
    if missing_clips:
        print(f"  clips missing: {', '.join(missing_clips)}")
        problems.append("clips")
    else:
        print(f"  clips: {', '.join(clip_names)} (ok)")

    if problems:
        print(f"RESULT: FAIL (missing {', '.join(problems)})")
        return 1
    print("RESULT: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
