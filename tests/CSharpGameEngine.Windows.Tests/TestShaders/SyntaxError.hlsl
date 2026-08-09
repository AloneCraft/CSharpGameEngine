// Intentionally malformed HLSL used by HlslCompilerTests to verify diagnostic reporting.
struct VertexOutput
{
    float4 Position : SV_Position
};

VertexOutput VSMain(float3 position : POSITION)
{
    VertexOutput output;
    output.Position = float4(position, 1.0)
    return output;
}
