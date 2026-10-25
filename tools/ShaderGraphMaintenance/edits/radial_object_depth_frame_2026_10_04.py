"""M0100 (ADR-0076): FrameCB no longer carries the frame-wide conservative raster view-projection.

Each object's conservative radial raster depth is ObjectCB.RadialRasterDepth (radial_object_depth_2026_10_04.py) and
The conservative-depth vertex entry replaces only the depth of the standard clip position, so FrameCB ends with the depth-mapping packet again.
"""


def replace_once(source, old, new):
    if source.count(old) != 1:
        raise ValueError(f"Expected exactly one occurrence of: {old[:80]}")
    return source.replace(old, new)


def edit(source):
    source = source.replace("\r\n", "\n")
    source = replace_once(source, """    float4 DepthMapping0;
    float4 DepthMapping1;
    row_major float4x4 RadialRasterViewProjection;
}""", """    float4 DepthMapping0;
    float4 DepthMapping1;
}""")
    return source
