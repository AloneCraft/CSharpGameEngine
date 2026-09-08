namespace CSharpGameEngine.CodeAnalysis.Tests;

public sealed class SnapshotGeneratorTests {
    private const string Systems="""
        using CSharpGameEngine.Entities;
        namespace Sample;

        public struct RuntimeState:IRuntimeComponent {
            public int DerivedValue;
        }

        internal struct SnapshotSystem:ISystem {
            public bool Update(Entity Entity,ref Position Position,ref RuntimeState RuntimeState,ref State State)=>true;
        }
        """;

    [Fact]
    public void EmitsOneSchemaAndTheCompleteAtomicSnapshotBoundary(){
        var Result=GameSystemGeneratorTestHelper.RunGenerator(GameSystemGeneratorTestHelper.CommonComponents,Systems);

        string Text=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Snapshot.g.cs");

        Assert.Contains("public static string SnapshotSchemaFingerprint",Text,StringComparison.Ordinal);
        Assert.Contains("public byte[] CaptureSnapshot",Text,StringComparison.Ordinal);
        Assert.Contains("public byte[] UpdateSnapshot",Text,StringComparison.Ordinal);
        Assert.Contains("public global::CSharpGameEngine.Entities.EcsSnapshotRestoreResult RestoreSnapshot",Text,StringComparison.Ordinal);
        Assert.Contains("public global::CSharpGameEngine.Entities.EcsSnapshotRestoreResult ApplyPendingSnapshot",Text,StringComparison.Ordinal);
        Assert.Contains("private bool ValidateSnapshotEntry",Text,StringComparison.Ordinal);
        Assert.Contains("private void ApplySnapshotEntry",Text,StringComparison.Ordinal);
        Assert.Contains("this.Commands.Count != 0 || this.Commands.IsSystemExecuting",Text,StringComparison.Ordinal);
    }

    [Fact]
    public void SchemaDescribesRuntimeComponentsButCaptureExcludesTheirPayload(){
        var Result=GameSystemGeneratorTestHelper.RunGenerator(GameSystemGeneratorTestHelper.CommonComponents,Systems);

        string Text=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Snapshot.g.cs");

        Assert.Contains("EcsSnapshotComponentCodec<global::Sample.Position>.Descriptor",Text,StringComparison.Ordinal);
        Assert.Contains("EcsSnapshotComponentCodec<global::Sample.RuntimeState>.Descriptor",Text,StringComparison.Ordinal);
        Assert.Contains("if (!global::CSharpGameEngine.Entities.EcsSnapshotComponentCodec<global::Sample.RuntimeState>.IsTransient)",Text,StringComparison.Ordinal);
        Assert.DoesNotContain("\\n",Text,StringComparison.Ordinal);
    }
}
