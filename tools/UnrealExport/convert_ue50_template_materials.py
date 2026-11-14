"""Convert the UE 5.0 ThirdPerson grid materials for the engine's static preview.

The engine anchors each grid to its actor's origin. The UE 5.0 map uses quarter
turns for its grid meshes, so its cubic grid shares the world axes.
TopDark's top faces are assigned separately.
The script reads the measured parameters from the export manifest.
"""
import copy
import json
import pathlib
import struct
import sys


def convert(path, parameters, top_dark):
    data = path.read_bytes()
    magic, version, total = struct.unpack_from('<4sII', data)
    if magic != b'glTF' or version != 2 or total != len(data):
        raise ValueError(f'Invalid GLB: {path}')
    json_size, json_kind = struct.unpack_from('<I4s', data, 12)
    if json_kind != b'JSON':
        raise ValueError(f'Missing JSON chunk: {path}')
    document = json.loads(data[20:20 + json_size])
    bin_offset = 20 + json_size
    bin_size, bin_kind = struct.unpack_from('<I4s', data, bin_offset)
    if bin_kind != b'BIN\0' or bin_offset + 8 + bin_size != len(data):
        raise ValueError(f'Unexpected GLB chunks: {path}')
    binary = bytearray(data[bin_offset + 8:])
    primitive = document['meshes'][0]['primitives'][0]
    if len(document['meshes']) != 1 or len(document['meshes'][0]['primitives']) != 1:
        raise ValueError(f'Expected one primitive: {path}')

    def material(color):
        return {'name': 'CGE_UE50_grid', 'pbrMetallicRoughness': {
            'baseColorFactor': [*color, 1], 'metallicFactor': 0, 'roughnessFactor': parameters['Roughness']},
            'extras': {'CGE_world_grid': {'gridSizeMeters': parameters['Grid Size'] / 100,
                'subGridDivisions': int(parameters['Sub Grid Number']), 'wireHalfWidth': 5 / 512,
                'gridColor': parameters['GridColor'], 'subGridColor': parameters['SubGridColor'],
                'anchorToObject': parameters['ObjectAlignedEnabled']}}}

    document['materials'] = [material(parameters['SurfaceColor'])]
    primitive['material'] = 0
    if top_dark:
        normals_accessor = document['accessors'][primitive['attributes']['NORMAL']]
        indices_accessor = document['accessors'][primitive['indices']]
        if normals_accessor['componentType'] != 5126 or normals_accessor['type'] != 'VEC3' or indices_accessor['type'] != 'SCALAR':
            raise ValueError(f'Unexpected mesh data: {path}')
        view = document['bufferViews'][normals_accessor['bufferView']]
        start = view.get('byteOffset', 0) + normals_accessor.get('byteOffset', 0)
        stride = view.get('byteStride', 12)
        normals = [struct.unpack_from('<fff', binary, start + i * stride) for i in range(normals_accessor['count'])]
        view = document['bufferViews'][indices_accessor['bufferView']]
        start = view.get('byteOffset', 0) + indices_accessor.get('byteOffset', 0)
        code = {5121: 'B', 5123: 'H', 5125: 'I'}[indices_accessor['componentType']]
        indices = struct.unpack_from('<' + code * indices_accessor['count'], binary, start)
        sides, top = [], []
        for i in range(0, len(indices), 3):
            triangle = indices[i:i + 3]
            (top if sum(normals[v][1] for v in triangle) / 3 >= 0.7 else sides).extend(triangle)
        if not top or not sides:
            raise ValueError(f'Expected top and side faces: {path}')
        document['materials'].append(material(parameters['TopSurfaceColor']))
        primitives = []
        for values, material_index in ((sides, 0), (top, 1)):
            while len(binary) % 4:
                binary.append(0)
            offset = len(binary)
            binary.extend(struct.pack('<' + code * len(values), *values))
            view_index = len(document['bufferViews'])
            document['bufferViews'].append({'buffer': 0, 'byteOffset': offset, 'byteLength': len(values) * struct.calcsize(code), 'target': 34963})
            accessor_index = len(document['accessors'])
            document['accessors'].append({'bufferView': view_index, 'componentType': indices_accessor['componentType'], 'count': len(values), 'type': 'SCALAR'})
            part = copy.deepcopy(primitive)
            part['indices'], part['material'] = accessor_index, material_index
            primitives.append(part)
        document['meshes'][0]['primitives'] = primitives
    document['buffers'][0]['byteLength'] = len(binary)
    while len(binary) % 4:
        binary.append(0)
    encoded = json.dumps(document, separators=(',', ':')).encode()
    encoded += b' ' * (-len(encoded) % 4)
    result = struct.pack('<4sII', b'glTF', 2, 12 + 8 + len(encoded) + 8 + len(binary))
    result += struct.pack('<I4s', len(encoded), b'JSON') + encoded
    result += struct.pack('<I4s', len(binary), b'BIN\0') + binary
    path.write_bytes(result)


def main(manifest_path, project_path):
    manifest = json.loads(pathlib.Path(manifest_path).read_text(encoding='utf-8'))
    properties = manifest['referenceMaterialProperties']
    converted = 0
    for variant in manifest['renderVariants']:
        names = variant['materials']
        if len(names) != 1:
            raise ValueError('Expected one material per variant')
        name = names[0]
        if 'MI_PrototypeGrid_Gray.' in name or 'MI_PrototypeGrid_TopDark.' in name:
            parameters = properties[name]
            convert(pathlib.Path(project_path) / variant['assetPath'], parameters, 'MI_PrototypeGrid_TopDark.' in name)
            converted += 1
    if converted != 4:
        raise ValueError(f'Expected four grid variants, got {converted}')
    print(f'Converted {converted} grid variants.')


if __name__ == '__main__':
    main(*sys.argv[1:])
