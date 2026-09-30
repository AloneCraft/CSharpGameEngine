"""ADR-0069: screen-space ambient occlusion of indirect light (StageAmbientOcclusion on the C# side)."""


def replace_once(source, old, new):
    if source.count(old) != 1:
        raise ValueError(f"Expected exactly one occurrence of: {old[:80]}")
    return source.replace(old, new)


def edit(source):
    source = source.replace("\r\n", "\n")
    source = replace_once(source, """    float4 GiAtlasSize;
}""", """    float4 GiAtlasSize;
    float4 AmbientOcclusionParams;
}""")
    source = replace_once(source, """Texture2D GiDistanceAtlas : register(t13);
""", """Texture2D GiDistanceAtlas : register(t13);
Texture2D AmbientOcclusionTexture : register(t14);
""")
    source = replace_once(source, """    float3 color = AmbientColor.xyz * baseColor + lighting;
    float4 globalIllumination = float4(0.0, 0.0, 0.0, 0.0);""", """    float ambientOcclusion = 1.0;
    if (AmbientOcclusionParams.x > 0.5)
    {
        float2 occlusionUv = clamp((input.Position).xy * AmbientOcclusionParams.zw, AmbientOcclusionParams.zw, 1.0 - AmbientOcclusionParams.zw);
        float obscurance = AmbientOcclusionTexture.SampleLevel(LinearSampler, occlusionUv, 0).r;
        ambientOcclusion = lerp(1.0, obscurance, AmbientOcclusionParams.y);
    }
    float3 color = AmbientColor.xyz * baseColor * ambientOcclusion + lighting;
    float4 globalIllumination = float4(0.0, 0.0, 0.0, 0.0);""")
    source = replace_once(source, """            diffuseIrradiance = lerp(diffuseIrradiance, (globalIllumination.rgb), (globalIllumination.a));
        }
""", """            diffuseIrradiance = lerp(diffuseIrradiance, (globalIllumination.rgb), (globalIllumination.a));
        }
        diffuseIrradiance *= ambientOcclusion;
        reflectedIrradiance *= ambientOcclusion;
""")
    return source
