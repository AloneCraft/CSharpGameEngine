"""Compares the mean luminance of fixed patches of the ThirdPerson spawn view between a UE image and engine captures (M0112).

Usage: python compare_lighting_patches.py <ue-image> <engine-capture.png> [<engine-capture.png> ...]
The engine's picture is the left-right mirror of Unreal's (an existing property of the import: R = X, F = -Y, M0107), so every engine capture is flipped
first. Luminance is 0.2126 R + 0.7152 G + 0.0722 B of the 8-bit sRGB values; the table also prints the linear value (what a lighting difference is a
ratio of). The patches are at 1280 x 720: the ramps' faces, the two outer walls, a sunlit back wall and the floor near the camera. The engine capture
includes the character (it stands in the middle, outside every patch).
"""
import sys

import numpy as np
from PIL import Image

PATCHES = {
    "left ramp face": (100, 410, 230, 470),
    "right ramp face": (1060, 410, 1230, 470),
    "right wall": (1185, 345, 1275, 385),
    "left wall": (5, 345, 100, 385),
    "back wall lit": (300, 345, 500, 385),
    "floor near": (300, 560, 900, 700),
}


def luminance(array):
    return 0.2126 * array[..., 0] + 0.7152 * array[..., 1] + 0.0722 * array[..., 2]


def srgb_to_linear(value):
    value = value / 255.0
    return value / 12.92 if value <= 0.04045 else ((value + 0.055) / 1.055) ** 2.4


def load(path, flip):
    image = Image.open(path).convert("RGB").resize((1280, 720))
    if flip:
        image = image.transpose(Image.FLIP_LEFT_RIGHT)
    return np.array(image).astype(float)


def main():
    reference = load(sys.argv[1], False)
    engines = [(path, load(path, True)) for path in sys.argv[2:]]
    print("%-16s %12s" % ("patch", "reference") + "".join("%22s" % path.replace("\\", "/").split("/")[-1][:21] for path, _ in engines))
    for name, (x0, y0, x1, y1) in PATCHES.items():
        base = luminance(reference[y0:y1, x0:x1]).mean()
        cells = ""
        for _, image in engines:
            value = luminance(image[y0:y1, x0:x1]).mean()
            cells += "%12.1f (x%5.2f lin)" % (value, srgb_to_linear(value) / max(srgb_to_linear(base), 1e-6))
        print("%-16s %12.1f" % (name, base) + cells)


if __name__ == "__main__":
    main()
