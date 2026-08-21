using CSharpGameEngine.SceneAnalysis.Tests;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class TempTransformProbeTests {
    [Fact]
    public void Probe(){
        var Compilation=SceneAnalysisTestHelper.CreateCompilation("""
            namespace Sample;
            public struct Transform {
                public float PositionX;
                public float PositionY;
                public float PositionZ;
                public float RotationX;
                public float RotationY;
                public float RotationZ;
                public float RotationW;
                public float ScaleX;
                public float ScaleY;
                public float ScaleZ;
                public Transform(){
                    this.RotationW=1f;
                    this.ScaleX=1f;
                    this.ScaleY=1f;
                    this.ScaleZ=1f;
                }
            }
            """);
        var Type=Compilation.GetTypeByMetadataName("Sample.Transform");
        var Expression=SceneComponentInitializerPolicy.CreateExpression(
            Type!,Type!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        Assert.Fail(Expression);
    }
}
