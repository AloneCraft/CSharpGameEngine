"""ADR-0084/M0119: Stage writes physical ray distance alongside HDR colour."""
def edit(source):
    source=source.replace("\r\n","\n")
    old="    float4 Color : SV_Target;\n    float Depth : SV_DepthGreaterEqual;"
    assert source.count(old)==1
    source=source.replace(old,"    float4 Color : SV_Target0;\n    float Distance : SV_Target1;\n    float Depth : SV_DepthGreaterEqual;")
    old="    output.Depth = DepthMapping0.x == 0.0 ? (input.Position).z : EncodeRadialDepth(depthPosition);"
    assert source.count(old)==1
    return source.replace(old,"    output.Distance = RadialSafeLength(depthPosition);\n"+old)
