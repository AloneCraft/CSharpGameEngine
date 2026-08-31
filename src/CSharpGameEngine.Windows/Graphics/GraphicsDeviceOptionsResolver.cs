namespace CSharpGameEngine.Graphics.D3D12;
public static class GraphicsDeviceOptionsResolver {
    public const string AdapterEnvironmentVariable="CGE_GRAPHICS_ADAPTER";
    public const string DebugLayerEnvironmentVariable="CGE_D3D12_DEBUG_LAYER";
    public static GraphicsDeviceOptions FromEnvironment(){
        var Adapter=(Environment.GetEnvironmentVariable(AdapterEnvironmentVariable)??"default").Trim().ToLowerInvariant();
        var UseWarp=Adapter switch { "default" or "hardware"=>false,"warp"=>true,_=>throw new InvalidOperationException($"{AdapterEnvironmentVariable} must be default, hardware, or warp.") };
        var DebugText=(Environment.GetEnvironmentVariable(DebugLayerEnvironmentVariable)??"0").Trim();
        var EnableDebug=DebugText switch { "0"=>false,"1"=>true,_=>throw new InvalidOperationException($"{DebugLayerEnvironmentVariable} must be 0 or 1.") };
        return new GraphicsDeviceOptions { UseWarp=UseWarp,EnableDebugLayer=EnableDebug };
    }
}
