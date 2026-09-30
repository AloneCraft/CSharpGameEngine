using System.Collections.Immutable;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
namespace CSharpGameEngine.CodeAnalysis;
/// <summary>
/// Reports SCENE201-205 on <c>*.scene.json</c> additional files at the JSON line and column
/// (ADR-0070). The generator runs the same <see cref="SceneJsonSceneAnalyzer"/> only to decide
/// whether it can emit, so each problem is reported once.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SceneJsonDslAnalyzer:DiagnosticAnalyzer {
    internal const string SceneFileSuffix=".scene.json";
    public override ImmutableArray<DiagnosticDescriptor>SupportedDiagnostics=>[.. SceneJsonDiagnostics.All];
    public override void Initialize(AnalysisContext Context){
        Context.EnableConcurrentExecution();
        Context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        Context.RegisterCompilationStartAction(static CompilationStart=>{
            // Same gate as SceneDslAnalyzer: only projects that use the engine's scene model.
            if(CompilationStart.Compilation.GetTypeByMetadataName(SceneDslWellKnownNames.SceneInterfaceMetadataName) is null){
                return;
            }
            CompilationStart.RegisterAdditionalFileAction(static FileContext=>{
                var File=FileContext.AdditionalFile;
                if(!IsSceneFile(File.Path)||File.GetText(FileContext.CancellationToken) is not { }Text){
                    return;
                }
                var Scene=SceneJsonSceneAnalyzer.Analyze(Text.ToString(),FileContext.Compilation);
                foreach(var Problem in Scene.Problems){
                    FileContext.ReportDiagnostic(Diagnostic.Create(Problem.Descriptor,Locate(File.Path,Text,Problem.Start,Problem.End),Problem.Arguments));
                }
            });
        });
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
