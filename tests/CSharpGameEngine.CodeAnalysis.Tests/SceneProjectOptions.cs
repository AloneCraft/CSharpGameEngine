using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
namespace CSharpGameEngine.CodeAnalysis.Tests;
/// <summary>
/// The build's <c>ProjectDir</c> for generator and analyzer tests: scenes are the <c>*.json</c> files
/// under <see cref="ProjectDirectory"/>'s <c>Scenes</c> folder (ADR-0070, <c>SceneFiles</c>).
/// </summary>
internal sealed class SceneProjectOptions:AnalyzerConfigOptionsProvider {
    internal const string ProjectDirectory=@"C:\Game\";
    internal const string ScenesDirectory=ProjectDirectory+@"Scenes\";
    internal static readonly SceneProjectOptions Instance=new();
    private static readonly Options Global=new(new(){ ["build_property.ProjectDir"]=ProjectDirectory });
    private static readonly Options Empty=new([]);
    public override AnalyzerConfigOptions GlobalOptions=>Global;
    public override AnalyzerConfigOptions GetOptions(SyntaxTree Tree)=>Empty;
    public override AnalyzerConfigOptions GetOptions(AdditionalText TextFile)=>Empty;
    internal static AnalyzerOptions Analyzer(System.Collections.Immutable.ImmutableArray<AdditionalText>AdditionalFiles)=>new(AdditionalFiles,Instance);
    private sealed class Options(Dictionary<string,string>Values):AnalyzerConfigOptions {
        public override bool TryGetValue(string Key,[NotNullWhen(true)]out string?Value)=>Values.TryGetValue(Key,out Value);
    }
}
