"""M0099 Phase 0: the world-space grid and the simplified height fog follow the world, not the camera.

The runtime renders camera-relative (the camera is the render-space origin), so input.WorldPosition is not a
world position there. ObjectCB.WorldGridAnchor carries the render origin reduced modulo the material's grid
size (computed exactly in Q64.64 on the CPU) and FogParams0.z carries the render origin's altitude. Callers that
render in world coordinates leave both at zero, which keeps their output bit for bit.
"""


def replace_once(source, old, new):
    if source.count(old) != 1:
        raise ValueError(f"Expected exactly one occurrence of: {old[:80]}")
    return source.replace(old, new)


def edit(source):
    source = source.replace("\r\n", "\n")
    source = replace_once(source, """    float4 WorldGridParams;
    float4 WorldGridColor;
    float4 WorldSubGridColor;
}""", """    float4 WorldGridParams;
    float4 WorldGridColor;
    float4 WorldSubGridColor;
    float4 WorldGridAnchor;
}""")
    source = replace_once(source, """        float mainWire = WorldGridWire((input.WorldPosition), vertexNormal, (WorldGridParams.x), (WorldGridParams.z));
        float subWire = WorldGridWire((input.WorldPosition), vertexNormal, (WorldGridParams.x / WorldGridParams.y), (WorldGridParams.z));""", """        float3 gridPosition = input.WorldPosition + WorldGridAnchor.xyz;
        float mainWire = WorldGridWire(gridPosition, vertexNormal, WorldGridParams.x, WorldGridParams.z);
        float subWire = WorldGridWire(gridPosition, vertexNormal, WorldGridParams.x / WorldGridParams.y, WorldGridParams.z);""")
    source = replace_once(source, """        float heightTerm = exp((-FogParams0.y * max(((input.WorldPosition).y), 0.0)));""", """        float heightTerm = exp((-FogParams0.y * max(((input.WorldPosition).y + FogParams0.z), 0.0)));""")
    return source
