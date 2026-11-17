"""Names the unnamed nodes and textures of binary glTF files, so the engine's importer does not have to assign structural names (IMP150).

UE's glTF exporter writes the node that holds a static or skeletal mesh without a name, and scripts that add a texture may leave it unnamed.
A node takes the name of its mesh (the stage exports already name that node after the mesh) and a texture takes the name of its image.
Only the JSON chunk changes; the binary chunk is copied as it is. A file without unnamed nodes or textures is left untouched.

Usage: python name_gltf_nodes.py <file.glb>...
The exporters call name_glb() on each file they write (export_skeletal_mesh.py, export_vehicle_car.py).
"""
import json
import struct
import sys
from pathlib import Path


def name_glb(path) -> list[str]:
    path = Path(path)
    data = path.read_bytes()
    magic, version, length = struct.unpack_from('<4sII', data, 0)
    json_size, json_kind = struct.unpack_from('<I4s', data, 12)
    if magic != b'glTF' or version != 2 or length != len(data) or json_kind != b'JSON':
        raise ValueError(f'Not a binary glTF 2.0 file: {path}')
    document = json.loads(data[20:20 + json_size])
    rest = data[20 + json_size:]
    changes = []
    meshes = document.get('meshes', [])
    for index, node in enumerate(document.get('nodes', [])):
        if not node.get('name') and 'mesh' in node and meshes[node['mesh']].get('name'):
            node['name'] = meshes[node['mesh']]['name']
            changes.append(f'node[{index}] = {node["name"]}')
    images = document.get('images', [])
    for index, texture in enumerate(document.get('textures', [])):
        if not texture.get('name') and 'source' in texture and images[texture['source']].get('name'):
            texture['name'] = images[texture['source']]['name']
            changes.append(f'texture[{index}] = {texture["name"]}')
    if changes:
        encoded = json.dumps(document, separators=(',', ':'), ensure_ascii=False).encode('utf-8')
        encoded += b' ' * (-len(encoded) % 4)
        header = struct.pack('<4sII', b'glTF', 2, 12 + 8 + len(encoded) + len(rest)) + struct.pack('<I4s', len(encoded), b'JSON')
        path.write_bytes(header + encoded + rest)
    return changes


if __name__ == '__main__':
    for argument in sys.argv[1:]:
        for change in name_glb(argument):
            print(f'{argument}: {change}')
