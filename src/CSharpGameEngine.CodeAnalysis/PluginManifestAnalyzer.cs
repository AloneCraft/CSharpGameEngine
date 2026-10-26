using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// 有効なプラグインのマニフェストを検査する（ADR-0079）。ビルドは有効な各プラグインの<c>&lt;Name&gt;.cgeplugin</c>を
/// 追加ファイルとして渡すため、その中に含まれない依存プラグインは有効ではない。エンジンのバージョンにはビルドの<c>CgeEngineVersion</c>を使う。
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PluginManifestAnalyzer:DiagnosticAnalyzer {
    private const string Category="CSharpGameEngine.Plugins";
    internal const string EngineVersionProperty="build_property.CgeEngineVersion";

    internal static readonly DiagnosticDescriptor InvalidManifest=new(
        id:"PLUGIN001",
        title:"A plugin manifest must be valid",
        messageFormat:"The plugin manifest is not valid: {0}",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor DuplicatePlugin=new(
        id:"PLUGIN002",
        title:"Two enabled plugins must not have the same name",
        messageFormat:"The plugin '{0}' is enabled twice ({1} and {2}); remove one of them",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true,
        customTags:WellKnownDiagnosticTags.CompilationEnd);

    internal static readonly DiagnosticDescriptor MissingDependency=new(
        id:"PLUGIN003",
        title:"A plugin's dependency must be enabled",
        messageFormat:"The plugin '{0}' needs the plugin '{1}', which is not enabled (add it to CgePlugins, Plugins/ or the package references)",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true,
        customTags:WellKnownDiagnosticTags.CompilationEnd);

    internal static readonly DiagnosticDescriptor DependencyCycle=new(
        id:"PLUGIN004",
        title:"Plugin dependencies must not form a cycle",
        messageFormat:"The plugins depend on each other in a cycle: {0}",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true,
        customTags:WellKnownDiagnosticTags.CompilationEnd);

    internal static readonly DiagnosticDescriptor EngineVersionMismatch=new(
        id:"PLUGIN005",
        title:"A plugin must support the engine version",
        messageFormat:"The plugin '{0}' is written for engine versions '{1}', but this engine is {2}",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    public override ImmutableArray<DiagnosticDescriptor>SupportedDiagnostics=>
        ImmutableArray.Create(InvalidManifest,DuplicatePlugin,MissingDependency,DependencyCycle,EngineVersionMismatch);

    public override void Initialize(AnalysisContext Context){
        Context.EnableConcurrentExecution();
        Context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        Context.RegisterCompilationStartAction(static CompilationStart=>{
            CompilationStart.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(EngineVersionProperty,out var EngineVersionText);
            PluginVersion?EngineVersion=PluginVersion.TryParse(EngineVersionText,out var Parsed)?Parsed:null;
            ConcurrentBag<(PluginManifest Manifest,string Path,SourceText Text)>Manifests=[];
            CompilationStart.RegisterAdditionalFileAction(FileContext=>{
                var File=FileContext.AdditionalFile;
                if(!File.Path.EndsWith(PluginManifest.Extension,StringComparison.OrdinalIgnoreCase)||File.GetText(FileContext.CancellationToken) is not { }Text){
                    return;
                }
                var Manifest=PluginManifest.Parse(Text.ToString(),out var Problem,out var Start,out var End);
                if(Manifest is null){
                    FileContext.ReportDiagnostic(Diagnostic.Create(InvalidManifest,SceneJsonDslAnalyzer.Locate(File.Path,Text,Start,End),Problem));
                    return;
                }
                if(EngineVersion is { }Engine&&Manifest.EngineVersion is { }Range&&PluginVersionRange.TryParse(Range,out var Constraints)&&!Constraints.Contains(Engine)){
                    FileContext.ReportDiagnostic(Diagnostic.Create(EngineVersionMismatch,SceneJsonDslAnalyzer.Locate(File.Path,Text,Manifest.EngineVersionStart,Manifest.EngineVersionEnd),
                        Manifest.Name,Range,Engine.ToString()));
                }
                Manifests.Add((Manifest,File.Path,Text));
            });
            CompilationStart.RegisterCompilationEndAction(EndContext=>{
                // ファイルの到着順は不定なので、パス順に並べ替える。最初のファイルが名前を所有し、診断結果も安定する。
                var Ordered=Manifests.OrderBy(static Entry=>Entry.Path,StringComparer.Ordinal).ToArray();
                Dictionary<string,(PluginManifest Manifest,string Path,SourceText Text)>ByName=new(StringComparer.Ordinal);
                foreach(var Entry in Ordered){
                    if(ByName.TryGetValue(Entry.Manifest.Name,out var First)){
                        EndContext.ReportDiagnostic(Diagnostic.Create(DuplicatePlugin,NameLocation(Entry),Entry.Manifest.Name,First.Path,Entry.Path));
                        continue;
                    }
                    ByName.Add(Entry.Manifest.Name,Entry);
                }
                foreach(var Entry in ByName.Values.OrderBy(static Entry=>Entry.Path,StringComparer.Ordinal)){
                    foreach(var (Dependency,Start,End) in Entry.Manifest.DependencySpans){
                        if(!ByName.ContainsKey(Dependency)){
                            EndContext.ReportDiagnostic(Diagnostic.Create(MissingDependency,SceneJsonDslAnalyzer.Locate(Entry.Path,Entry.Text,Start,End),Entry.Manifest.Name,Dependency));
                        }
                    }
                }
                if(FindCycle(ByName) is { }Cycle){
                    var Owner=ByName[Cycle[0]];
                    EndContext.ReportDiagnostic(Diagnostic.Create(DependencyCycle,NameLocation(Owner),string.Join(" -> ",Cycle)));
                }
            });
        });
    }

    private static Location NameLocation((PluginManifest Manifest,string Path,SourceText Text)Entry)=>
        SceneJsonDslAnalyzer.Locate(Entry.Path,Entry.Text,Entry.Manifest.NameStart,Entry.Manifest.NameEnd);

    /// <summary>名前順で最初に見つかった依存サイクル。開始地点に戻る名前列（A -> B -> A）、またはnull。</summary>
    private static List<string>?FindCycle(Dictionary<string,(PluginManifest Manifest,string Path,SourceText Text)>ByName){
        Dictionary<string,int>States=new(StringComparer.Ordinal);
        List<string>Path=[];
        foreach(var Name in ByName.Keys.OrderBy(static Name=>Name,StringComparer.Ordinal)){
            if(Visit(Name) is { }Found){ return Found; }
        }
        return null;

        List<string>?Visit(string Name){
            if(States.TryGetValue(Name,out var State)){
                if(State==1){ return [.. Path.Skip(Path.IndexOf(Name)),Name]; }
                return null;
            }
            States[Name]=1;
            Path.Add(Name);
            foreach(var Dependency in ByName[Name].Manifest.Dependencies.Where(ByName.ContainsKey).OrderBy(static D=>D,StringComparer.Ordinal)){
                if(Visit(Dependency) is { }Found){ return Found; }
            }
            Path.RemoveAt(Path.Count-1);
            States[Name]=2;
            return null;
        }
    }
}
