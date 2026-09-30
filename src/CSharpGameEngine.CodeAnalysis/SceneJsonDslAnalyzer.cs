using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
namespace CSharpGameEngine.CodeAnalysis;
/// <summary>
/// Reports SCENE201-205 on <c>*.scene.json</c> additional files at the JSON line and column
/// (ADR-0070). The generator runs the same <see cref="SceneJsonSceneAnalyzer"/> only to decide
/// whether it can emit, so each problem is reported once. Two scenes with the same
/// <c>startupOrder</c> are SCENE118, reported at each later scene's value; that needs every file, so it
/// is reported when the compilation ends.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SceneJsonDslAnalyzer:DiagnosticAnalyzer {
    internal const string SceneFileSuffix=".scene.json";
    public override ImmutableArray<DiagnosticDescriptor>SupportedDiagnostics=>
        ImmutableArray.Create(SceneJsonDiagnostics.UnknownComponentType,SceneJsonDiagnostics.UnknownMember,SceneJsonDiagnostics.ValueMismatch,
            SceneJsonDiagnostics.InvalidDocument,SceneJsonDiagnostics.DuplicateComponent,SceneDslDiagnostics.DuplicateStartupSceneOrder);
    public override void Initialize(AnalysisContext Context){
        Context.EnableConcurrentExecution();
        Context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        Context.RegisterCompilationStartAction(static CompilationStart=>{
            // Same gate as SceneDslAnalyzer: only projects that use the engine's scene model.
            if(CompilationStart.Compilation.GetTypeByMetadataName(SceneDslWellKnownNames.SceneInterfaceMetadataName) is null){
                return;
            }
            ConcurrentBag<(int Order,string ClassName,string Path,Location Location)>StartupScenes=[];
            CompilationStart.RegisterAdditionalFileAction(FileContext=>{
                var File=FileContext.AdditionalFile;
                if(!IsSceneFile(File.Path)||File.GetText(FileContext.CancellationToken) is not { }Text){
                    return;
                }
                var Scene=SceneJsonSceneAnalyzer.Analyze(Text.ToString(),FileContext.Compilation);
                foreach(var Problem in Scene.Problems){
                    FileContext.ReportDiagnostic(Diagnostic.Create(Problem.Descriptor,Locate(File.Path,Text,Problem.Start,Problem.End),Problem.Arguments));
                }
                if(Scene.StartupOrder is { }Order&&StartupOrderValue(Text.ToString()) is { }Value){
                    var ClassName=Scene.Namespace is null?Scene.ClassName:Scene.Namespace+"."+Scene.ClassName;
                    StartupScenes.Add((Order,ClassName,File.Path,Locate(File.Path,Text,Value.Start,Value.End)));
                }
            });
            CompilationStart.RegisterCompilationEndAction(EndContext=>{
                // Files arrive in any order; the first scene by path owns each order, so the report is stable.
                foreach(var Group in StartupScenes.GroupBy(static Scene=>Scene.Order)){
                    var Ordered=Group.OrderBy(static Scene=>Scene.Path,System.StringComparer.Ordinal).ToArray();
                    foreach(var Later in Ordered.Skip(1)){
                        EndContext.ReportDiagnostic(Diagnostic.Create(SceneDslDiagnostics.DuplicateStartupSceneOrder,Later.Location,Group.Key,Ordered[0].ClassName,Later.ClassName));
                    }
                }
            });
        });
    }
    private static SceneJsonValue?StartupOrderValue(string Text){
        try {
            return SceneJsonReader.Parse(Text).Get("startupOrder");
        } catch(SceneJsonException){
            return null;
        }
    }
    internal static bool IsSceneFile(string Path)=>Path.EndsWith(SceneFileSuffix,System.StringComparison.OrdinalIgnoreCase);
    internal static Location Locate(string Path,SourceText Text,int Start,int End){
        var Length=Text.Length;
        var SpanStart=System.Math.Min(System.Math.Max(Start,0),Length);
        var SpanEnd=System.Math.Min(System.Math.Max(End,SpanStart),Length);
        var Span=TextSpan.FromBounds(SpanStart,SpanEnd);
        return Location.Create(Path,Span,Text.Lines.GetLinePositionSpan(Span));
    }
}
