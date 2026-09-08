StructuredBuffer<float>Input:register(t0);
RWStructuredBuffer<float>Output:register(u0);

[numthreads(64,1,1)]
void CSMain(uint3 DispatchThreadId:SV_DispatchThreadID){
    uint Count;
    uint Stride;
    Input.GetDimensions(Count,Stride);
    if(DispatchThreadId.x<Count){
        Output[DispatchThreadId.x]=(Input[DispatchThreadId.x]*2.0f)+1.0f;
    }
}
