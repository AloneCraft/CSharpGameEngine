// Minimal colored-triangle shader pair for the Windows + Direct3D 12 vertical slice.
// Compiled with DXC at Shader Model 6.0 (see CSharpGameEngine.Compiler.Hlsl).

struct VertexInput
{
    float3 Position : POSITION;
    float4 Color : COLOR;
};

struct VertexOutput
{
    float4 Position : SV_Position;
    float4 Color : COLOR;
};

VertexOutput VSMain(VertexInput input)
{
    VertexOutput output;
    output.Position = float4(input.Position, 1.0);
    output.Color = input.Color;
    return output;
}

float4 PSMain(VertexOutput input) : SV_Target
{
    return input.Color;
}
