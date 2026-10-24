"""Builds the Vehicle template's ground meshes from what the UE probes read (ADR-0078, M0112): the Landscape terrain and the spline track ring.

Usage: python build_vehicle_ground.py <heights.json> <SM_Track_10M.glb> <ground_color.png> <template-dir>
  heights.json     probe_vehicle_landscape_heights.py's output (Landscape heights on a 1 m grid, SplineMeshComponent parameters)
  SM_Track_10M.glb the glb run_export_skeletal_mesh.cmd writes for /Game/VehicleTemplate/Meshes/SM_Track_10M (CGE_SK_STATIC=1)
  ground_color.png capture_vehicle_ground_color.py's top-down base-colour capture of the Landscape (sRGB)
  template-dir     Templates/CSharpGameEngine.Templates.Vehicle
Writes Assets/Stage/Lvl_VehicleBasic/Meshes/Landscape_Terrain.glb, Track_Road.glb (the road: collides) and Track_Curbs.glb (the curbs: drawn only)
under template-dir, and swaps the scene's flat "Ground" slab for the "Landscape", "Track" and "TrackCurbs" nodes that use them
(Scenes/Lvl_VehicleBasic.json, edited as text so the rest of the file stays as it was; running it again replaces the same nodes).

The curbs have no collider on purpose: the engine's box-versus-triangle-mesh contact (PhysicsNarrowPhase.TryBoxMeshManifold) keeps one face orientation per
step, and a car straddling a curb's slope and the flat ground lost the ground's support and sank (VehicleLevelTests documents the road-only level).

Why not a glTF export: UE's glTF exporter cannot write a Landscape or a Landscape spline's SplineMeshComponents (the engine has no terrain
component either), so the Landscape becomes a static triangle mesh built from the height field, with its albedo captured from above, and
each spline mesh is deformed here with the formulas of UE's SplineMeshCommon.ush and merged into one mesh.

glTF space is the one UE's exporter writes: (x, y, z) = (X, Z, Y) of UE in metres, so the engine importer maps it to R = X, U = Z, F = -Y like
every other level mesh, and the meshes are placed at the scene origin with an identity Transform.
"""
import io
import json
import os
import re
import struct
import sys
import uuid

import numpy as np
from PIL import Image

COMPONENT_DTYPES = {5121: np.uint8, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
TYPE_WIDTHS = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}
LANDSCAPE_HALF_CM = 6300.0
TILE_QUADS = 7
GROUND_ROUGHNESS = 0.8
FLAT_TOLERANCE_CM = 1e-3


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


class GlbBuilder:
    """A minimal glb writer: one mesh with any number of primitives, materials with a base-colour texture, one sampler."""

    def __init__(self):
        self.blob = bytearray()
        self.views = []
        self.accessors = []
        self.images = []
        self.textures = []
        self.materials = []
        self.primitives = []
        self.samplers = [{"magFilter": 9729, "minFilter": 9987, "wrapS": 10497, "wrapT": 10497}]

    def view(self, data: bytes, target=None):
        while len(self.blob) % 4:
            self.blob.append(0)
        entry = {"buffer": 0, "byteOffset": len(self.blob), "byteLength": len(data)}
        if target is not None:
            entry["target"] = target
        self.blob.extend(data)
        self.views.append(entry)
        return len(self.views) - 1

    def accessor(self, array, component_type, type_name, target=None, with_bounds=False):
        view = self.view(np.ascontiguousarray(array).tobytes(), target)
        entry = {"bufferView": view, "componentType": component_type, "count": int(array.shape[0]), "type": type_name}
        if with_bounds:
            entry["min"] = [float(v) for v in array.min(axis=0)]
            entry["max"] = [float(v) for v in array.max(axis=0)]
        self.accessors.append(entry)
        return len(self.accessors) - 1

    def image(self, png_bytes: bytes, name: str, clamp=False):
        view = self.view(png_bytes)
        self.images.append({"name": name, "mimeType": "image/png", "bufferView": view})
        sampler = len(self.samplers)
        if clamp:
            self.samplers.append({"magFilter": 9729, "minFilter": 9987, "wrapS": 33071, "wrapT": 33071})
        self.textures.append({"name": name, "sampler": sampler if clamp else 0, "source": len(self.images) - 1})
        return len(self.textures) - 1

    def material(self, name, texture, roughness, metallic=0.0):
        self.materials.append({"name": name, "pbrMetallicRoughness": {"baseColorTexture": {"index": texture}, "metallicFactor": metallic, "roughnessFactor": roughness}})
        return len(self.materials) - 1

    def primitive(self, positions, normals, uvs, indices, material):
        attributes = {
            "POSITION": self.accessor(positions.astype(np.float32), 5126, "VEC3", 34962, True),
            "NORMAL": self.accessor(normals.astype(np.float32), 5126, "VEC3", 34962),
            "TEXCOORD_0": self.accessor(uvs.astype(np.float32), 5126, "VEC2", 34962),
        }
        index = self.accessor(indices.astype(np.uint32).reshape(-1, 1), 5125, "SCALAR", 34963)
        self.primitives.append({"attributes": attributes, "indices": index, "material": material})

    def write(self, path, mesh_name):
        document = {
            "asset": {"version": "2.0", "generator": "CSharpGameEngine build_vehicle_ground.py"},
            "scene": 0,
            "scenes": [{"nodes": [0]}],
            "nodes": [{"mesh": 0, "name": mesh_name}],
            "meshes": [{"name": mesh_name, "primitives": self.primitives}],
            "materials": self.materials,
            "textures": self.textures,
            "images": self.images,
            "samplers": self.samplers,
            "accessors": self.accessors,
            "bufferViews": self.views,
            "buffers": [{"byteLength": len(self.blob)}],
        }
        text = json.dumps(document, separators=(",", ":")).encode("utf-8")
        text += b" " * ((4 - len(text) % 4) % 4)
        blob = bytes(self.blob) + b"\0" * ((4 - len(self.blob) % 4) % 4)
        total = 12 + 8 + len(text) + 8 + len(blob)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "wb") as output:
            output.write(struct.pack("<III", 0x46546C67, 2, total))
            output.write(struct.pack("<II", len(text), 0x4E4F534A))
            output.write(text)
            output.write(struct.pack("<II", len(blob), 0x004E4942))
            output.write(blob)


