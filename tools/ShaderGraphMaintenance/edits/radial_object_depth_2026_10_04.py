"""M0100 (ADR-0076): each object carries the tangent of its own conservative radial raster depth.

The radial main pass rasterizes a - b/w, a hyperbola that never exceeds the encoded radial depth. One frame-wide tangent
(at 8 m) let the hardware reject hidden fragments early only near that distance: 24 layers between 100 m and 1 km drawn
front to back took 1.74 ms against 0.10 ms standard on an RTX 4060 Ti. ObjectCB.RadialRasterDepth = (a, b) is the
tangent at the object's own view depth, which is a lower bound everywhere just the same. Only the conservative-depth vertex entry reads it;
radial_object_depth_frame_2026_10_04.py then drops the frame-wide matrix.
"""


def replace_once(source, old, new):
    if source.count(old) != 1:
        raise ValueError(f"Expected exactly one occurrence of: {old[:80]}")
    return source.replace(old, new)


def edit(source):
    source = source.replace("\r\n", "\n")
    source = replace_once(source, """    float4 WorldSubGridColor;
    float4 WorldGridAnchor;
}""", """    float4 WorldSubGridColor;
    float4 WorldGridAnchor;
    float4 RadialRasterDepth;
}""")
    return source
