using System.Collections.Immutable;
using CSharpGameEngine.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CSharpGameEngine.CodeAnalysis.Tests;

public sealed class LightApiMigrationAnalyzerTests {
    private static readonly MetadataReference[] References=[
        .. AppDomain.CurrentDomain.GetAssemblies()
            .Where(static Assembly=>!Assembly.IsDynamic&&!string.IsNullOrEmpty(Assembly.Location))
            .Select(static Assembly=>MetadataReference.CreateFromFile(Assembly.Location)),
    ];

    [Fact]
    public void LegacyObjectInitializers_ReportPhysicalFieldAndUnitGuidance(){
        var Diagnostics=Analyze(Preamble+"""
            namespace Game;
            public static class Scene {
                public static void Build(){
                    _=new UserEcsProject.DirectionalLight { Intensity=1f,DirectionTowardLightZ=1f };
                    _=new UserEcsProject.PointLight { Intensity=2f,PositionX=3f,Range=4f };
                    _=new UserEcsProject.SpotLight {
                        Intensity=5f,DirectionZ=1f,Range=6f,
                        InnerConeAngleRadians=0.2f,OuterConeAngleRadians=0.4f,
                    };
                    _=new UserEcsProject.AreaLight { Intensity=7f,Width=2f,Height=3f,PositionY=4f };
                }
            }
            """);

        Assert.Equal(14,Diagnostics.Count(static Diagnostic=>Diagnostic.Id=="LGT001"));
        Assert.Contains(Diagnostics,static Diagnostic=>Diagnostic.GetMessage().Contains("Illuminance=new Illuminance",StringComparison.Ordinal));
        Assert.Contains(Diagnostics,static Diagnostic=>Diagnostic.GetMessage().Contains("Flux=new LuminousFlux",StringComparison.Ordinal));
        Assert.Contains(Diagnostics,static Diagnostic=>Diagnostic.GetMessage().Contains("Luminance=new Luminance",StringComparison.Ordinal));
        Assert.Contains(Diagnostics,static Diagnostic=>Diagnostic.GetMessage().Contains("fullDegrees = radians * 360 / Math.PI",StringComparison.Ordinal));
        Assert.Contains(Diagnostics,static Diagnostic=>Diagnostic.GetMessage().Contains("Transform.Rotation",StringComparison.Ordinal));
    }

    [Fact]
    public void LegacyAssignmentOutsideAnInitializer_IsReportedOnce(){
        var Diagnostics=Analyze(Preamble+"""
            namespace Game;
            public static class Scene {
                public static void Build(){
                    UserEcsProject.PointLight light=default;
                    light.Intensity=20f;
                    light.Range=8f;
                }
            }
            """);

        Assert.Equal(["LGT001","LGT001"],Ids(Diagnostics));
    }

    [Fact]
    public void RemovedLegacyMember_IsReportedInObjectInitializerAndStandaloneAssignmentWithoutDuplicates(){
        var Diagnostics=Analyze(PhysicalOnlyPreamble+"""
            namespace Game;
            public static class Scene {
                public static void Build(){
                    _=new UserEcsProject.PointLight { Intensity=20f };
                    UserEcsProject.PointLight light=default;
                    light.Intensity=30f;
                }
            }
            """);

        var LegacyDiagnostics=Diagnostics.Where(static Diagnostic=>Diagnostic.Id=="LGT001").ToArray();

        Assert.Equal(2,LegacyDiagnostics.Length);
        Assert.All(LegacyDiagnostics,static Diagnostic=>
            Assert.Contains("Flux=new LuminousFlux",Diagnostic.GetMessage(),StringComparison.Ordinal));
    }

