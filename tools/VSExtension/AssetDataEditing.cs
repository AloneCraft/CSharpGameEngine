using System;
using System.Collections.Generic;
using System.Linq;
using CSharpGameEngine.SceneAnalysis.Json;
using Microsoft.CodeAnalysis;
namespace CSharpGameEngine.VSExtension;
internal sealed record AssetDataRow(string Name,int Start,int Length,string Json,SceneJsonKind Kind);
internal static class AssetDataEditing {
    internal static INamedTypeSymbol[]Types(Compilation Compilation){
        var Types=new List<INamedTypeSymbol>();
        void Visit(INamespaceOrTypeSymbol Container){
            foreach(var Member in Container.GetMembers()){
                if(Member is INamespaceOrTypeSymbol Child){
                    if(Child is INamedTypeSymbol Type&&!Type.IsGenericType&&Type.GetAttributes().Any(static Attribute=>Attribute.AttributeClass?.ToDisplayString()=="CSharpGameEngine.Authoring.DataAssetAttribute")){ Types.Add(Type); }
                    Visit(Child);
                }
            }
        }
        Visit(Compilation.Assembly.GlobalNamespace);
        foreach(var Assembly in Compilation.SourceModule.ReferencedAssemblySymbols.Where(static Assembly=>!Assembly.Name.StartsWith("System",StringComparison.Ordinal)&&!Assembly.Name.StartsWith("Microsoft",StringComparison.Ordinal)&&Assembly.Name is not("mscorlib" or "netstandard"))){ Visit(Assembly.GlobalNamespace); }
        return Types.OrderBy(static Type=>Type.ToDisplayString(),StringComparer.Ordinal).ToArray();
    }
    internal static string[]EnumChoices(string Text,AssetDataRow Row,Compilation Compilation){
        var TypeName=SceneJsonReader.Parse(Text).Get("type")?.Text;
        ITypeSymbol?Type=TypeName is null?null:Compilation.GetTypeByMetadataName(TypeName)??Types(Compilation).FirstOrDefault(Type=>Type.ToDisplayString()==TypeName);
        foreach(System.Text.RegularExpressions.Match Part in System.Text.RegularExpressions.Regex.Matches(Row.Name,@"[^.\[\]]+|\[\d+\]")){
            if(Part.Value.StartsWith("[",StringComparison.Ordinal)){ Type=Type is IArrayTypeSymbol Array?Array.ElementType:Type is INamedTypeSymbol Generic&&Generic.TypeArguments.Length==1?Generic.TypeArguments[0]:null;continue; }
            var Member=Type?.GetMembers().FirstOrDefault(Member=>Member.Name==Part.Value||SceneJsonSceneAnalyzer.KeyOf(Member)==Part.Value);
            Type=Member switch{IFieldSymbol Field=>Field.Type,IPropertySymbol Property=>Property.Type,_=>null};
        }
        return Type?.TypeKind==TypeKind.Enum?Type.GetMembers().OfType<IFieldSymbol>().Where(static Field=>Field.HasConstantValue).Select(static Field=>Field.Name).ToArray():[];
    }
    internal static IReadOnlyList<AssetDataRow>Rows(string Text,Compilation Compilation){
        Validate(Text,Compilation);
        var Root=SceneJsonReader.Parse(Text);
        var Result=new List<AssetDataRow>();
        if(Root.Get("members") is { }Members){ Visit(Members,string.Empty,Text,Result); }
        return Result;
    }
    private static void Visit(SceneJsonValue Value,string Name,string Text,List<AssetDataRow>Rows){
        if(Value.Kind==SceneJsonKind.Object){ foreach(var Property in Value.Properties){ Visit(Property.Value,string.IsNullOrEmpty(Name)?Property.Name:Name+"."+Property.Name,Text,Rows); }return; }
        Rows.Add(new(Name,Value.Start,Value.End-Value.Start,Text.Substring(Value.Start,Value.End-Value.Start),Value.Kind));
        if(Value.Kind==SceneJsonKind.Array){ for(var Index=0;Index<Value.Items.Count;Index++){ Visit(Value.Items[Index],Name+"["+Index+"]",Text,Rows); } }
    }
    internal static string Edit(string Before,AssetDataRow Row,string NewJson,Compilation Compilation){
        _=SceneJsonReader.Parse(NewJson);
        if(Row.Start<0||Row.Start+Row.Length>Before.Length||Before.Substring(Row.Start,Row.Length)!=Row.Json){ throw new InvalidOperationException("ASSET_DOCUMENT_CONFLICT"); }
        var After=Before.Substring(0,Row.Start)+NewJson+Before.Substring(Row.Start+Row.Length);
        Validate(After,Compilation);
        return After;
    }
    internal static void Validate(string Text,Compilation Compilation){
        if(SceneJsonSceneAnalyzer.AnalyzeDataAsset(Text,Compilation,out var Problems) is null){
            throw new InvalidOperationException(string.Join(Environment.NewLine,Problems.Select(static Problem=>
                Problem.Descriptor.Id+": "+string.Format(System.Globalization.CultureInfo.InvariantCulture,Problem.Descriptor.MessageFormat.ToString(),Problem.Arguments))));
        }
    }
}
