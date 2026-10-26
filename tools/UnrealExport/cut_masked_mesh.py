"""Turns a masked (alpha cut-out) glTF mesh into plain geometry, for the engine's opaque material path (ADR-0078/M0105).

The engine's Stage shader has no alpha test, so a mesh whose silhouette the material cuts out of a texture (UE's M_Cursor: an arrow
texture on ribbons, BLEND_Masked) would draw the whole ribbon, the transparent texels as black. This script cuts the silhouette out of the
geometry instead: every quad of the mesh is tessellated, and a cell is kept only where the base-colour texture's alpha reaches the
material's alphaCutoff. The kept cells get the colour of the cut-out texels (their average), as a material factor and with no texture.

Usage: python cut_masked_mesh.py <input.glb> <output.glb> [cells-per-quad-side]
The input is the glb UE's glTF exporter writes (run_export_skeletal_mesh.cmd with CGE_SK_STATIC=1). The mesh must be made of quads, each
written as two consecutive triangles that share an edge (the cursor mesh is; a quad is recognised by its four vertices' UVs).
"""
import io
import json
import struct
import sys

import numpy as np
from PIL import Image

COMPONENT_DTYPES = {5121: np.uint8, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
TYPE_WIDTHS = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}


def read_glb(path):
    data = open(path, "rb").read()
    json_length = struct.unpack("<I", data[12:16])[0]
    document = json.loads(data[20:20 + json_length])
    offset = 20 + json_length
    bin_length = struct.unpack("<I", data[offset:offset + 4])[0]
    return document, data[offset + 8:offset + 8 + bin_length]


def accessor(document, blob, index):
    entry = document["accessors"][index]
    view = document["bufferViews"][entry["bufferView"]]
    width = TYPE_WIDTHS[entry["type"]]
    start = view.get("byteOffset", 0) + entry.get("byteOffset", 0)
    array = np.frombuffer(blob, dtype=COMPONENT_DTYPES[entry["componentType"]], count=entry["count"] * width, offset=start)
    return array.reshape(entry["count"], width)


def srgb_to_linear(values):
    values = np.asarray(values, dtype=np.float64)
    return np.where(values <= 0.04045, values / 12.92, ((values + 0.055) / 1.055) ** 2.4)


