using CSharpGameEngine.Lighting;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
namespace CSharpGameEngine.VSExtension.Tests;
internal static class TypedLightTestScene {
    internal static readonly SceneKey Key=new("lights","Game.MainScene");
    internal static (SceneModel Model,CSharpCompilation Compilation,string Source)Analyze(string Components,bool AddTransform=true){
        var Source="""using CSharpGameEngine.Entities; using CSharpGameEngine.Runtime.Lighting; using CSharpGameEngine.Runtime.Windows; using CSharpGameEngine.Lighting; namespace Game; public partial class MainScene:IScene { public Node Root=>new() { Name="Lamp", Components=["""+(AddTransform?"new Transform(),":"")+Components+"] }; }";
        return AnalyzeSource(Source);
    }
    internal static (SceneModel Model,CSharpCompilation Compilation,string Source)AnalyzeSource(string Source){
        var Root=VsixBuildIdentity.RepoRoot;
        var Files=new[]{
            File.ReadAllText(Path.Combine(Root,"src/CSharpGameEngine.Runtime.Lighting/LightComponents.cs")),
            File.ReadAllText(Path.Combine(Root,"src/CSharpGameEngine.Entities/SceneComponentOrderAttribute.cs")),
            File.ReadAllText(Path.Combine(Root,"src/CSharpGameEngine.Entities/SceneComponentIconAttribute.cs")),
            "namespace CSharpGameEngine.Entities { [SceneComponentOrder(SceneComponentCategory.Spatial,0)] public struct Transform {} }",
            "namespace CSharpGameEngine.Runtime.Windows { public enum CameraProjection { Perspective,Orthographic } public struct Camera { public CameraProjection Projection; public float FovYRadians; public float NearZ; public float FarZ; public float OrthographicSize; } }",
            Source,
        };
        var Compilation=SceneAnalysisTestHelper.CreateCompilation(Files);
        if(Compilation.GetTypeByMetadataName(typeof(LuminousFlux).FullName!) is null){
            Compilation=Compilation.AddReferences(MetadataReference.CreateFromFile(typeof(LuminousFlux).Assembly.Location));
        }
        var Type=Compilation.GetTypeByMetadataName("Game.MainScene")!;
        var Model=SceneDeclarationAnalyzer.Analyze(Type,Compilation,CancellationToken.None);
        return(Model,Compilation,Source);
    }
}
