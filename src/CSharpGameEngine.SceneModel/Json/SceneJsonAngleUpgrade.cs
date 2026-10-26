using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
namespace CSharpGameEngine.SceneAnalysis.Json;
/// <summary>
/// ADR-0074：スキーマバージョン1または2の Scene ドキュメントを、現在のバージョン3へ書き換える。バージョン1の角度の数値は回転数に変換し、C# の単位名を含む角度キーは単位名を含まない
/// キーに変え、<c>schemaVersion</c> を3にする。それ以外の文字列はすべてそのまま残す。変換後に再読込し、元と同じコンポーネント値になることを確認するため、更新によって Scene が
/// 変わることはない。
/// </summary>
internal static class SceneJsonAngleUpgrade {
    internal sealed class Result {
        internal Result(string?Text,bool Changed,IReadOnlyList<string>Problems){
            this.Text=Text;
            this.Changed=Changed;
            this.Problems=Problems;
        }
        /// <summary>更新後のテキスト。すでに現行バージョンなら元のテキストを返し、問題があれば <c>null</c> を返す。</summary>
        internal string?Text { get; }
        internal bool Changed { get; }
        internal IReadOnlyList<string>Problems { get; }
    }
    internal static Result Upgrade(string Text,Compilation Compilation){
        var Before=SceneJsonSceneAnalyzer.Analyze(Text,Compilation);
        if(!Before.Problems.IsEmpty||Before.Root is null){
            return new Result(null,false,[.. Before.Problems.Select(Describe)]);
        }
        if(Before.SchemaVersion>=SceneJsonSceneAnalyzer.SchemaVersion){ return new Result(Text,false,[]); }
        var Version=SceneJsonReader.Parse(Text).Get("schemaVersion")!;
        List<(int Start,int End,string Text)>Edits=[(Version.Start,Version.End,SceneJsonSceneAnalyzer.SchemaVersion.ToString(CultureInfo.InvariantCulture))];
        if(Before.SchemaVersion<SceneJsonAngles.TurnsSchemaVersion){
            foreach(var Angle in Before.AngleValues){
                var Value=float.Parse(Angle.Text,NumberStyles.Float,CultureInfo.InvariantCulture);
                Edits.Add((Angle.Start,Angle.End,SceneJsonAngles.FormatTurns(Value,Angle.Unit)));
            }
        }
        foreach(var Rename in Before.KeyRenames){
            if(!string.Equals(Text.Substring(Rename.Start,Rename.End-Rename.Start),"\""+Rename.Name+"\"",StringComparison.Ordinal)){
                return new Result(null,false,[$"the key '{Rename.Name}' at {Rename.Start.ToString(CultureInfo.InvariantCulture)} is escaped; rename it to '{Rename.Key}' by hand."]);
            }
            Edits.Add((Rename.Start,Rename.End,"\""+Rename.Key+"\""));
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
