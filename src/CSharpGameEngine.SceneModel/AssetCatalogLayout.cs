using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
namespace CSharpGameEngine.SceneAnalysis;
/// <summary>カタログ出力処理と作成処理のリファクタリング間で共有する命名規約。</summary>
internal static class AssetCatalogLayout {
    internal static Dictionary<string,string>Map(IEnumerable<string>Paths){
        var Root=new Folder("Assets");
        var Result=new Dictionary<string,string>(StringComparer.Ordinal);
        var Ordered=Paths.Distinct(StringComparer.Ordinal).OrderBy(static Value=>Value,StringComparer.Ordinal).ToArray();
        foreach(var PathValue in Ordered){
            var Parts=PathValue.Replace('\\','/').Split('/');
            var Plugin=Parts.Length>3&&Parts[0]=="Plugins"&&Parts[2]=="Assets";
            var Folders=Plugin?new[]{Parts[0],Parts[1]}.Concat(Parts.Skip(3).Take(Parts.Length-4)):Parts.Skip(1).Take(Parts.Length-2);
            var Current=Root;
            foreach(var Part in Folders){ var Id=Identifier(Part);if(!Current.Children.TryGetValue(Id,out var Child)){ Current.Children.Add(Id,Child=new Folder(Id)); }Current=Child; }
        }
        foreach(var PathValue in Ordered){
            var Parts=PathValue.Replace('\\','/').Split('/');
            var Plugin=Parts.Length>3&&Parts[0]=="Plugins"&&Parts[2]=="Assets";
            var Folders=Plugin?new[]{Parts[0],Parts[1]}.Concat(Parts.Skip(3).Take(Parts.Length-4)):Parts.Skip(1).Take(Parts.Length-2);
            var Current=Root;var Name="Assets";
            foreach(var Part in Folders){
                var Id=Identifier(Part);
                if(!Current.Children.TryGetValue(Id,out var Child)){ Current.Children.Add(Id,Child=new Folder(Id)); }
                Current=Child;Name+="."+Id;
            }
            var File=Parts[Parts.Length-1];
            var Stem=Identifier(System.IO.Path.GetFileNameWithoutExtension(File));
            if(Current.Taken(Stem)){ Stem=Identifier(System.IO.Path.GetFileNameWithoutExtension(File)+"_"+System.IO.Path.GetExtension(File).TrimStart('.')); }
            var Candidate=Stem;var Suffix=2;
            while(Current.Taken(Candidate)){ Candidate=Stem+"_"+Suffix++; }
            Current.Names.Add(Candidate);Result.Add(PathValue,Name+"."+Candidate);
        }
        return Result;
    }
    internal static string Identifier(string Raw){
        var Builder=new StringBuilder();foreach(var Character in Raw){ Builder.Append(char.IsLetterOrDigit(Character)||Character=='_'?Character:'_'); }
        if(Builder.Length==0||char.IsDigit(Builder[0])){ Builder.Insert(0,'_'); }
        var Text=Builder.ToString();return SyntaxFacts.GetKeywordKind(Text)!=SyntaxKind.None?"@"+Text:Text;
    }
    private sealed class Folder(string Name){
        internal string Name{get;}=Name;
        internal Dictionary<string,Folder>Children{get;}=new(StringComparer.Ordinal);
        internal HashSet<string>Names{get;}=new(StringComparer.Ordinal);
        internal bool Taken(string Name)=>this.Names.Contains(Name)||this.Children.ContainsKey(Name);
    }
}
