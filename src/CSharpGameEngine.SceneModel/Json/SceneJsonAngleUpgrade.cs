using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
namespace CSharpGameEngine.SceneAnalysis.Json;
/// <summary>
/// ADR-0074: rewrites a schema version 1 scene document as version 2. Each number an angle member holds becomes
/// turns and <c>schemaVersion</c> becomes 2; every other character of the author's text stays. The result is read
/// back and must give the same component values as the original, so an upgrade never changes the scene.
/// </summary>
internal static class SceneJsonAngleUpgrade {
    internal sealed class Result {
        internal Result(string?Text,bool Changed,IReadOnlyList<string>Problems){
            this.Text=Text;
            this.Changed=Changed;
            this.Problems=Problems;
        }
        /// <summary>The upgraded text (the original when it is already version 2), or <c>null</c> on a problem.</summary>
        internal string?Text { get; }
        internal bool Changed { get; }
        internal IReadOnlyList<string>Problems { get; }
    }
    internal static Result Upgrade(string Text,Compilation Compilation){
        var Before=SceneJsonSceneAnalyzer.Analyze(Text,Compilation);
        if(!Before.Problems.IsEmpty||Before.Root is null){
            return new Result(null,false,[.. Before.Problems.Select(Describe)]);
        }
        if(Before.SchemaVersion>=SceneJsonAngles.TurnsSchemaVersion){ return new Result(Text,false,[]); }
        var Version=SceneJsonReader.Parse(Text).Get("schemaVersion")!;
        List<(int Start,int End,string Text)>Edits=[(Version.Start,Version.End,SceneJsonAngles.TurnsSchemaVersion.ToString(CultureInfo.InvariantCulture))];
        foreach(var Angle in Before.AngleValues){
            var Value=float.Parse(Angle.Text,NumberStyles.Float,CultureInfo.InvariantCulture);
            Edits.Add((Angle.Start,Angle.End,SceneJsonAngles.FormatTurns(Value,Angle.Unit)));
        }
        var Builder=new StringBuilder(Text);
        foreach(var Edit in Edits.OrderByDescending(static Edit=>Edit.Start)){
            Builder.Remove(Edit.Start,Edit.End-Edit.Start);
            Builder.Insert(Edit.Start,Edit.Text);
        }
        var Upgraded=Builder.ToString();
        var After=SceneJsonSceneAnalyzer.Analyze(Upgraded,Compilation);
        if(!After.Problems.IsEmpty||After.Root is null){
            return new Result(null,false,[.. After.Problems.Select(Describe)]);
        }
        if(FirstDifference(Before.Root,After.Root) is { }Difference){
            return new Result(null,false,[$"the upgraded document would change {Difference}; it was left as it is."]);
        }
        return new Result(Upgraded,true,[]);
    }
    private static string Describe(SceneJsonProblem Problem)=>
        $"{Problem.Descriptor.Id} at {Problem.Start.ToString(CultureInfo.InvariantCulture)}: {string.Format(CultureInfo.InvariantCulture,Problem.Descriptor.MessageFormat.ToString(CultureInfo.InvariantCulture),Problem.Arguments)}";
    private static string?FirstDifference(SceneJsonNode Before,SceneJsonNode After){
        if(Before.Components.Length!=After.Components.Length||Before.Children.Length!=After.Children.Length){ return "the node structure"; }
        for(var Index=0;Index<Before.Components.Length;Index++){
            if(!string.Equals(Before.Components[Index].EmitExpressionText,After.Components[Index].EmitExpressionText,StringComparison.Ordinal)){
                return $"{Before.Components[Index].TypeFullyQualifiedName} from {Before.Components[Index].EmitExpressionText} to {After.Components[Index].EmitExpressionText}";
            }
        }
        for(var Index=0;Index<Before.Children.Length;Index++){
            if(FirstDifference(Before.Children[Index],After.Children[Index]) is { }Difference){ return Difference; }
        }
        return null;
    }
}
