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
/// Reports SCENE201-205 on scene files (<c>Scenes\**\*.json</c>, <see cref="SceneFiles"/>) at the JSON line and column
/// (ADR-0070). The generator runs the same <see cref="SceneJsonSceneAnalyzer"/> only to decide
/// whether it can emit, so each problem is reported once. Two scenes with the same
/// <c>startupOrder</c> are SCENE118, reported at each later scene's value, and two scene files that declare the same
/// class are SCENE207, reported at each later file's className; both need every file, so they are reported when the
/// compilation ends. The first file by path owns the order or the class (the generator builds only that scene).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SceneJsonDslAnalyzer:DiagnosticAnalyzer {
    /// <summary>The build passes the project folder (CompilerVisibleProperty) so scenes are found by <see cref="SceneFiles"/>.</summary>
    internal const string ProjectDirectoryProperty="build_property.ProjectDir";
    public override ImmutableArray<DiagnosticDescriptor>SupportedDiagnostics=>
        ImmutableArray.Create(SceneJsonDiagnostics.UnknownComponentType,SceneJsonDiagnostics.UnknownMember,SceneJsonDiagnostics.ValueMismatch,
            SceneJsonDiagnostics.InvalidDocument,SceneJsonDiagnostics.DuplicateComponent,SceneJsonDiagnostics.DuplicateScene,
            SceneDslDiagnostics.DuplicateStartupSceneOrder);
    public override void Initialize(AnalysisContext Context){
        Context.EnableConcurrentExecution();
        Context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        Context.RegisterCompilationStartAction(static CompilationStart=>{
            // Same gate as SceneDslAnalyzer: only projects that use the engine's scene model.
            if(CompilationStart.Compilation.GetTypeByMetadataName(SceneDslWellKnownNames.SceneInterfaceMetadataName) is null){
                return;
            }
            ConcurrentBag<(int Order,string ClassName,string Path,Location Location)>StartupScenes=[];
            ConcurrentBag<(string ClassName,string Path,Location Location)>Scenes=[];
            CompilationStart.RegisterAdditionalFileAction(FileContext=>{
                var File=FileContext.AdditionalFile;
                if(!IsSceneFile(File.Path,FileContext.Options.AnalyzerConfigOptionsProvider.GlobalOptions)||File.GetText(FileContext.CancellationToken) is not { }Text){
                    return;
                }
                var Scene=SceneJsonSceneAnalyzer.Analyze(Text.ToString(),FileContext.Compilation);
                foreach(var Problem in Scene.Problems){
                    FileContext.ReportDiagnostic(Diagnostic.Create(Problem.Descriptor,Locate(File.Path,Text,Problem.Start,Problem.End),Problem.Arguments));
                }
                var ClassName=Scene.Namespace is null?Scene.ClassName:Scene.Namespace+"."+Scene.ClassName;
                if(Scene.StartupOrder is { }Order&&TopLevelValue(Text.ToString(),"startupOrder") is { }Value){
                    StartupScenes.Add((Order,ClassName,File.Path,Locate(File.Path,Text,Value.Start,Value.End)));
                }
                if(Scene.ClassName.Length>0&&TopLevelValue(Text.ToString(),"className") is { }ClassValue){
                    Scenes.Add((ClassName,File.Path,Locate(File.Path,Text,ClassValue.Start,ClassValue.End)));
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
                foreach(var Group in Scenes.GroupBy(static Scene=>Scene.ClassName,System.StringComparer.Ordinal)){
                    var Ordered=Group.OrderBy(static Scene=>Scene.Path,System.StringComparer.Ordinal).ToArray();
                    foreach(var Later in Ordered.Skip(1)){
                        EndContext.ReportDiagnostic(Diagnostic.Create(SceneJsonDiagnostics.DuplicateScene,Later.Location,Group.Key,System.IO.Path.GetFileName(Ordered[0].Path)));
                    }
                }
            });
        });
    }
    private static SceneJsonValue?TopLevelValue(string Text,string Key){
        try {
            return SceneJsonReader.Parse(Text).Get(Key);
        } catch(SceneJsonException){
            return null;
        }
    }
    internal static bool IsSceneFile(string Path,AnalyzerConfigOptions GlobalOptions)=>
        GlobalOptions.TryGetValue(ProjectDirectoryProperty,out var ProjectDirectory)&&SceneFiles.IsScene(Path,ProjectDirectory);
    internal static Location Locate(string Path,SourceText Text,int Start,int End){
        var Length=Text.Length;
        var SpanStart=System.Math.Min(System.Math.Max(Start,0),Length);
        var SpanEnd=System.Math.Min(System.Math.Max(End,SpanStart),Length);
        var Span=TextSpan.FromBounds(SpanStart,SpanEnd);
        return Location.Create(Path,Span,Text.Lines.GetLinePositionSpan(Span));
    }
}
