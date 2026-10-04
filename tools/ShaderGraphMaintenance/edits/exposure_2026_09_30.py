"""ADR-0069: histogram auto exposure read from the GPU state (StageAutoExposure on the C# side)."""


def replace_once(source, old, new):
    if source.count(old) != 1:
        raise ValueError(f"Expected exactly one occurrence of: {old[:80]}")
    return source.replace(old, new)


def edit(source):
    source = source.replace("\r\n", "\n")
    source = replace_once(source, """Texture2D AmbientOcclusionTexture : register(t14);
""", """Texture2D AmbientOcclusionTexture : register(t14);
StructuredBuffer<float4> ExposureState : register(t15);
""")
    source = replace_once(source, """    color *= ExposureParams.x;
    return float4(color, 1.0);""", """    float exposure = ExposureParams.x;
    if (ExposureParams.z > 0.5)
    {
        exposure = ExposureState[0].y;
    }
    color *= exposure;
    return float4(color, 1.0);""")
    return source
