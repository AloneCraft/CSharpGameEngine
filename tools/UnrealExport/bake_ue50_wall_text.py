"""Bake the ThirdPersonMap wall label to a static mesh for texture-free previews."""
import argparse
import json
import struct
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


def bake(font_path, output_path):
    # The UE 5.0 TextRenderActor stores this text, a 150 cm WorldSize,
    # centered vertical alignment, and RGB 109. Its placement is in the scene.
    text = 'Third Person Template'
    em_pixels = 64
    meters_per_pixel = 1.5 / em_pixels
    font = ImageFont.truetype(str(font_path), em_pixels)
    left, top, right, bottom = font.getbbox(text)
    mask = Image.new('L', (right - left, bottom - top))
    ImageDraw.Draw(mask).text((-left, -top), text, fill=255, font=font)
    pixels = mask.load()
    active = {}
    rectangles = []
    for y in range(mask.height + 1):
        runs = set()
        if y < mask.height:
            start = None
            for x in range(mask.width + 1):
                if x < mask.width and pixels[x, y] >= 128:
                    if start is None:
                        start = x
                elif start is not None:
                    runs.add((start, x))
                    start = None
        for run in sorted(active.keys() - runs):
            rectangles.append((*run, active.pop(run), y))
        for run in runs:
            active.setdefault(run, y)

    positions, indices = [], []
    for start, end, first, last in rectangles:
        # The facing wall is on -R. In glTF +Z is engine -F.
        z0, z1 = ((mask.width - x) * meters_per_pixel for x in (start, end))
        y0, y1 = ((mask.height / 2 - y) * meters_per_pixel for y in (first, last))
        vertex = len(positions)
        positions.extend([(0, y0, z0), (0, y1, z0), (0, y1, z1), (0, y0, z1)])
        indices.extend([vertex, vertex + 2, vertex + 1, vertex, vertex + 3, vertex + 2])

    binary = bytearray()
    views, accessors = [], []

    def accessor(values, code, component, kind, count, target, **bounds):
        offset = len(binary)
        binary.extend(struct.pack('<' + code * len(values), *values))
        views.append(dict(buffer=0, byteOffset=offset, byteLength=len(binary)-offset, target=target))
        accessors.append(dict(bufferView=len(views)-1, componentType=component, type=kind, count=count, **bounds))
        return len(accessors)-1

    position = accessor([v for p in positions for v in p], 'f', 5126, 'VEC3', len(positions), 34962,
                        min=[min(p[a] for p in positions) for a in range(3)],
                        max=[max(p[a] for p in positions) for a in range(3)])
    normal = accessor([-1, 0, 0] * len(positions), 'f', 5126, 'VEC3', len(positions), 34962)
    index = accessor(indices, 'I', 5125, 'SCALAR', len(indices), 34963)
    tint = 109 / 255
    model = {
        'asset': {'version': '2.0', 'generator': 'CGE UE 5.0 wall label bake'},
        'scene': 0, 'scenes': [{'nodes': [0]}], 'nodes': [{'mesh': 0}],
        'meshes': [{'name': text, 'primitives': [{'attributes': {'POSITION': position, 'NORMAL': normal},
                    'indices': index, 'material': 0}]}],
        'materials': [{'name': 'Wall lettering', 'pbrMetallicRoughness': {
            'baseColorFactor': [tint, tint, tint, 1], 'metallicFactor': 0, 'roughnessFactor': 1}}],
        'buffers': [{'byteLength': len(binary)}], 'bufferViews': views, 'accessors': accessors,
    }
    encoded = json.dumps(model, separators=(',', ':')).encode()
    encoded += b' ' * (-len(encoded) % 4)
    output_path.write_bytes(
        struct.pack('<4sII', b'glTF', 2, 28 + len(encoded) + len(binary))
        + struct.pack('<I4s', len(encoded), b'JSON') + encoded
        + struct.pack('<I4s', len(binary), b'BIN\0') + binary)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('font', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    bake(args.font, args.output)
