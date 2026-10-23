"""Adds an emissive texture to one material of a binary glTF (M0109: the sports car's brake lights).

Usage: python -I add_brake_light_emission.py <model.glb> <emissive.png> <material index> <emissive factor>

UE's sports car body material (M_SportsCarBase) has no usable emissive input (its EmissivePower multiplies a named reroute whose declaration is
missing), and the brake light parameter of BP_VehicleAdvSportsCar goes to the hidden skeletal mesh's WorldGridMaterial (probe_vehicle_brake_lights.py).
The engine draws the tail lights from the red channel of UE's T_SportsCar_Lights_M (exported by probe_vehicle_emissive.py), which lies on the tail
light assembly in the chassis UV set 0. The texture goes into the glb's binary chunk as a PNG image; the material's emissiveFactor is the
brightness at the template's resting BrakeLightPower (MeshEmissive scales it at run time). Run it once on the glb that export_vehicle_car.py
wrote; a second run would add a second image.
"""
import json
import struct
import sys


def read_glb(path):
    data = open(path, "rb").read()
    magic, version, length = struct.unpack_from("<III", data, 0)
    if magic != 0x46546C67 or version != 2:
        raise SystemExit(f"{path} is not a binary glTF 2.0 file")
    offset = 12
    document = None
    binary = b""
    while offset < length:
        chunk_length, chunk_type = struct.unpack_from("<II", data, offset)
        chunk = data[offset + 8: offset + 8 + chunk_length]
        if chunk_type == 0x4E4F534A:
            document = json.loads(chunk.decode("utf-8"))
        elif chunk_type == 0x004E4942:
            binary = chunk
        offset += 8 + chunk_length
    return document, bytearray(binary)


def write_glb(path, document, binary):
    text = json.dumps(document, separators=(",", ":")).encode("utf-8")
    text += b" " * ((4 - len(text) % 4) % 4)
    binary += b"\0" * ((4 - len(binary) % 4) % 4)
    total = 12 + 8 + len(text) + 8 + len(binary)
    with open(path, "wb") as handle:
        handle.write(struct.pack("<III", 0x46546C67, 2, total))
        handle.write(struct.pack("<II", len(text), 0x4E4F534A))
        handle.write(text)
        handle.write(struct.pack("<II", len(binary), 0x004E4942))
        handle.write(binary)


def main():
    if len(sys.argv) != 5:
        raise SystemExit(__doc__)
    model, image_path, material_index, factor = sys.argv[1], sys.argv[2], int(sys.argv[3]), float(sys.argv[4])
    document, binary = read_glb(model)
    material = document["materials"][material_index]
    image_bytes = open(image_path, "rb").read()
    binary += b"\0" * ((4 - len(binary) % 4) % 4)
    view = {"buffer": 0, "byteOffset": len(binary), "byteLength": len(image_bytes)}
    binary += image_bytes
    document.setdefault("bufferViews", []).append(view)
    document.setdefault("images", []).append({"name": "T_SportsCar_BrakeLights_E", "mimeType": "image/png", "bufferView": len(document["bufferViews"]) - 1})
    texture = {"source": len(document["images"]) - 1}
    if document.get("samplers"):
        texture["sampler"] = 0
    document.setdefault("textures", []).append(texture)
    material["emissiveTexture"] = {"index": len(document["textures"]) - 1, "texCoord": 0}
    material["emissiveFactor"] = [factor, factor, factor]
    document["buffers"][0]["byteLength"] = len(binary) + ((4 - len(binary) % 4) % 4)
    write_glb(model, document, binary)
    print(f"{model}: material {material_index} ({material.get('name')}) emissive texture {texture['source']} factor {factor}")


if __name__ == "__main__":
    main()
