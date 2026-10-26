using System.Reflection;
using System.Runtime.Loader;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Spatial.Abstractions;
namespace CSharpGameEngine.Cooking;
/// <summary>モジュールを読み込むのはワーカーだけ。コントラクトの識別情報は常にワーカーの既定コンテキストから取得する。</summary>
public sealed class PluginModuleLoadContext:AssemblyLoadContext {
    private readonly AssemblyDependencyResolver Resolver;
    private static readonly IReadOnlyDictionary<string,Assembly>Shared=new[]{typeof(IAssetCooker).Assembly,typeof(AssetId).Assembly,typeof(ICoordinateConvention).Assembly}
        .ToDictionary(Assembly=>Assembly.GetName().Name!,StringComparer.Ordinal);
    public PluginModuleLoadContext(string ModulePath):base(isCollectible:true){
        this.Resolver=new AssemblyDependencyResolver(Path.GetFullPath(ModulePath));
    }
    protected override Assembly?Load(AssemblyName Name){
        if(Name.Name is{ }Identity&&Shared.TryGetValue(Identity,out var Assembly)){ return Assembly; }
        return this.Resolver.ResolveAssemblyToPath(Name) is{ }Path?this.LoadFromAssemblyPath(Path):null;
    }
    protected override IntPtr LoadUnmanagedDll(string Name)=>this.Resolver.ResolveUnmanagedDllToPath(Name) is{ }Path?this.LoadUnmanagedDllFromPath(Path):IntPtr.Zero;
}
