"""ADR-0084: native depth encoding ABI and unused frame data removal."""
import re

def edit(source):
    source=source.replace('\r\n','\n')
    old='    row_major float4x4 ShadowViewProjection;\n    float4 DirectionalShadowAtlasUvScaleOffset;'
    assert old in source
    source=source.replace(old,'    row_major float4x4 InverseViewProjection;\n    float4 ViewportSize;')
    source=source.replace('    float4 ShadowPosition : TEXCOORD2;\n','').replace('    output.ShadowPosition = mul(worldPosition, ShadowViewProjection);\n','')
    source,count=re.subn(r'float ComputeDirectionalShadow\(.*?\n\}\n','',source,flags=re.S)
    assert count==1
    source=source.replace('DepthMapping','DepthEncoding').replace('RadialRasterDepth','ConservativeRasterDepth')
    old='output.ClipDistance = logarithmic ? float2((clip.z), (clip.w - clip.z)) : float2(1.0, 1.0);'
    if old not in source:
        raise RuntimeError(next(line for line in source.splitlines() if 'output.ClipDistance' in line))
    return source.replace(old,'output.ClipDistance = float2((clip.z), (clip.w - clip.z));')
