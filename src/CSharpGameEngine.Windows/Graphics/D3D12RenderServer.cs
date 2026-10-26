using System.Runtime.CompilerServices;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Rendering.Backend;
using CSharpGameEngine.Rendering.Server;
namespace CSharpGameEngine.Graphics.D3D12;
/// <summary>ネイティブ描画の構成ルート。返すサーバーが移植可能な状態と回復処理を所有する。</summary>
public static class D3D12RenderServer {
    private static readonly ConditionalWeakTable<RenderServer,D3D12StageBackendFactory>Factories=new();
    public static RenderServer Create(nint Hwnd,int Width,int Height,GraphicsDeviceOptions DeviceOptions,string ShaderArtifactPath,
        IReadOnlyList<StageMeshCreateInfo>Meshes,IReadOnlyList<StageMaterialCreateInfo>Materials,IReadOnlyList<StageObjectCreateInfo>Objects){
        ArgumentNullException.ThrowIfNull(DeviceOptions);
        ArgumentNullException.ThrowIfNull(ShaderArtifactPath);
        var Factory=new D3D12StageBackendFactory(Hwnd,DeviceOptions,ShaderArtifactPath);
        var Server=RenderServer.Create(Factory,Width,Height,Meshes,Materials,Objects);
        Factories.Add(Server,Factory);
        return Server;
    }
    internal static StageRenderer BackendForTesting(RenderServer Server){
        ArgumentNullException.ThrowIfNull(Server);
        if(!Factories.TryGetValue(Server,out var Factory)||Factory.Current is null){
            throw new InvalidOperationException("The server was not composed with this provider.");
        }
        return Factory.Current;
    }
    private sealed class D3D12StageBackendFactory:IStageRenderBackendFactory {
        private readonly nint Hwnd;
        private readonly GraphicsDeviceOptions DeviceOptions;
        private readonly string ShaderArtifactPath;
        public StageRenderer?Current { get;private set; }
        public D3D12StageBackendFactory(nint Hwnd,GraphicsDeviceOptions DeviceOptions,string ShaderArtifactPath){
            this.Hwnd=Hwnd;
            this.DeviceOptions=new GraphicsDeviceOptions {
                UseWarp=DeviceOptions.UseWarp,EnableDebugLayer=DeviceOptions.EnableDebugLayer,EnableGpuBasedValidation=DeviceOptions.EnableGpuBasedValidation,
            };
            this.ShaderArtifactPath=Path.GetFullPath(ShaderArtifactPath);
        }
        public IStageRenderBackend Create(int Width,int Height,IReadOnlyList<StageMeshCreateInfo>Meshes,
            IReadOnlyList<StageMaterialCreateInfo>Materials,IReadOnlyList<StageObjectCreateInfo>Objects,IStageShaderArtifact?LastKnownGood=null){
            var Backend=LastKnownGood is null
                ?StageRenderer.Create(this.Hwnd,Width,Height,this.DeviceOptions,this.ShaderArtifactPath,Meshes,Materials,Objects)
                :StageRenderer.CreateFromLastKnownGood(this.Hwnd,Width,Height,this.DeviceOptions,this.ShaderArtifactPath,
                    LastKnownGood as ShaderArtifactPackage??throw new ArgumentException("This provider cannot rebuild another provider's shader artifact.",nameof(LastKnownGood)),Meshes,Materials,Objects);
            this.Current=Backend;
            return Backend;
        }
    }
}
/// <summary>ネイティブ専用の実行テスト。製品用 L4 ポートには、障害注入や深度読み戻し用のテスト API はない。</summary>
internal static class D3D12RenderServerTestAccess {
    internal static void RemoveDeviceForTesting(this RenderServer Server)=>D3D12RenderServer.BackendForTesting(Server).RemoveDeviceForTesting();
    internal static float[]ReadDepthForTesting(this RenderServer Server)=>D3D12RenderServer.BackendForTesting(Server).ReadDepthForTesting();
}