def main():
    source, target = sys.argv[1], sys.argv[2]
    cells = int(sys.argv[3]) if len(sys.argv) > 3 else 24
    document, blob = read_glb(source)
    primitive = document["meshes"][0]["primitives"][0]
    attributes = primitive["attributes"]
    positions = accessor(document, blob, attributes["POSITION"]).astype(np.float64)
    normals = accessor(document, blob, attributes["NORMAL"]).astype(np.float64)
    tangents = accessor(document, blob, attributes["TANGENT"]).astype(np.float64)
    uvs = accessor(document, blob, attributes["TEXCOORD_0"]).astype(np.float64)
    indices = accessor(document, blob, primitive["indices"]).reshape(-1).astype(np.int64)
    material = document["materials"][primitive["material"]]
    cutoff = material.get("alphaCutoff", 0.5)
    double_sided = material.get("doubleSided", False)
    texture_index = material["pbrMetallicRoughness"]["baseColorTexture"]["index"]
    image_index = document["textures"][texture_index]["source"]
    image_view = document["bufferViews"][document["images"][image_index]["bufferView"]]
    image_bytes = blob[image_view.get("byteOffset", 0):image_view.get("byteOffset", 0) + image_view["byteLength"]]
    image = np.asarray(Image.open(io.BytesIO(image_bytes)).convert("RGBA"), dtype=np.float64) / 255.0
    height, width = image.shape[0], image.shape[1]

    def sample(u, v):
        x = np.clip((u * width).astype(int), 0, width - 1)
        y = np.clip((v * height).astype(int), 0, height - 1)
        return image[y, x]

    triangles = indices.reshape(-1, 3)
    if len(triangles) % 2 != 0:
        raise SystemExit("The mesh is not made of quads (an odd number of triangles).")
    out_positions, out_normals, out_tangents, out_uvs, out_indices = [], [], [], [], []
    kept_colors = []
    steps = np.arange(cells + 1) / cells
    for first in range(0, len(triangles), 2):
        corner_ids = sorted(set(triangles[first]) | set(triangles[first + 1]))
        if len(corner_ids) != 4:
            raise SystemExit("Triangles %d and %d do not share an edge." % (first, first + 1))
        # UV に従って4隅を配置します：(low u, low v)、(high u, low v)、(low u, high v)、(high u, high v)。
        corner_uvs = uvs[corner_ids]
        low_u, high_u = corner_uvs[:, 0].min(), corner_uvs[:, 0].max()
        low_v, high_v = corner_uvs[:, 1].min(), corner_uvs[:, 1].max()
        grid_ids = {}
        for corner_id in corner_ids:
            u_high = uvs[corner_id, 0] > (low_u + high_u) / 2
            v_high = uvs[corner_id, 1] > (low_v + high_v) / 2
            grid_ids[(u_high, v_high)] = corner_id
        if len(grid_ids) != 4:
            raise SystemExit("The corners of quad %d are not axis-aligned in UV space." % (first // 2))

        def lerp(attribute, s, t):
            a, b = attribute[grid_ids[(False, False)]], attribute[grid_ids[(True, False)]]
            c, d = attribute[grid_ids[(False, True)]], attribute[grid_ids[(True, True)]]
            return (a * (1 - s) + b * s) * (1 - t) + (c * (1 - s) + d * s) * t

        s_grid, t_grid = np.meshgrid(steps, steps, indexing="xy")
        s_flat, t_flat = s_grid.reshape(-1, 1), t_grid.reshape(-1, 1)
        quad_positions = lerp(positions, s_flat, t_flat)
        quad_normals = lerp(normals, s_flat, t_flat)
        quad_normals /= np.linalg.norm(quad_normals, axis=1, keepdims=True)
        quad_tangents = lerp(tangents, s_flat, t_flat)
        quad_tangents[:, :3] /= np.linalg.norm(quad_tangents[:, :3], axis=1, keepdims=True)
        quad_uvs = lerp(uvs, s_flat, t_flat)
        side = cells + 1
        used = {}
        for row in range(cells):
            for column in range(cells):
                center_u = low_u + (high_u - low_u) * (column + 0.5) / cells
                center_v = low_v + (high_v - low_v) * (row + 0.5) / cells
                texel = sample(np.array([center_u]), np.array([center_v]))[0]
                if texel[3] < cutoff:
                    continue
                kept_colors.append(texel[:3])
                corner_cells = [row * side + column, row * side + column + 1, (row + 1) * side + column + 1, (row + 1) * side + column]
                ids = []
                for cell in corner_cells:
                    if cell not in used:
                        used[cell] = len(out_positions)
                        out_positions.append(quad_positions[cell])
                        out_normals.append(quad_normals[cell])
                        out_tangents.append(quad_tangents[cell])
                        out_uvs.append(quad_uvs[cell])
                    ids.append(used[cell])
                # 元の winding：quad 自身の三角形から、normal 方向に見て (s, t) が反時計回りかどうかを判定します。
                a, b, d = positions[grid_ids[(False, False)]], positions[grid_ids[(True, False)]], positions[grid_ids[(False, True)]]
                facing = np.dot(np.cross(b - a, d - a), normals[grid_ids[(False, False)]])
                if facing >= 0:
                    out_indices += [ids[0], ids[1], ids[2], ids[0], ids[2], ids[3]]
                else:
                    out_indices += [ids[0], ids[2], ids[1], ids[0], ids[3], ids[2]]
    if not out_indices:
        raise SystemExit("The mask left nothing of the mesh.")
    out_positions = np.array(out_positions, dtype=np.float32)
    out_normals = np.array(out_normals, dtype=np.float32)
    out_tangents = np.array(out_tangents, dtype=np.float32)
    out_uvs = np.array(out_uvs, dtype=np.float32)
    out_indices = np.array(out_indices, dtype=np.uint32)
    if double_sided:
        # renderer は back face を cull し、double-sided flag もありません。反対側は winding と normal を反転した複製で表します。
        count = len(out_positions)
        out_positions = np.concatenate([out_positions, out_positions])
        out_normals = np.concatenate([out_normals, -out_normals])
        flipped_tangents = out_tangents.copy()
        flipped_tangents[:, 3] = -flipped_tangents[:, 3]
        out_tangents = np.concatenate([out_tangents, flipped_tangents])
        out_uvs = np.concatenate([out_uvs, out_uvs])
        back = out_indices.reshape(-1, 3)[:, ::-1].reshape(-1) + count
        out_indices = np.concatenate([out_indices, back])
    base_color = srgb_to_linear(np.mean(kept_colors, axis=0))

    chunks = []

    def add(array, target_kind):
        data = array.tobytes()
        offset = sum(len(chunk) for chunk in chunks)
        chunks.append(data + b"\0" * (-len(data) % 4))
        return {"buffer": 0, "byteOffset": offset, "byteLength": len(data), "target": target_kind}

    views = [add(out_indices, 34963), add(out_positions, 34962), add(out_normals, 34962), add(out_tangents, 34962), add(out_uvs, 34962)]
    accessors = [
        {"bufferView": 0, "componentType": 5125, "count": int(len(out_indices)), "type": "SCALAR"},
        {"bufferView": 1, "componentType": 5126, "count": int(len(out_positions)), "type": "VEC3",
         "min": out_positions.min(axis=0).tolist(), "max": out_positions.max(axis=0).tolist()},
        {"bufferView": 2, "componentType": 5126, "count": int(len(out_normals)), "type": "VEC3"},
        {"bufferView": 3, "componentType": 5126, "count": int(len(out_tangents)), "type": "VEC4"},
        {"bufferView": 4, "componentType": 5126, "count": int(len(out_uvs)), "type": "VEC2"},
    ]
    blob_out = b"".join(chunks)
    result = {
        "asset": {"version": "2.0", "generator": "cut_masked_mesh.py (from " + document.get("asset", {}).get("generator", "glTF") + ")"},
        "scene": 0,
        "scenes": [{"nodes": [0]}],
        "nodes": [{"mesh": 0, "name": document["meshes"][0].get("name", "Mesh")}],
        "meshes": [{"name": document["meshes"][0].get("name", "Mesh"), "primitives": [{
            "attributes": {"POSITION": 1, "NORMAL": 2, "TANGENT": 3, "TEXCOORD_0": 4}, "indices": 0, "material": 0}]}],
        "materials": [{"name": material.get("name", "Material"), "pbrMetallicRoughness": {
            "baseColorFactor": [round(float(base_color[0]), 6), round(float(base_color[1]), 6), round(float(base_color[2]), 6), 1.0],
            "metallicFactor": 0.0, "roughnessFactor": 1.0}}],
        "accessors": accessors,
        "bufferViews": views,
        "buffers": [{"byteLength": len(blob_out)}],
    }
    json_bytes = json.dumps(result, separators=(",", ":")).encode("utf-8")
    json_bytes += b" " * (-len(json_bytes) % 4)
    total = 12 + 8 + len(json_bytes) + 8 + len(blob_out)
    with open(target, "wb") as handle:
        handle.write(struct.pack("<III", 0x46546C67, 2, total))
        handle.write(struct.pack("<II", len(json_bytes), 0x4E4F534A) + json_bytes)
        handle.write(struct.pack("<II", len(blob_out), 0x004E4942) + blob_out)
    print("wrote %s: %d triangles, %d vertices, colour %s" % (target, len(out_indices) // 3, len(out_positions), np.round(base_color, 4).tolist()))


if __name__ == "__main__":
    main()
