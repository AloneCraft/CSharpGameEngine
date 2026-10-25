"""ADR-0084/M0119: Stage VSMain and PSMain own view depth; legacy entries become temporary aliases."""

def replace_once(source,old,new):
    assert source.count(old)==1,(source.count(old),old[:90])
    return source.replace(old,new)

OUTPUT = """struct StagePixelOutput
{
    float4 Color : SV_Target;
    float Depth : SV_DepthGreaterEqual;
};
StagePixelOutput CompleteStageSurface(VertexOutput input, float4 color)
{
    StagePixelOutput output;
    output.Color = color;
    float3 depthPosition = DepthMapping0.x == 2.0 ? input.DepthPosition : input.WorldPosition - CameraPositionWS.xyz;
    output.Depth = DepthMapping0.x == 0.0 ? input.Position.z : EncodeRadialDepth(depthPosition);
    return output;
}
"""

def edit(source):
    source=source.replace("\r\n","\n")
    source=replace_once(source,"struct VertexOutput\n{\n    float4 Position : SV_Position;","struct VertexOutput\n{\n    noperspective centroid float4 Position : SV_Position;")
    source=replace_once(source,"    float4 ShadowPosition : TEXCOORD2;","    float4 ShadowPosition : TEXCOORD2;\n    float3 DepthPosition : TEXCOORD3;\n    float2 ClipDistance : SV_ClipDistance0;")
    source=replace_once(source,"    output.Position = mul(worldPosition, ViewProjection);","""    float4 clip = mul(worldPosition, ViewProjection);
    bool logarithmic = DepthMapping0.x == 1.0;
    float rasterDepth = logarithmic ? DepthMapping1.w * (RadialRasterDepth.x * clip.w - RadialRasterDepth.y) : clip.z;
    output.Position = float4(clip.xy, rasterDepth, clip.w);
    output.DepthPosition = worldPosition.xyz - CameraPositionWS.xyz;
    output.ClipDistance = logarithmic ? float2(clip.z, clip.w - clip.z) : float2(1.0, 1.0);""")
    source=replace_once(source,"float4 PSMain(VertexOutput input) : SV_Target\n{",OUTPUT+"""StagePixelOutput PSMain(VertexOutput input)
{
    if (DepthMapping0.x == 1.0 && IsBeyondRadialFarPlane(input.WorldPosition))
    {
        discard;
    }""")
    return replace_once(source,"    return float4(color, 1.0);","    return CompleteStageSurface(input, float4(color, 1.0));")
