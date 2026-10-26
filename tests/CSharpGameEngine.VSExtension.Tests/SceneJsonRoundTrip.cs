using System.Linq;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Json;
using Microsoft.CodeAnalysis;
namespace CSharpGameEngine.VSExtension.Tests;
/// <summary>
/// 予測モデルが一致すべき正解 (ADR-0070)：コミットと同じようにモデルを Scene JSON として書き出し（<see cref="SceneJsonConverter"/>）、デザイナービューを通して読み戻す（<see cref="SceneJsonCommitPlanner.TryAnalyze"/>）。テストでは C# シーン DSL で元のシーンを作成するため、コンバーターと同じように最初に <paramref name="Compilation"/> からシーンクラスを除去する。
/// </summary>
internal static class SceneJsonRoundTrip {
    internal static SceneModel Reanalyze(SceneModel Predicted,Compilation Compilation)=>ToJsonScene(Predicted,Compilation).Context.Model;
    /// <summary>
    /// シーン JSON ドキュメントとしての <paramref name="Model"/> と、Scene Designer が構築するコミットコンテキスト。<c>Base</c> は C# シーンクラスを除いた <paramref name="Compilation"/>。
    /// </summary>
    internal static (string Json,SceneSourceCommitContext Context,Compilation Base)ToJsonScene(SceneModel Model,Compilation Compilation){
        var Converted=SceneJsonConverter.Convert(Model,null);
        Assert.True(Converted.Text is not null,string.Join("\n",Converted.Problems));
        var Scene=Compilation.GetTypeByMetadataName(Model.ClassFullyQualifiedName);
        var Base=Scene is null?Compilation:Compilation.RemoveSyntaxTrees(Scene.DeclaringSyntaxReferences.Select(static Reference=>Reference.SyntaxTree).Distinct());
        var Json=Converted.Text!;
        return (Json,Analyze(Json,Model.ClassName,Model.ClassFullyQualifiedName,Base),Base);
    }
    internal static SceneSourceCommitContext Analyze(string Json,string ClassName,string ClassFullyQualifiedName,Compilation Base){
        Assert.True(SceneJsonCommitPlanner.TryAnalyze(Json,@"C:\Game\Scenes\"+ClassName+".json",Base,
            ClassFullyQualifiedName,CancellationToken.None,out var Model,out var Root,out var AnalysisCompilation,out var Error),Error);
        return new SceneSourceCommitContext(Model!,Root!,Microsoft.CodeAnalysis.Text.SourceText.From(Json),AnalysisCompilation!);
    }
    /// <summary>
    /// 表記を除いた値の意味：JSON は、C# が <c>25f</c> と書く値を <c>25</c> と書き、コンストラクター引数にも名前を付ける。そのため往復でソーステキストを比較できない。
    /// </summary>
    internal static string Signature(SceneValue Value)=>Value.Kind switch {
        SceneValueKind.ObjectCreation=>(Value.Type?.ToDisplayString()??"?")
            +"("+string.Join(",",Value.Arguments.Select(Signature))+")"
            +"{"+string.Join(",",Value.Members.Select(static Member=>Member.MemberName+"="+Signature(Member.Value)))+"}",
        SceneValueKind.Collection=>"["+string.Join(",",Value.Elements.Select(Signature))+"]",
        SceneValueKind.Default=>Value.Type switch {
            { SpecialType:SpecialType.System_Boolean }=>"False",
            { TypeKind:TypeKind.Enum }Enum=>Enum.GetMembers().OfType<IFieldSymbol>().First(static Field=>Field.HasConstantValue&&System.Convert.ToString(Field.ConstantValue,System.Globalization.CultureInfo.InvariantCulture)=="0").Name,
            { IsValueType:true,SpecialType:not SpecialType.None }=>"0",
            { IsValueType:true }Struct=>Struct.ToDisplayString()+"(){}",
            _=>string.Empty,
        },
        _=>System.Convert.ToString(Value.ConstantValue,System.Globalization.CultureInfo.InvariantCulture)
            ??Value.ReferencedSymbol?.Name??Value.Kind.ToString(),
    };
}