def png_bytes(image: Image.Image) -> bytes:
    buffer = io.BytesIO()
    image.save(buffer, format="PNG", optimize=True)
    return buffer.getvalue()


# ---- terrain ------------------------------------------------------------------------------------------------------------

def build_terrain(heights_cm: np.ndarray, ground: Image.Image, output_path: str) -> dict:
    """Heights (ny, nx) in cm at 1 m spacing, row = UE Y, column = UE X, starting at (-63 m, -63 m)."""
    ny, nx = heights_cm.shape
    quads = nx - 1
    if quads % TILE_QUADS:
        raise RuntimeError(f"The height grid has {quads} quads per side, not a multiple of {TILE_QUADS}")
    spacing_m = 1.0
    heights_m = heights_cm / 100.0
    index_of = {}
    vertices = []

    def vertex(ix, iy):
        key = (ix, iy)
        found = index_of.get(key)
        if found is None:
            found = len(vertices)
            index_of[key] = found
            vertices.append(key)
        return found

    triangles = []
    flat_tiles = 0
    for tile_y in range(0, quads, TILE_QUADS):
        for tile_x in range(0, quads, TILE_QUADS):
            block = heights_cm[tile_y:tile_y + TILE_QUADS + 1, tile_x:tile_x + TILE_QUADS + 1]
            if np.all(np.abs(block) <= FLAT_TOLERANCE_CM):
                flat_tiles += 1
                a, b = vertex(tile_x, tile_y), vertex(tile_x + TILE_QUADS, tile_y)
                c, d = vertex(tile_x, tile_y + TILE_QUADS), vertex(tile_x + TILE_QUADS, tile_y + TILE_QUADS)
                triangles.append((a, d, b))
                triangles.append((a, c, d))
                continue
            for iy in range(tile_y, tile_y + TILE_QUADS):
                for ix in range(tile_x, tile_x + TILE_QUADS):
                    a, b = vertex(ix, iy), vertex(ix + 1, iy)
                    c, d = vertex(ix, iy + 1), vertex(ix + 1, iy + 1)
                    # UE's Landscape splits every quad along the diagonal (x, y) - (x + 1, y + 1).
                    triangles.append((a, d, b))
                    triangles.append((a, c, d))
    keys = np.array(vertices, dtype=np.int64)
    ix, iy = keys[:, 0], keys[:, 1]
    x_ue_m = -LANDSCAPE_HALF_CM / 100.0 + ix * spacing_m
    y_ue_m = -LANDSCAPE_HALF_CM / 100.0 + iy * spacing_m
    z_ue_m = heights_m[iy, ix]
    # Normals from central differences of the height field (what the Landscape's normal map holds).
    padded = np.pad(heights_m, 1, mode="edge")
    dzdx = (padded[1:-1, 2:] - padded[1:-1, :-2]) / (2.0 * spacing_m)
    dzdy = (padded[2:, 1:-1] - padded[:-2, 1:-1]) / (2.0 * spacing_m)
    normal_ue = np.stack([-dzdx[iy, ix], -dzdy[iy, ix], np.ones(len(ix))], axis=1)
    normal_ue /= np.linalg.norm(normal_ue, axis=1, keepdims=True)
    positions = np.stack([x_ue_m, z_ue_m, y_ue_m], axis=1)
    normals = np.stack([normal_ue[:, 0], normal_ue[:, 2], normal_ue[:, 1]], axis=1)
    # The capture's image right is UE +Y and image up is UE +X (capture_vehicle_ground_color.py: a camera at yaw 0 looking down).
    uvs = np.stack([(y_ue_m * 100.0 + LANDSCAPE_HALF_CM) / (2.0 * LANDSCAPE_HALF_CM), 1.0 - (x_ue_m * 100.0 + LANDSCAPE_HALF_CM) / (2.0 * LANDSCAPE_HALF_CM)], axis=1)
    indices = np.array(triangles, dtype=np.uint32)
    # glTF is right-handed with the Y and Z of UE swapped, so the winding that is counter-clockwise from above in UE (x, y) is clockwise here:
    # check one triangle against its normal and flip the whole mesh if needed.
    p0, p1, p2 = positions[indices[0]]
    if np.dot(np.cross(p1 - p0, p2 - p0), normals[indices[0, 0]]) < 0:
        indices = indices[:, ::-1].copy()
    builder = GlbBuilder()
    texture = builder.image(png_bytes(ground), "Landscape_BaseColor", clamp=True)
    material = builder.material("M_Landscape", texture, GROUND_ROUGHNESS)
    builder.primitive(positions, normals, uvs, indices, material)
    builder.write(output_path, "Landscape_Terrain")
    return {"vertices": len(positions), "triangles": len(indices), "flatTiles": flat_tiles, "bytes": os.path.getsize(output_path)}


