using CSharpGameEngine.Graphics.D3D12;
namespace CSharpGameEngine.ShaderArtifacts.Tests;
public sealed class ShaderArtifactRuntimeBoundaryTests {
    [Fact]
    public void WindowsRuntimeDoesNotContainHlslCompiler() {
        var Assembly=typeof(TriangleRenderer).Assembly;
        Assert.Null(Assembly.GetType("CSharpGameEngine.Compiler.Hlsl.HlslCompiler",false));
    }
    [Fact]
    public void HotReloadIsCompiledOnlyInDebug() {
#if DEBUG
        Assert.True(TriangleRenderer.ShaderHotReloadEnabled);
#else
        Assert.False(TriangleRenderer.ShaderHotReloadEnabled);
#endif
    }    [Fact]
    public void WindowsRuntimeDoesNotReferenceDxcAssembly() {
        var References=typeof(TriangleRenderer).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(References,Reference=>Reference.Name?.Contains("DXC",StringComparison.OrdinalIgnoreCase)==true);
    }
}