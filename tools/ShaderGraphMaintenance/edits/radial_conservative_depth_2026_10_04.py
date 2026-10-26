"""M0099 follow-up (ADR-0076): FrameCB ends with the radial main pass's conservative raster view-projection.

Its depth row is the hyperbola a - b/w that never exceeds the encoded radial depth, so the radial pixel entries can
declare SV_DepthGreaterEqual and the hardware keeps rejecting hidden fragments early. Only the conservative-depth vertex entry reads it.
"""


def replace_once(source, old, new):
    if source.count(old) != 1:
        raise ValueError(f"Expected exactly one occurrence of: {old[:80]}")
    return source.replace(old, new)


def edit(source):
    source = source.replace("\r\n", "\n")
    source = replace_once(source, """    float4 DepthMapping0;
    float4 DepthMapping1;
}""", """    float4 DepthMapping0;
    float4 DepthMapping1;
    row_major float4x4 RadialRasterViewProjection;
}""")
    return source
