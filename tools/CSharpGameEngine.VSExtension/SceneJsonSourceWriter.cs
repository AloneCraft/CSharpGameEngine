using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// M0050: 編集後の<see cref="SceneModel"/>を<c>*.scene.json</c>のテキストへ丸ごと再構築する。
///
/// <para>
/// C#の<c>SceneSourceEditor.ApplyForSave</c>(<c>SyntaxAnnotation</c>で対象を追跡する部分差分書き換え)
/// ほどの複雑さは不要 — JSONファイルは数百行規模なので、Modelから毎回全文を再構築しても
/// コストは無視できる。書き戻し先のlive bufferへは<see cref="SceneTextChangePlanner.Plan(
/// Microsoft.CodeAnalysis.Text.SourceText,Microsoft.CodeAnalysis.Text.SourceText)"/>(粗い全文diff)
/// で最小差分化してから渡す(呼び出し元<see cref="SceneEditorSessionService.SaveAsync"/>参照)。
/// </para>
/// </summary>
internal static class SceneJsonSourceWriter {
    private static readonly JsonSerializerOptions Options=new(){ WriteIndented=true };

    internal static string Serialize(SceneModel Model){
        JsonObject Root=new();
        if(Model.Namespace is not null){
            Root["namespace"]=Model.Namespace;
        }
        Root["className"]=Model.ClassName;
        if(Model.IsStartupScene){
            Root["startupOrder"]=Model.StartupOrder;
        }
        if(Model.Root is { } RootNode){
            Root["root"]=SerializeNode(RootNode);
        }

        return Root.ToJsonString(Options)+"\n";
    }

    private static JsonObject SerializeNode(SceneNode Node){
        JsonObject Result=new();
        if(Node.Name is not null){
            Result["name"]=Node.Name;
        }

        if(Node.Components.Length>0){
            JsonArray Components=[];
            foreach(SceneComponent Component in Node.Components){
                Components.Add(SerializeComponent(Component));
            }
            Result["components"]=Components;
        }

        if(Node.Children.Length>0){
            JsonArray Children=[];
            foreach(SceneNode Child in Node.Children){
                Children.Add(SerializeNode(Child));
            }
            Result["children"]=Children;
        }

        return Result;
    }

    private static JsonObject SerializeComponent(SceneComponent Component){
        JsonObject Result=new(){
            ["type"]=StripGlobalPrefix(Component.TypeFullyQualifiedName),
        };

        if(Component.Members.Length>0){
            JsonObject Members=[];
            foreach(SceneComponentMember Member in Component.Members){
                Members[Member.MemberName]=SerializeValue(Member.Value);
            }
            Result["members"]=Members;
        }

        return Result;
    }

    /// <summary>
    /// enumは<see cref="SceneValue.ReferencedSymbol"/>のメンバー名、それ以外は
    /// <see cref="SceneValue.ConstantValue"/>をJSONネイティブ型としてそのまま出す
    /// (<see cref="SceneJsonModelBuilder"/>の読み込みと対称)。
    /// </summary>
    private static JsonNode?SerializeValue(SceneValue Value){
        if(Value.Kind==SceneValueKind.EnumMember){
            return Value.ReferencedSymbol is { } Symbol?JsonValue.Create(Symbol.Name):null;
        }

        return Value.ConstantValue switch {
            null=>null,
            string StringValue=>JsonValue.Create(StringValue),
            bool BoolValue=>JsonValue.Create(BoolValue),
            float FloatValue=>JsonValue.Create(FloatValue),
            double DoubleValue=>JsonValue.Create(DoubleValue),
            int IntValue=>JsonValue.Create(IntValue),
            long LongValue=>JsonValue.Create(LongValue),
            _=>null,
        };
    }

    private static string StripGlobalPrefix(string TypeFullyQualifiedName)=>
        TypeFullyQualifiedName.StartsWith("global::",StringComparison.Ordinal)
            ?TypeFullyQualifiedName.Substring("global::".Length)
            :TypeFullyQualifiedName;
}
