using CSharpGameEngine.Rendering;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Rendering.Server;
using CSharpGameEngine.Runtime.Windows;
namespace CSharpGameEngine.ShaderArtifacts.Tests;
public sealed class ShaderArtifactRuntimeBoundaryTests {
    [Fact]
    public void WindowsRuntimeDoesNotContainHlslCompiler() {
        var Assembly=typeof(StageRenderer).Assembly;
        Assert.Null(Assembly.GetType("CSharpGameEngine.Compiler.Hlsl.HlslCompiler",false));
    }
    [Fact]
    public void RenderServerAssemblyUsesOnlyPortableRuntimeDependencies(){
        var Assembly=typeof(RenderServer).Assembly;
        Assert.Equal(typeof(RenderView).Assembly,Assembly);
        Assert.DoesNotContain(Assembly.GetReferencedAssemblies(),Reference=>Reference.Name is "CSharpGameEngine.Windows" or "TerraFX.Interop.Windows");
        Assert.DoesNotContain(typeof(RenderServer).GetMethods().SelectMany(Method=>Method.GetParameters()),Parameter=>Parameter.ParameterType==typeof(GraphicsDeviceOptions)||Parameter.ParameterType==typeof(nint));
    }
    [Fact]
    public void HotReloadIsCompiledOnlyInDebug() {
#if DEBUG
        Assert.True(RenderServer.ShaderHotReloadEnabled);
#else
        Assert.False(RenderServer.ShaderHotReloadEnabled);
#endif
    }    [Fact]
    public void WindowsRuntimeDoesNotReferenceDxcAssembly() {
        var References=typeof(StageRenderer).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(References,Reference=>Reference.Name?.Contains("DXC",StringComparison.OrdinalIgnoreCase)==true);
    }
}