# ---- spline mesh --------------------------------------------------------------------------------------------------------

def hermite_position(start, start_tangent, end, end_tangent, alpha):
    a2, a3 = alpha * alpha, alpha * alpha * alpha
    return ((2 * a3 - 3 * a2 + 1)[:, None] * start + (a3 - 2 * a2 + alpha)[:, None] * start_tangent
            + (a3 - a2)[:, None] * end_tangent + (-2 * a3 + 3 * a2)[:, None] * end)


def hermite_direction(start, start_tangent, end, end_tangent, alpha):
    c = 6 * start + 3 * start_tangent + 3 * end_tangent - 6 * end
    d = -6 * start - 4 * start_tangent - 2 * end_tangent + 6 * end
    e = start_tangent
    direction = (alpha * alpha)[:, None] * c + alpha[:, None] * d + e
    return direction / np.linalg.norm(direction, axis=1, keepdims=True)


def deform_segment(component, local_ue_cm, normal_ue, length_cm):
    """UE SplineMeshCommon.ush for a forward axis X: SplineMeshCalcSlice, SplineMeshCalcSliceTransform and the normal's SliceRot."""
    start = np.array(component["startPos"], dtype=np.float64)
    end = np.array(component["endPos"], dtype=np.float64)
    start_tangent = np.array(component["startTangent"], dtype=np.float64)
    end_tangent = np.array(component["endTangent"], dtype=np.float64)
    up = np.array(component["splineUpDir"], dtype=np.float64)
    alpha = local_ue_cm[:, 0] / length_cm
    position = hermite_position(start, start_tangent, end, end_tangent, alpha)
    direction = hermite_direction(start, start_tangent, end, end_tangent, alpha)
    smooth = component["smoothInterpRollScale"]
    blend = alpha * alpha * (3 - 2 * alpha) if smooth else alpha
    scale = np.array(component["startScale"])[None, :] * (1 - blend)[:, None] + np.array(component["endScale"])[None, :] * blend[:, None]
    roll = np.array(component["startRoll"]) * (1 - blend) + np.array(component["endRoll"]) * blend
    offset = np.array(component["startOffset"])[None, :] * (1 - blend)[:, None] + np.array(component["endOffset"])[None, :] * blend[:, None]
    base_x = np.cross(up[None, :], direction)
    base_x /= np.linalg.norm(base_x, axis=1, keepdims=True)
    base_y = np.cross(direction, base_x)
    sin, cos = np.sin(roll)[:, None], np.cos(roll)[:, None]
    x_vec = cos * base_x - sin * base_y
    y_vec = cos * base_y + sin * base_x
    slice_pos = position + offset[:, 0:1] * base_x + offset[:, 1:2] * base_y
    deformed = slice_pos + (local_ue_cm[:, 1] * scale[:, 0])[:, None] * x_vec + (local_ue_cm[:, 2] * scale[:, 1])[:, None] * y_vec
    new_normal = normal_ue[:, 0:1] * direction + normal_ue[:, 1:2] * x_vec + normal_ue[:, 2:3] * y_vec
    new_normal /= np.linalg.norm(new_normal, axis=1, keepdims=True)
    return deformed, new_normal


