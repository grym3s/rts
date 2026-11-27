#!/usr/bin/env python3
"""Validate a self-contained glTF 2.0 GLB asset.

Checks:
  - GLB container (magic "glTF", version 2, length matches file size, JSON chunk present and parseable)
  - self-contained (no external buffer `uri` and no external image `uri`; data URIs are embedded;
    image `bufferView` references are valid)
  - binary data (BIN chunk present when a buffer has data; bufferView ranges within the buffer)
  - materials (non-empty `materials` array)
  - armature (non-empty `skins`, each skin with non-empty `joints` whose node indices exist,
    and a usable `inverseBindMatrices` reference — an accessor index into bufferView -> buffer
    with valid extent and joint coverage; omitted matrices are valid (identity per glTF 2.0))
  - named clips (an `animations` array containing all of `idle`, `move`, `fire`,
    each with usable channels, samplers, and valid input/output accessor references)

Exits 0 when all required elements are present and usable, 1 otherwise.
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
    images = js.get("images", [])
    buffer_views = js.get("bufferViews", [])

    # Self-contained: no external buffer references.
    # Data URIs (data:image/...) are embedded; external file URIs are not.
    external_bufs = [b.get("uri") for b in buffers if b.get("uri") and not b.get("uri", "").startswith("data:")]
    ext_images = []
    for im in images:
        uri = im.get("uri")
        if uri and not uri.startswith("data:"):
            ext_images.append(uri)
    # Image bufferView references must be valid.
    bad_img_views = []
    for im in images:
        bv_idx = im.get("bufferView")
        if bv_idx is not None and (bv_idx >= len(buffer_views) or bv_idx < 0):
            bad_img_views.append(f"bufferView {bv_idx}")
    if external_bufs or ext_images or bad_img_views:
        print(f"  self-contained: ext buffer {external_bufs} / ext image {ext_images} / bad img view {bad_img_views} (not self-contained)")
        problems.append("self-contained")
    else:
        print(f"  self-contained: {len(buffers)} embedded buffer(s), {len(images)} image(s) (ok)")

    # Binary data: required BIN chunk present when any buffer has data.
    max_byte = max((b.get("byteLength", 0) for b in buffers), default=0)
    if max_byte > 0 and bin_size < max_byte:
        print(f"  binary data: BIN chunk {bin_size} < buffer byteLength {max_byte} (missing data)")
        problems.append("binary-data")
    else:
        print(f"  binary data: BIN chunk {bin_size} (ok)")

    # bufferView references: byteOffset + byteLength within the buffer.
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

    # armature: non-empty skins; each skin needs non-empty joints whose node
    # references exist, plus a usable inverseBindMatrices reference (optional).
    accessors = js.get("accessors", [])
    nodes = js.get("nodes", [])
    skins = js.get("skins", [])
    bad_skins = []
    for i, s in enumerate(skins):
        joints = s.get("joints")
        if not joints:
            bad_skins.append(f"skin{i}: empty joints")
            continue
        missing_nodes = [n for n in joints if n >= len(nodes)]
        if missing_nodes:
            bad_skins.append(f"skin{i}: joint node indices {missing_nodes} out of range")
            continue
        ibm = s.get("inverseBindMatrices")
        if ibm is None:
            continue  # optional; omitted = identity matrices (valid)
        if ibm >= len(accessors):
            bad_skins.append(f"skin{i}: inverseBindMatrices accessor {ibm} out of range")
            continue
        acc = accessors[ibm]
        bv_idx = acc.get("bufferView")
        if bv_idx is None or bv_idx >= len(buffer_views):
            bad_skins.append(f"skin{i}: inverseBindMatrices accessor {ibm} has no valid bufferView")
            continue
        bv = buffer_views[bv_idx]
        buf_idx = bv.get("buffer", 0)
        buf_len = buffers[buf_idx].get("byteLength", 0) if buf_idx < len(buffers) else 0
        bv_end = bv.get("byteOffset", 0) + bv.get("byteLength", 0)
        if bv_end > buf_len:
            bad_skins.append(f"skin{i}: inverseBindMatrices bufferView out of range")
        else:
            # Accessor extent: byteOffset + count*componentSize must fit in bufferView.
            comp_size = {"SCALAR": 4, "VEC2": 8, "VEC3": 12, "VEC4": 16, "MAT4": 64}.get(acc.get("type"), 4)
            acc_bytes = acc.get("byteOffset", 0) + acc.get("count", 0) * comp_size
            if acc_bytes > bv.get("byteLength", 0):
                bad_skins.append(f"skin{i}: inverseBindMatrices accessor extent {acc_bytes} exceeds bufferView {bv.get('byteLength', 0)}")
            # Type must be MAT4 with FLOAT component (glTF 2.0 spec).
            if acc.get("type") != "MAT4":
                bad_skins.append(f"skin{i}: inverseBindMatrices type {acc.get('type')} is not MAT4")
            if acc.get("componentType") != 5126:
                bad_skins.append(f"skin{i}: inverseBindMatrices componentType {acc.get('componentType')} is not FLOAT")
            # Joint coverage: count must be >= number of joints (glTF permits extra).
            if acc.get("count", 0) < len(joints):
                bad_skins.append(f"skin{i}: inverseBindMatrices count {acc.get('count', 0)} < joints {len(joints)}")
    if skins and not bad_skins:
        joints_total = sum(len(s.get("joints", [])) for s in skins)
        print(f"  armature: {len(skins)} skin(s), {joints_total} joints (ok)")
    elif not skins:
        print("  armature: none (missing)")
        problems.append("armature")
    else:
        print(f"  armature: {bad_skins} (unusable)")
        problems.append("armature")

    # clips: all required clips present, each with usable channels and samplers.
    animations = js.get("animations", [])
    clip_by_name = {a.get("name"): a for a in animations}
    clip_problems = []
    for n in REQUIRED_CLIPS:
        a = clip_by_name.get(n)
        if a is None:
            clip_problems.append(f"{n} missing")
            continue
        if not a.get("channels"):
            clip_problems.append(f"{n} has no channels")
            continue
        if not a.get("samplers"):
            clip_problems.append(f"{n} has no samplers")
            continue
        for c in a.get("channels", []):
            sidx = c.get("sampler")
            if sidx is None or sidx >= len(a.get("samplers", [])):
                clip_problems.append(f"{n} channel sampler {sidx} out of range")
                break
            # Target node must reference a valid node.
            tgt = c.get("target", {})
            node_idx = tgt.get("node")
            if node_idx is None or node_idx >= len(nodes) or node_idx < 0:
                clip_problems.append(f"{n} channel target node {node_idx} invalid")
                break
        # Sampler input/output accessors must reference valid accessors.
        for s in a.get("samplers", []):
            inp = s.get("input")
            if inp is None or inp >= len(accessors) or inp < 0:
                clip_problems.append(f"{n} sampler input accessor {inp} invalid")
                break
            out = s.get("output")
            if out is None or out >= len(accessors) or out < 0:
                clip_problems.append(f"{n} sampler output accessor {out} invalid")
                break
            # Validate referenced data: non-empty, float component, input must be SCALAR.
            inp_acc = accessors[inp]
            out_acc = accessors[out]
            if inp_acc.get("count", 0) == 0:
                clip_problems.append(f"{n} sampler input accessor {inp} is empty")
                break
            if out_acc.get("count", 0) == 0:
                clip_problems.append(f"{n} sampler output accessor {out} is empty")
                break
            if inp_acc.get("componentType") != 5126:
                clip_problems.append(f"{n} sampler input accessor {inp} componentType {inp_acc.get('componentType')} is not FLOAT")
                break
            if inp_acc.get("type") != "SCALAR":
                clip_problems.append(f"{n} sampler input accessor {inp} type {inp_acc.get('type')} is not SCALAR")
                break
            if out_acc.get("componentType") != 5126:
                clip_problems.append(f"{n} sampler output accessor {out} componentType {out_acc.get('componentType')} is not FLOAT")
                break
            # Accessor extent: data must fit within its bufferView.
            bad_extent = False
            for label, acc in (("input", inp_acc), ("output", out_acc)):
                bv_idx = acc.get("bufferView")
                if bv_idx is None or bv_idx >= len(buffer_views):
                    continue
                bv = buffer_views[bv_idx]
                comp_size = {"SCALAR": 4, "VEC2": 8, "VEC3": 12, "VEC4": 16, "MAT4": 64}.get(acc.get("type", ""), 0)
                extent = acc.get("byteOffset", 0) + acc.get("count", 0) * comp_size
                if extent > bv.get("byteLength", 0):
                    clip_problems.append(f"{n} sampler {label} accessor extent {extent} exceeds bufferView {bv.get('byteLength', 0)}")
                    bad_extent = True
                    break
            if bad_extent:
                break
    if clip_problems:
        print(f"  clips: {', '.join(clip_problems)} (unusable)")
        problems.append("clips")
    else:
        print(f"  clips: {', '.join(REQUIRED_CLIPS)} (ok)")

    if problems:
        print(f"RESULT: FAIL (missing {', '.join(problems)})")
        return 1
    print("RESULT: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
