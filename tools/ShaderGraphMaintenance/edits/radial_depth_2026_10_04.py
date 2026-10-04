"""M0099 Phase 3 (ADR-0076): FrameCB ends with the view's depth-mapping packet.

DepthMapping0 = (mode: 0 standard / 1 radial / 2 radial observer, transition metres T, 1 / G(rMax), FarZ) and
DepthMapping1 = (observer centre in render space, reserved). The standard entries never read them, so their output
is unchanged; the radial entries live in StagePresentationShaderSource.
"""


def replace_once(source, old, new):
    if source.count(old) != 1:
        raise ValueError(f"Expected exactly one occurrence of: {old[:80]}")
    return source.replace(old, new)


def edit(source):
    source = source.replace("\r\n", "\n")
    source = replace_once(source, """    float4 AmbientOcclusionParams;
}""", """    float4 AmbientOcclusionParams;
    float4 DepthMapping0;
    float4 DepthMapping1;
}""")
    return source