    [Fact]
    public void PreviousScalarVocabulary_RequiresTypedValues(){
        var Diagnostics=Analyze(Preamble+"""
            namespace Game;
            public static class Scene {
                public static void Build(){
                    _=new UserEcsProject.SpotLight {
                        LuminousFluxLumens=800f,RangeMeters=10f,
                        InnerConeFullAngleDegrees=30f,OuterConeFullAngleDegrees=45f,
                    };
                }
            }
            """);

        Assert.Equal(4,Diagnostics.Length);
    }

    [Fact]
    public void PreviousWindowsVocabulary_RequiresTypedValues(){
        var Diagnostics=Analyze(Preamble+"""
            namespace CSharpGameEngine.Runtime.Windows {
                public struct PointLight {
                    public float LuminousFluxLumens;
                    public float RangeMeters;
                }
            }
            namespace Game;
            public static class Scene {
                public static void Build()=>_ = new CSharpGameEngine.Runtime.Windows.PointLight {
                    LuminousFluxLumens=800f,RangeMeters=10f,
                };
            }
            """);

        Assert.Equal(2,Diagnostics.Length);
    }
    [Fact]
    public void ProjectWithoutTransformPrecisionContract_IsNotAnalyzed(){
        var Diagnostics=Analyze(Preamble.Replace(
            "[assembly: CSharpGameEngine.Entities.TransformPrecision(384,173)]",string.Empty,StringComparison.Ordinal)+"""
            namespace Game;
            public static class Scene {
                public static void Build()=>_ = new UserEcsProject.PointLight { Intensity=1f };
            }
            """);

        Assert.Empty(Diagnostics);
    }

    private static ImmutableArray<Diagnostic> Analyze(string Source){
        var Compilation=CSharpCompilation.Create(
            "LightApiMigrationTests",
            [CSharpSyntaxTree.ParseText(Source,new CSharpParseOptions(LanguageVersion.Latest))],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var Analyzers=Compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new LightApiMigrationAnalyzer()));
        return Analyzers.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
    }

    private static string[] Ids(ImmutableArray<Diagnostic> Diagnostics)=>
        [.. Diagnostics.Select(static Diagnostic=>Diagnostic.Id).OrderBy(static Id=>Id,StringComparer.Ordinal)];

    private const string Preamble="""
        [assembly: CSharpGameEngine.Entities.TransformPrecision(384,173)]
        namespace CSharpGameEngine.Entities {
            [global::System.AttributeUsage(global::System.AttributeTargets.Assembly)]
            public sealed class TransformPrecisionAttribute:global::System.Attribute {
                public TransformPrecisionAttribute(int totalBits,int fractionalBits) { }
            }
        }
        namespace UserEcsProject {
            public struct DirectionalLight {
                public float Intensity;
                public float DirectionTowardLightZ;
                public float IlluminanceLux;
            }
            public struct PointLight {
                public float Intensity;
                public float PositionX;
                public float Range;
                public float LuminousFluxLumens;
                public float RangeMeters;
            }
            public struct SpotLight {
                public float Intensity;
                public float DirectionZ;
                public float Range;
                public float InnerConeAngleRadians;
                public float OuterConeAngleRadians;
                public float LuminousFluxLumens;
                public float RangeMeters;
                public float InnerConeFullAngleDegrees;
                public float OuterConeFullAngleDegrees;
            }
            public struct AreaLight {
                public float Intensity;
                public float Width;
                public float Height;
                public float PositionY;
                public float LuminanceNits;
                public float WidthMeters;
                public float HeightMeters;
            }
        }
        """;

    private const string PhysicalOnlyPreamble="""
        [assembly: CSharpGameEngine.Entities.TransformPrecision(384,173)]
        namespace CSharpGameEngine.Entities {
            [global::System.AttributeUsage(global::System.AttributeTargets.Assembly)]
            public sealed class TransformPrecisionAttribute:global::System.Attribute {
                public TransformPrecisionAttribute(int totalBits,int fractionalBits) { }
            }
        }
        namespace UserEcsProject {
            public struct PointLight {
                public float LuminousFluxLumens;
                public float RangeMeters;
            }
        }
        """;
}
