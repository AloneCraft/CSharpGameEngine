using Xunit;
namespace CSharpGameEngine.CodeAnalysis.Tests;

public sealed class SnapshotGeneratorTests {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AVersionOneSnapshotIsRejectedWithoutTouchingTheWorld(bool Native){
        // ADR-0098 §5 の D：1.0 までセーブは互換を約束しない。版 1 は読まず、保存し直すよう案内する。
        var Source=(Native?"using LogicalScalar=System.Single;":"using LogicalScalar=CSharpGameEngine.Numerics.LogicalScalar;")+"""
            using CSharpGameEngine.Entities;
            namespace Sample {
                public struct FixedPosition {public LogicalScalar X;}
                internal struct FixedSystem:ISystem {public void Update(in FixedPosition Value){}}
            }
            namespace CSharpGameEngine.Entities {
                public partial struct World {
                    public static string CheckLegacy(string Base64){
                        var Cell=global::CSharpGameEngine.Contracts.WorldCellId.Parse("30000000-0000-5000-8000-000000000001");
                        var Id=global::CSharpGameEngine.Contracts.PersistentEntityId.Parse("40000000-0000-5000-8000-000000000001");
                        var World=new World();var Entity=World.AttachToCell(Cell,Id,World.Create(new Sample.FixedPosition{X=9}));
                        var Result=World.RestoreSnapshot(global::System.Convert.FromBase64String(Base64));
                        if(Result.Succeeded)return "version 1 accepted";
                        if(Result.Error.Code!=global::CSharpGameEngine.Entities.EcsSnapshotErrorCode.UnsupportedVersion)return Result.Error.Code.ToString();
                        if(!Result.Error.Message.Contains("Save the world again"))return Result.Error.Message;
                        if(!World.TryGetComponent(Entity,out Sample.FixedPosition Value)||Value.X!=(LogicalScalar)9)return "world changed";
                        return "OK";
                    }
                }
            }
            """;
        var Assembly=GameSystemGeneratorTestHelper.CompileAndLoad(Source);
        Assert.Equal("OK",GameSystemGeneratorTestHelper.Invoke(Assembly,"CSharpGameEngine.Entities.World","CheckLegacy",LegacySnapshotFixtures.FixedPosition));
    }
    [Fact]
    public void UnsupportedPersistentReferencesFailAtBuildTimeWithoutReflectionFallback(){
        const string Source="""
            using CSharpGameEngine.Entities;
            namespace Sample;
            public struct Persistent {public object Native;}
            internal struct System:ISystem {public void Update(ref Persistent Value){}}
            public static class Save {public static void Run(ref World Value){Value.CaptureSnapshot(0,new string('0',64));}}
            """;
        var Result=GameSystemGeneratorTestHelper.RunGenerator(Source);
        Assert.Contains(Result.Diagnostics,static Diagnostic=>Diagnostic.Id=="SYSGEN044");
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SceneOnlyStateDoesNotRequireSnapshotSupportOrIntroduceReflection(bool UnrelatedSnapshot){
        const string Source="""
            using CSharpGameEngine.Entities;
            namespace Sample;
            public struct Persistent {public object Native;}
            internal struct System:ISystem {public void Update(ref Persistent Value){}}
            """;
        var Unrelated=UnrelatedSnapshot?"public sealed class Camera {public void CaptureSnapshot(){} public void Run(){this.CaptureSnapshot();}}":string.Empty;
        var Result=GameSystemGeneratorTestHelper.RunGenerator(Source+Unrelated);
        Assert.DoesNotContain(Result.Diagnostics,static Diagnostic=>Diagnostic.Id=="SYSGEN044");
        var Text=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Snapshot.g.cs");
        Assert.DoesNotContain("EcsSnapshotComponentCodec",Text,StringComparison.Ordinal);
        Assert.Contains("NotSupportedException",Text,StringComparison.Ordinal);
    }
    [Fact]
    public void NestedValueGraphPreservesBytesAndConstructorValidation(){
        const string Components="""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Numerics;
            namespace Sample;
            public enum Mode:byte {First,Second}
            public readonly struct Limits {
                public float Minimum{get;}
                public float Maximum{get;}
                public bool IsValid=>Minimum<=Maximum;
                public Limits(float Minimum,float Maximum){
                    if(Minimum>Maximum)throw new System.ArgumentException("minimum exceeds maximum");
                    this.Minimum=Minimum;this.Maximum=Maximum;
                }
            }
            public readonly struct Range {
                public readonly int Low;
                public readonly int High;
                public Range(int High,int Low){this.High=High;this.Low=Low;}
            }
            public struct Aggregate {
                public LogicalVector3 Position;
                public Limits Bounds;
                public Mode? Choice;
                public CSharpGameEngine.Core.StableId Asset;
                public Range Range;
                public string? Label;
            }
            internal struct AggregateSystem:ISystem {public void Update(in Aggregate Value){}}
            """;
        const string Probe="""
            namespace CSharpGameEngine.Entities {
                public partial struct World {
                    public static string CheckGraph() {
                        var Original=new Sample.Aggregate {
                            Position=new(CSharpGameEngine.Numerics.LogicalScalar.FromRaw(1),CSharpGameEngine.Numerics.LogicalScalar.ParseInvariant("-2.5"),CSharpGameEngine.Numerics.LogicalScalar.One),
                            Bounds=new(-2,3),Choice=Sample.Mode.Second,
                            Range=new(7,-3),
                            Asset=CSharpGameEngine.Core.StableId.Parse("98a2fc1e-d67f-5d92-8813-00ce97d444fd")
                        };
                        var Bytes=SerializeSnapshotComponent0(in Original);
                        if(!DeserializeSnapshotComponent0(Bytes,out var Restored,out var Error)) return Error;
                        if(Restored.Position!=Original.Position||Restored.Choice!=Original.Choice||Restored.Asset!=Original.Asset||Restored.Bounds.Minimum!=-2) return "state mismatch";
                        if(Restored.Range.Low!=-3||Restored.Range.High!=7) return "readonly field order mismatch";
                        var Invalid=System.Text.Encoding.UTF8.GetString(Bytes).Replace("\"Minimum\":\"-2\"","\"Minimum\":\"4\"");
                        if(DeserializeSnapshotComponent0(System.Text.Encoding.UTF8.GetBytes(Invalid),out _,out _)) return "invalid constructor accepted";
                        Original.Choice=null;
                        Bytes=SerializeSnapshotComponent0(in Original);
                        if(!DeserializeSnapshotComponent0(Bytes,out Restored,out Error)||Restored.Choice!=null) return "nullable mismatch";
                        return "OK";
                    }
                }
            }
            """;
        var Compilation=GameSystemGeneratorTestHelper.CreateCompilation([(null,Components),(null,Probe)]);
        Compilation=Compilation.WithOptions(Compilation.Options.WithNullableContextOptions(Microsoft.CodeAnalysis.NullableContextOptions.Enable).WithGeneralDiagnosticOption(Microsoft.CodeAnalysis.ReportDiagnostic.Error));
        var Assembly=GameSystemGeneratorTestHelper.CompileAndLoad(Compilation);
        Assert.Equal("OK",GameSystemGeneratorTestHelper.Invoke(Assembly,"CSharpGameEngine.Entities.World","CheckGraph"));
    }
    [Fact]
    public void GeneratedFixedScalarPreservesExactPersistedValue(){
        const string Components="""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Numerics;
            namespace Sample;
            public struct FixedPosition {public LogicalScalar X;}
            internal struct FixedSystem:ISystem {public void Update(in FixedPosition Value){}}
            """;
        const string Probe="""
            namespace CSharpGameEngine.Entities {
                public partial struct World {
                    public static bool CheckFixedScalar() {
                        foreach(var Raw in new[]{System.Int128.MinValue,System.Int128.MaxValue,(System.Int128)1,(System.Int128)(-1)}) {
                            var Original=new Sample.FixedPosition {X=CSharpGameEngine.Numerics.LogicalScalar.FromRaw(Raw)};
                            var Bytes=SerializeSnapshotComponent0(in Original);
                            var Previous=EcsSnapshotComponentCodec<Sample.FixedPosition>.Serialize(in Original);
                            if(!Bytes.AsSpan().SequenceEqual(Previous)) return false;
                            if(!DeserializeSnapshotComponent0(Bytes,out var Restored,out _)) return false;
                            if(Restored.X.Raw!=Raw) return false;
                        }
                        return true;
                    }
                }
            }
            """;
        var Assembly=GameSystemGeneratorTestHelper.CompileAndLoad(Components,Probe);
        Assert.Equal(true,GameSystemGeneratorTestHelper.Invoke(Assembly,"CSharpGameEngine.Entities.World","CheckFixedScalar"));
    }
    [Fact]
    public void NestedComponentsReceiveGeneratedMetadataAndValueCodecs(){
        const string Source="""
            using CSharpGameEngine.Entities;
            namespace Sample;
            public static class DataTypes { public struct Position {public float X;} }
            internal struct Move:ISystem {public void Update(ref DataTypes.Position Value){Value.X++;}}
            """;
        var Result=GameSystemGeneratorTestHelper.RunGenerator(Source);
        var Text=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Snapshot.g.cs");
        Assert.Contains("new global::CSharpGameEngine.Entities.EcsSnapshotComponentDescriptor(\"Sample.DataTypes+Position\", false,",Text,StringComparison.Ordinal);
        Assert.Contains("SerializeSnapshotComponent0",Text,StringComparison.Ordinal);
        Assert.Contains("DeserializeSnapshotComponent0",Text,StringComparison.Ordinal);
    }
    [Fact]
    public void GeneratedValueCodecUsesCommonNumbersAndRejectsIncompletePayload(){
        const string Probe="""
            namespace CSharpGameEngine.Entities {
                public partial struct World {
                    public static bool CheckGeneratedValues() {
                        var Original=new Sample.Position {X=1.25f,Y=-2f,Z=0.125f};
                        var Bytes=SerializeSnapshotComponent0(in Original);
                        var Previous=EcsSnapshotComponentCodec<Sample.Position>.Serialize(in Original);
                        if(System.Text.Encoding.UTF8.GetString(Bytes)!="{\"X\":\"1.25\",\"Y\":\"-2\",\"Z\":\"0.125\"}") return false;
                        // 版 1 の形（JSON の数値）は、生成された読み手が拒否する。
                        if(DeserializeSnapshotComponent0(Previous,out _,out _)) return false;
                        if(!DeserializeSnapshotComponent0(Bytes,out var Restored,out _)) return false;
                        if(Restored.X!=Original.X||Restored.Y!=Original.Y||Restored.Z!=Original.Z) return false;
                        foreach(var Json in new[]{"{}","{\"X\":1,\"Y\":2}","{\"X\":1,\"X\":2,\"Z\":3}","{\"X\":null,\"Y\":2,\"Z\":3}"}) {
                            if(DeserializeSnapshotComponent0(System.Text.Encoding.UTF8.GetBytes(Json),out _,out _)) return false;
                        }
                        return true;
                    }
                }
            }
            """;
        var Assembly=GameSystemGeneratorTestHelper.CompileAndLoad(GameSystemGeneratorTestHelper.CommonComponents,Systems,Probe);
        Assert.Equal(true,GameSystemGeneratorTestHelper.Invoke(Assembly,"CSharpGameEngine.Entities.World","CheckGeneratedValues"));
    }
    [Fact]
    public void GeneratedSchemaHasOnlyThePortableStructure(){
        // ADR-0098 §5 規則 2：生成コードに旧版の構造の対応表を残さない。
        var Result=GameSystemGeneratorTestHelper.RunGenerator(GameSystemGeneratorTestHelper.CommonComponents,Systems);
        var Text=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Snapshot.g.cs");
        Assert.Contains("SnapshotSchemaFingerprint",Text,StringComparison.Ordinal);
        Assert.DoesNotContain("Legacy",Text,StringComparison.Ordinal);
        Assert.DoesNotContain("FormatVersion",Text,StringComparison.Ordinal);
    }
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
        Assert.Contains("private static byte[] SerializeSnapshotComponent",Text,StringComparison.Ordinal);
        Assert.Contains("EcsSnapshotValueCodec.WriteSingle",Text,StringComparison.Ordinal);
        Assert.Contains("this.Commands.Count != 0 || this.Commands.IsSystemExecuting",Text,StringComparison.Ordinal);
    }

    [Fact]
    public void SchemaDescribesRuntimeComponentsButCaptureExcludesTheirPayload(){
        var Result=GameSystemGeneratorTestHelper.RunGenerator(GameSystemGeneratorTestHelper.CommonComponents,Systems);

        string Text=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Snapshot.g.cs");

        Assert.Contains("new global::CSharpGameEngine.Entities.EcsSnapshotComponentDescriptor(\"Sample.Position\", false,",Text,StringComparison.Ordinal);
        Assert.Contains("new global::CSharpGameEngine.Entities.EcsSnapshotComponentDescriptor(\"Sample.RuntimeState\", true, \"transient\")",Text,StringComparison.Ordinal);
        Assert.Contains(".Transient)",Text,StringComparison.Ordinal);
        Assert.DoesNotContain(".IsTransient",Text,StringComparison.Ordinal);
        Assert.DoesNotContain("EcsSnapshotComponentCodec",Text,StringComparison.Ordinal);
        Assert.DoesNotContain("\\n",Text,StringComparison.Ordinal);
    }
}
