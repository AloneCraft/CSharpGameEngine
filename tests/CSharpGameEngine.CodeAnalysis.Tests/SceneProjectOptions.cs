using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
namespace CSharpGameEngine.CodeAnalysis.Tests;
/// <summary>
/// アナライザーとジェネレーターのテストで使う、ビルドの <c>ProjectDir</c>。Scene は <see cref="ProjectDirectory"/> の <c>Scenes</c>
/// フォルダー以下にある <c>*.json</c>（ADR-0070、<c>SceneFiles</c>）。
/// </summary>
internal sealed class SceneProjectOptions:AnalyzerConfigOptionsProvider {
    internal const string ProjectDirectory=@"C:\Game\";
    internal const string ScenesDirectory=ProjectDirectory+@"Scenes\";
    /// <summary>有効化されたエンジンプラグイン Vehicles（ADR-0079）。実際のビルドと同様、プロジェクトフォルダーの外にある。</summary>
    internal const string EnginePluginDirectory=@"C:\Engine\plugins\CSharpGameEngine.Vehicles\";
    internal const string EngineVersion="0.1.16";
    internal static readonly SceneProjectOptions Instance=new(EngineVersion);
    private readonly Options Global;
    private SceneProjectOptions(string Version){
        this.Global=new(new(){
            ["build_property.ProjectDir"]=ProjectDirectory,
            ["build_property.CgePluginRootsEncoded"]="|Vehicles="+EnginePluginDirectory,
            ["build_property.CgeEngineVersion"]=Version,
        });
    }
    private static readonly Options Empty=new([]);
    public override AnalyzerConfigOptions GlobalOptions=>this.Global;
    public override AnalyzerConfigOptions GetOptions(SyntaxTree Tree)=>Empty;
    public override AnalyzerConfigOptions GetOptions(AdditionalText TextFile)=>Empty;
    internal static AnalyzerOptions Analyzer(System.Collections.Immutable.ImmutableArray<AdditionalText>AdditionalFiles)=>new(AdditionalFiles,Instance);
    /// <summary>エンジンの版だけを差し替えたオプション（プレリリースや読めない版の試験用）。</summary>
    internal static AnalyzerOptions Analyzer(System.Collections.Immutable.ImmutableArray<AdditionalText>AdditionalFiles,string Version)=>new(AdditionalFiles,new SceneProjectOptions(Version));
    private sealed class Options(Dictionary<string,string>Values):AnalyzerConfigOptions {
        public override bool TryGetValue(string Key,[NotNullWhen(true)]out string?Value)=>Values.TryGetValue(Key,out Value);
    }
}
