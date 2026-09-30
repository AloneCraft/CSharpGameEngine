using System;
using System.Collections.Generic;
namespace CSharpGameEngine.SceneAnalysis.Json;
internal enum SceneJsonKind { Object,Array,String,Number,True,False,Null }
/// <summary>
/// One parsed JSON value of a <c>*.scene.json</c> document (ADR-0070). The DOM is dependency-free so
/// the same code runs in the source generator (netstandard2.0), the VSIX (net472) and the tools.
/// Every value keeps its source span and numbers keep their original text, so the generator emits
/// the author's literal unchanged and an edit can replace exactly one value's text.
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
    /// <summary>Offset of the first character of the value in the document text.</summary>
    internal int Start { get; }
    /// <summary>Offset just past the last character of the value.</summary>
    internal int End { get; }
    /// <summary>The unescaped string, or a number's original text.</summary>
    internal string?Text { get; }
    /// <summary>Object properties in document order (duplicates are rejected by the reader).</summary>
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
/// <summary>A malformed document; <see cref="Position"/> is the offending character offset.</summary>
internal sealed class SceneJsonException:Exception {
    internal SceneJsonException(string Message,int Position):base(Message){
        this.Position=Position;
    }
    internal int Position { get; }
}
