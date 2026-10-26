using System;
using System.Collections.Generic;
namespace CSharpGameEngine.SceneAnalysis.Json;
internal enum SceneJsonKind { Object,Array,String,Number,True,False,Null }
/// <summary>
/// Scene JSON ドキュメントから解析した JSON 値1つ（ADR-0070）。DOM は依存関係を持たず、ソースジェネレーター（netstandard2.0）、VSIX（net472）、ツール
/// で同じコードを実行できる。各値はソース範囲を保持し、数値は元のテキストも保持する。そのためジェネレーターは作成者が書いたリテラルを変更せず出力でき、編集時も対象値のテキストだけを正確に置き換えられる。
/// </summary>
internal sealed class SceneJsonValue {
    private static readonly List<SceneJsonProperty>NoProperties=[];
    private static readonly List<SceneJsonValue>NoItems=[];
    private readonly List<SceneJsonProperty>PropertyList;
    private readonly List<SceneJsonValue>ItemList;
    private SceneJsonValue(SceneJsonKind Kind,int Start,int End,string?Text,List<SceneJsonProperty>?Properties,List<SceneJsonValue>?Items){
        this.Kind=Kind;
        this.Start=Start;
        this.End=End;
        this.Text=Text;
        this.PropertyList=Properties??NoProperties;
        this.ItemList=Items??NoItems;
    }
    internal SceneJsonKind Kind { get; }
    /// <summary>ドキュメントテキストにおける値の先頭文字位置。</summary>
    internal int Start { get; }
    /// <summary>ドキュメントテキストにおける値の末尾文字の直後の位置。</summary>
    internal int End { get; }
    /// <summary>エスケープ解除後の文字列、または数値の元のテキスト。</summary>
    internal string?Text { get; }
    /// <summary>ドキュメント上の順序を保ったオブジェクトプロパティ（重複は読み込み側で拒否する）。</summary>
    internal IReadOnlyList<SceneJsonProperty>Properties=>this.PropertyList;
    internal IReadOnlyList<SceneJsonValue>Items=>this.ItemList;
    internal SceneJsonValue?Get(string Name){
        foreach(var Property in this.PropertyList){
            if(string.Equals(Property.Name,Name,StringComparison.Ordinal)){ return Property.Value; }
        }
        return null;
    }
    internal static SceneJsonValue CreateObject(int Start,int End,List<SceneJsonProperty>Properties)=>new(SceneJsonKind.Object,Start,End,null,Properties,null);
    internal static SceneJsonValue CreateArray(int Start,int End,List<SceneJsonValue>Items)=>new(SceneJsonKind.Array,Start,End,null,null,Items);
    internal static SceneJsonValue CreateScalar(SceneJsonKind Kind,int Start,int End,string?Text)=>new(Kind,Start,End,Text,null,null);
}
internal sealed class SceneJsonProperty {
    internal SceneJsonProperty(string Name,int NameStart,SceneJsonValue Value){
        this.Name=Name;
        this.NameStart=NameStart;
        this.Value=Value;
    }
    internal string Name { get; }
    internal int NameStart { get; }
    internal SceneJsonValue Value { get; }
}
/// <summary>形式が不正なドキュメント。<see cref="Position"/> は問題のある文字位置。</summary>
internal sealed class SceneJsonException:Exception {
    internal SceneJsonException(string Message,int Position):base(Message){
        this.Position=Position;
    }
    internal int Position { get; }
}
