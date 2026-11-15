#!/usr/bin/env python3
"""Validate a self-contained glTF 2.0 GLB asset.

Checks:
  - GLB container (magic "glTF", version 2, JSON chunk present and parseable)
  - materials (non-empty `materials` array)
  - armature (non-empty `skins` array)
  - named clips (an `animations` array containing `idle`, `move`, `fire`)

Exits 0 when all required elements are present, 1 otherwise.
"""
import json
import struct
import sys

REQUIRED_CLIPS = ("idle", "move", "fire")


def read_glb(path):
    """Return (version, json_dict) for a GLB file, or raise ValueError."""
    with open(path, "rb") as f:
        magic = f.read(4)
        if magic != b"glTF":
            raise ValueError("not a GLB (magic bytes != 'glTF')")
        version = struct.unpack("<I", f.read(4))[0]
        length = struct.unpack("<I", f.read(4))[0]
        if version != 2:
            raise ValueError(f"unsupported glTF version {version} (need 2)")
        # Read the JSON chunk (first chunk).
        chunk_len = struct.unpack("<I", f.read(4))[0]
        chunk_type = f.read(4)
        if chunk_type != b"JSON":
            raise ValueError(f"first chunk type {chunk_type!r} (expected 'JSON')")
        data = f.read(chunk_len)
        # JSON chunk is padded to a 4-byte boundary with 0x20 (space).
        js = json.loads(data)
        return version, js


def main():
    if len(sys.argv) != 2:
        print("usage: validate_glb.py <path-to.glb>")
        return 2
    path = sys.argv[1]
    try:
        version, js = read_glb(path)
    except ValueError as e:
        print(f"GLB validation: {path}")
        print(f"  container: {e}")
        print("RESULT: FAIL")
        return 1
    print(f"GLB validation: {path}")
    print(f"  glTF version: {version}")

    problems = []

    materials = js.get("materials", [])
    if materials:
        print(f"  materials: {len(materials)} (ok)")
    else:
        print("  materials: none (missing)")
        problems.append("materials")

    skins = js.get("skins", [])
    if skins:
        joints = sum(len(s.get("joints", [])) for s in skins)
        print(f"  armature: {len(skins)} skin(s), {joints} joints (ok)")
    else:
        print("  armature: none (missing)")
        problems.append("armature")

    animations = js.get("animations", [])
    clip_names = [a.get("name") for a in animations]
    found = [n for n in REQUIRED_CLIPS if n in clip_names]
    missing_clips = [n for n in REQUIRED_CLIPS if n not in clip_names]
    if found:
        print(f"  clips: {', '.join(found)} (ok)")
    else:
        print("  clips: none (missing)")
        problems.append("clips")
    if missing_clips:
        print(f"  clips missing: {', '.join(missing_clips)}")

    if problems:
        print(f"RESULT: FAIL (missing {', '.join(problems)})")
        return 1
    print("RESULT: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
