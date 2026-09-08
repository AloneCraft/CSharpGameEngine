using CSharpGameEngine.Runtime.Windows;
namespace CSharpGameEngine.ShaderArtifacts.Tests;
public sealed class ShaderArtifactRuntimeBoundaryTests {
    [Fact]
    public void WindowsRuntimeDoesNotContainHlslCompiler() {
        var Assembly=typeof(RenderServer).Assembly;
        Assert.Null(Assembly.GetType("CSharpGameEngine.Compiler.Hlsl.HlslCompiler",false));
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
        var References=typeof(RenderServer).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(References,Reference=>Reference.Name?.Contains("DXC",StringComparison.OrdinalIgnoreCase)==true);
    }
}
