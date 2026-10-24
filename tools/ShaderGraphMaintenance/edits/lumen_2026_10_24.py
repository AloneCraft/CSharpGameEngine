"""ADR-0078/M0111: the Stage program reads the Lumen-like diffuse irradiance (t16) and reflections (t17) that StageLumen computes before the main
pass. FrameCB.LumenParams.x enables the diffuse, .y the reflections; with both zero (the probe volume tier, previews and probe captures) every
pixel takes the path of M0110 and earlier unchanged."""


def replace_once(source, old, new):
    if source.count(old) != 1:
        raise ValueError(f"Expected exactly one occurrence of: {old[:80]}")
    return source.replace(old, new)


LUMEN_FUNCTIONS = """float4 SampleLumenDiffuse(float4 pixelPosition, float3 worldPosition)
{
    float4 lumen = LumenDiffuseTexture.Load(int3(int2(pixelPosition.xy), 0));
    float viewDistance = length(worldPosition - CameraPositionWS.xyz);
    if (abs(lumen.a - viewDistance) > 0.03 * viewDistance + 0.05)
    {
        return float4(0.0, 0.0, 0.0, 0.0);
    }
    return float4(lumen.rgb, 1.0);
}
"""


def edit(source):
    source = source.replace("\r\n", "\n")
    source = replace_once(source, """    float4 DepthMapping0;
    float4 DepthMapping1;
}""", """    float4 DepthMapping0;
    float4 DepthMapping1;
    float4 LumenParams;
}""")
    source = replace_once(source, """StructuredBuffer<float4> ExposureState : register(t15);
""", """StructuredBuffer<float4> ExposureState : register(t15);
Texture2D LumenDiffuseTexture : register(t16);
Texture2D LumenReflectionTexture : register(t17);
""")
    source = replace_once(source, """float4 PSMain(VertexOutput input) : SV_Target
{""", LUMEN_FUNCTIONS + """float4 PSMain(VertexOutput input) : SV_Target
{""")
    source = replace_once(source, """        globalIllumination = SampleGlobalIllumination((input.WorldPosition), n, v);
    }
    if (SkyLightParams.x > 0.5 || globalIllumination.a > 0.0)
    {""", """        globalIllumination = SampleGlobalIllumination((input.WorldPosition), n, v);
    }
    float4 lumenDiffuse = float4(0.0, 0.0, 0.0, 0.0);
    if (LumenParams.x > 0.5)
    {
        lumenDiffuse = SampleLumenDiffuse(input.Position, input.WorldPosition);
    }
    if (SkyLightParams.x > 0.5 || globalIllumination.a > 0.0 || lumenDiffuse.a > 0.0)
    {""")
    source = replace_once(source, """            diffuseIrradiance = lerp(diffuseIrradiance, (globalIllumination.rgb), (globalIllumination.a));
        }
        diffuseIrradiance *= ambientOcclusion;""", """            diffuseIrradiance = lerp(diffuseIrradiance, (globalIllumination.rgb), (globalIllumination.a));
        }
        if (lumenDiffuse.a > 0.0)
        {
            diffuseIrradiance = lumenDiffuse.rgb;
            if (LumenParams.y > 0.5)
            {
                reflectedIrradiance = LumenReflectionTexture.Load(int3(int2(input.Position.xy), 0)).rgb * PI;
            }
        }
        diffuseIrradiance *= ambientOcclusion;""")
    return source
