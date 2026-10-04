using System.Linq;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Json;
using Microsoft.CodeAnalysis;
namespace CSharpGameEngine.VSExtension.Tests;
/// <summary>
/// The truth a predicted model must match (ADR-0070): the model is written as scene JSON exactly as a
/// commit writes it (<see cref="SceneJsonConverter"/>) and read back through the designer view
/// (<see cref="SceneJsonCommitPlanner.TryAnalyze"/>). Tests author their base scene in the C# scene DSL,
/// so the scene class is removed from <paramref name="Compilation"/> first, as the converter does.
/// </summary>
internal static class SceneJsonRoundTrip {
    internal static SceneModel Reanalyze(SceneModel Predicted,Compilation Compilation)=>ToJsonScene(Predicted,Compilation).Context.Model;
    /// <summary>
    /// <paramref name="Model"/> as a scene JSON document and the commit context the Scene Designer would
    /// build for it; <c>Base</c> is <paramref name="Compilation"/> without the C# scene class.
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
    /// A value's meaning without its spelling: JSON writes <c>25</c> where C# wrote <c>25f</c> and names
    /// constructor arguments, so source text cannot be compared across the round trip.
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
