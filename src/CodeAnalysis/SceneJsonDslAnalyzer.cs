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
/// シーンファイル（<c>Scenes\**\*.json</c>、<see cref="SceneFiles"/>)について、JSON上の行と列でSCENE201-205を報告する（ADR-0070）。
/// ジェネレーターも出力可能か判断するため同じ<see cref="SceneJsonSceneAnalyzer"/>を実行するが、各問題は一度だけ報告する。
/// <c>startupOrder</c>が重複するシーンはSCENE118（後から現れる各シーンの値の位置で報告）、同じクラスを宣言するシーンファイルはSCENE207
///（後から現れる各ファイルのclassNameの位置で報告）となる。どちらも全ファイルを調べる必要があるため、コンパイル終了時に報告する。
/// パス順で最初のファイルが順序またはクラスを所有し、ジェネレーターはそのシーンだけを構築する。
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SceneJsonDslAnalyzer:DiagnosticAnalyzer {
    /// <summary>ビルドはプロジェクトフォルダー（CompilerVisibleProperty）を渡すため、<see cref="SceneFiles"/>でシーンを見つけられる。</summary>
    internal const string ProjectDirectoryProperty="build_property.ProjectDir";
    public override ImmutableArray<DiagnosticDescriptor>SupportedDiagnostics=>
        ImmutableArray.Create(SceneJsonDiagnostics.UnknownComponentType,SceneJsonDiagnostics.UnknownMember,SceneJsonDiagnostics.ValueMismatch,
            SceneJsonDiagnostics.InvalidDocument,SceneJsonDiagnostics.DuplicateComponent,SceneJsonDiagnostics.DuplicateScene,
            SceneDslDiagnostics.DuplicateStartupSceneOrder);
    public override void Initialize(AnalysisContext Context){
        Context.EnableConcurrentExecution();
        Context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        Context.RegisterCompilationStartAction(static CompilationStart=>{
            // SceneDslAnalyzerと同じ条件で判定する。エンジンのシーンモデルを使用するプロジェクトだけが対象。
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
                // ファイルの到着順は不定。パス順で最初のシーンが各順序を所有し、報告結果は安定する。
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
    /// <summary>
    /// <c>#</c>と<c>;</c>を<c>?h</c>と<c>?s</c>に置き換えて記述したプロジェクトフォルダー（<see cref="GeneratorPath"/>）。ビルドのProjectDirは
    /// .editorconfigリーダーによってこれらの文字で切り詰められるため、代替値としてのみ使用する。
    /// </summary>
    internal const string EncodedProjectDirectoryProperty="build_property.CgeProjectDirectory";
    /// <summary>有効なプラグインのフォルダー（ADR-0079）。同様にエンコードした<c>|Name=Folder</c>の組で表し、各フォルダーの<c>Scenes</c>にもシーンがある。</summary>
    internal const string PluginRootsProperty="build_property.CgePluginRootsEncoded";
    internal static bool IsSceneFile(string Path,AnalyzerConfigOptions GlobalOptions)=>
        ProjectDirectoryOf(GlobalOptions) is { }ProjectDirectory
        &&SceneFiles.IsScene(Path,ProjectDirectory,PluginRootsOf(GlobalOptions));
    internal static string?ProjectDirectoryOf(AnalyzerConfigOptions GlobalOptions)=>
        GlobalOptions.TryGetValue(EncodedProjectDirectoryProperty,out var Encoded)&&Encoded.Length>0?GeneratorPath.Decode(Encoded)
        :GlobalOptions.TryGetValue(ProjectDirectoryProperty,out var ProjectDirectory)?ProjectDirectory
        :null;
    internal static IReadOnlyList<PluginRoot>PluginRootsOf(AnalyzerConfigOptions GlobalOptions)=>
        PluginRoot.Parse(GlobalOptions.TryGetValue(PluginRootsProperty,out var Value)?Value:null);
    internal static Location Locate(string Path,SourceText Text,int Start,int End){
        var Length=Text.Length;
        var SpanStart=System.Math.Min(System.Math.Max(Start,0),Length);
        var SpanEnd=System.Math.Min(System.Math.Max(End,SpanStart),Length);
        var Span=TextSpan.FromBounds(SpanStart,SpanEnd);
        return Location.Create(Path,Span,Text.Lines.GetLinePositionSpan(Span));
    }
}