def build_track_ring(components, track_glb_path: str, output_path: str, mesh_name: str, material_slot: int) -> dict:
    """Deforms the track mesh's primitives of one material slot (0 = the curbs, MI_TrackEdge; 1 = the road, MI_TrackEdge_Inst) along every spline segment."""
    document, blob = read_glb(track_glb_path)
    mesh = document["meshes"][0]
    builder = GlbBuilder()
    wanted = [primitive for primitive in mesh["primitives"] if primitive["material"] == material_slot]
    if not wanted:
        raise RuntimeError(f"The track mesh has no primitive with material slot {material_slot}")
    material = document["materials"][material_slot]
    texture = material["pbrMetallicRoughness"]["baseColorTexture"]["index"]
    image = document["images"][document["textures"][texture]["source"]]
    view = document["bufferViews"][image["bufferView"]]
    data = blob[view.get("byteOffset", 0):view.get("byteOffset", 0) + view["byteLength"]]
    # M_Track: Metallic 0, Roughness 1 (T3D defaults); the exporter writes neither as a factor.
    material_id = builder.material(material["name"], builder.image(bytes(data), image["name"]), 1.0)
    totals = []
    for primitive in wanted:
        attributes = primitive["attributes"]
        # glTF -> UE local (cm): UE X = x, UE Y = z, UE Z = y.
        gltf_positions = accessor(document, blob, attributes["POSITION"]).astype(np.float64)
        gltf_normals = accessor(document, blob, attributes["NORMAL"]).astype(np.float64)
        uvs = accessor(document, blob, attributes["TEXCOORD_0"]).astype(np.float64)
        indices = accessor(document, blob, primitive["indices"]).reshape(-1).astype(np.int64)
        local_cm = np.stack([gltf_positions[:, 0], gltf_positions[:, 2], gltf_positions[:, 1]], axis=1) * 100.0
        local_normal = np.stack([gltf_normals[:, 0], gltf_normals[:, 2], gltf_normals[:, 1]], axis=1)
        length_cm = float(local_cm[:, 0].max() - local_cm[:, 0].min())
        if abs(local_cm[:, 0].min()) > 1e-3:
            raise RuntimeError("The track mesh must start at the origin along its forward axis")
        all_positions, all_normals, all_uvs, all_indices = [], [], [], []
        offset = 0
        for component in components:
            deformed_ue, normal_ue = deform_segment(component, local_cm, local_normal, length_cm)
            world_ue_cm = deformed_ue + np.array(component["location"])[None, :]
            all_positions.append(np.stack([world_ue_cm[:, 0], world_ue_cm[:, 2], world_ue_cm[:, 1]], axis=1) * 0.01)
            all_normals.append(np.stack([normal_ue[:, 0], normal_ue[:, 2], normal_ue[:, 1]], axis=1))
            all_uvs.append(uvs)
            all_indices.append(indices + offset)
            offset += len(local_cm)
        positions = np.concatenate(all_positions)
        builder.primitive(positions, np.concatenate(all_normals), np.concatenate(all_uvs), np.concatenate(all_indices), material_id)
        totals.append({"vertices": len(positions), "triangles": sum(len(i) for i in all_indices) // 3})
    builder.write(output_path, mesh_name)
    return {"primitives": totals, "segments": len(components), "bytes": os.path.getsize(output_path)}


SCENE_NAMESPACE = uuid.UUID("5f2d1c9a-6d0e-5a53-9c3e-0d0f5f6f1a11")
# UE's physical materials of the level: the Landscape's Ground layer is /Game/Vehicles/PhysicsMaterials/Slippery (friction 1, restitution 0.4)
# and the track's spline meshes are NonSlippery (friction 1, restitution 0); both multiply friction (probe_vehicle_surfaces.py).
GROUND_MATERIAL = {"Friction": 1, "Restitution": 0.4}
TRACK_MATERIAL = {"Friction": 1, "Restitution": 0}


def ground_node_text(name, asset, material):
    """One scene node in the text layout the scene tools write (so the rest of the file stays byte-for-byte as it was)."""
    identifier = uuid.uuid5(SCENE_NAMESPACE, name)
    lines = [
        "      {",
        f'        "id": "{identifier}",',
        f'        "name": "{name}",',
        '        "components": [',
        "          {",
        '            "type": "CSharpGameEngine.Entities.Transform",',
        '            "members": {',
        '              "Position": { "R": 0, "U": 0, "F": 0 },',
        '              "Rotation": { "EulerRuf": { "R": 0, "U": 0, "F": 0 } },',
        '              "Scale": { "R": 1, "U": 1, "F": 1 }',
        "            }",
        "          },",
        "          {",
        '            "type": "CSharpGameEngine.Runtime.Windows.StaticMesh",',
        '            "members": {',
        f'              "AssetPath": "{asset}",',
        '              "Mobility": "Static"',
        "            }",
        "          }" + ("," if material is not None else ""),
    ]
    if material is not None:
        lines += [
            "          {",
            '            "type": "CSharpGameEngine.Runtime.Physics.StaticMeshCollider",',
            f'            "members": {{ "Material": {{ "Friction": {material["Friction"]}, "Restitution": {material["Restitution"]} }} }}',
            "          }",
        ]
    return "\n".join(lines + ["        ],", '        "children": []', "      },", ""])


def update_scene(template_dir):
    path = os.path.join(template_dir, "Scenes", "Lvl_VehicleBasic.json")
    with open(path, encoding="utf-8", newline="") as handle:
        text = handle.read()
    newline = "\r\n" if "\r\n" in text else "\n"
    text = text.replace("\r\n", "\n")
    pattern = re.compile(r'      \{\n        "id": "[^"]+",\n        "name": "(?:Ground|Landscape|Track|TrackCurbs)",\n.*?\n      \},\n', re.DOTALL)
    first = pattern.search(text)
    if first is None:
        raise RuntimeError("The scene has no Ground, Landscape or Track node to replace")
    prefix = "Assets/Stage/Lvl_VehicleBasic/Meshes/"
    replacement = (ground_node_text("Landscape", prefix + "Landscape_Terrain.glb", GROUND_MATERIAL)
                   + ground_node_text("Track", prefix + "Track_Road.glb", TRACK_MATERIAL)
                   + ground_node_text("TrackCurbs", prefix + "Track_Curbs.glb", None))
    text = text[:first.start()] + replacement + text[first.end():]
    text = pattern.sub("", text.replace(replacement, "@@GROUND@@", 1)).replace("@@GROUND@@", replacement, 1)
    with open(path, "w", encoding="utf-8", newline="") as handle:
        handle.write(text.replace("\n", newline))


def main():
    heights_path, track_path, ground_path, template_dir = sys.argv[1:5]
    data = json.load(open(heights_path, encoding="utf-8"))
    grid = data["grid"]
    if grid["stepCm"] != 100.0 or grid["misses"] != 0:
        raise RuntimeError("Expected a 1 m grid with no missed trace")
    heights = np.array(grid["heights"], dtype=np.float64).reshape(grid["ny"], grid["nx"])
    ground = Image.open(ground_path).convert("RGB")
    mesh_dir = os.path.join(template_dir, "Assets", "Stage", "Lvl_VehicleBasic", "Meshes")
    terrain = build_terrain(heights, ground, os.path.join(mesh_dir, "Landscape_Terrain.glb"))
    print("terrain", terrain)
    road = build_track_ring(data["splineMeshes"], track_path, os.path.join(mesh_dir, "Track_Road.glb"), "Track_Road", 1)
    print("road", road)
    curbs = build_track_ring(data["splineMeshes"], track_path, os.path.join(mesh_dir, "Track_Curbs.glb"), "Track_Curbs", 0)
    print("curbs", curbs)
    old = os.path.join(mesh_dir, "Track_Ring.glb")
    if os.path.exists(old):
        os.remove(old)
    update_scene(template_dir)


if __name__ == "__main__":
    main()
