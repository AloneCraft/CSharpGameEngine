using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// 結果値と診断をまとめて<c>IncrementalValuesProvider</c>へ流すための対。<see cref="SceneEmitModel"/>と
/// 同じ理由(<see cref="SceneEmitEquality"/>参照)で値等価性を実装する — 既定の構造体等価性は
/// <c>ImmutableArray&lt;Diagnostic&gt;</c>の内部配列を参照比較してしまい、インクリメンタルキャッシュが
/// 常時ミスし続ける。
/// </summary>
internal readonly struct SceneJsonAnalysisResult : IEquatable<SceneJsonAnalysisResult> {
    internal SceneJsonAnalysisResult(SceneEmitModel?Model,ImmutableArray<Diagnostic>Diagnostics){
        this.Model=Model;
        this.Diagnostics=Diagnostics;
    }

    internal SceneEmitModel?Model { get; }

    internal ImmutableArray<Diagnostic>Diagnostics { get; }

    public bool Equals(SceneJsonAnalysisResult Other){
        return Nullable.Equals(this.Model,Other.Model)
            &&SceneEmitEquality.SequenceEqual(
                this.Diagnostics.Select(static D=>D.ToString()).ToImmutableArray(),
                Other.Diagnostics.Select(static D=>D.ToString()).ToImmutableArray());
    }

    public override bool Equals(object?Obj)=>Obj is SceneJsonAnalysisResult Other&&this.Equals(Other);

    public override int GetHashCode()=>SceneEmitEquality.Combine(
        this.Model?.GetHashCode()??0,
        SceneEmitEquality.CombineHashCodes(this.Diagnostics.Select(static D=>D.ToString()).ToImmutableArray()));
}

/// <summary>
/// M0049: <c>*.scene.json</c>を<see cref="SceneEmitModel"/>へ変換する。<see cref="SceneValueReader"/>が
/// C#構文+SemanticModelで行っていた型・enum・値の解決を、JSON+<see cref="Compilation"/>で再実装する。
///
/// <para>
/// <b>値の解釈は対象フィールド/プロパティの宣言型で決める</b>(<see cref="SceneJsonModel"/>のドキュメント
/// 参照)。JSON側にタグは持たせない — enumなら文字列をメンバー名として解決し、それ以外はJSONネイティブ
/// 型をそのまま使う。
/// </para>
/// </summary>
internal static class SceneJsonAnalyzer {
    private static readonly JsonSerializerOptions JsonOptions=new(){ PropertyNameCaseInsensitive=true };

    internal static SceneJsonAnalysisResult Analyze(
        AdditionalText JsonFile,Compilation Compilation,CancellationToken CancellationToken){
        ImmutableArray<Diagnostic>.Builder Diagnostics=ImmutableArray.CreateBuilder<Diagnostic>();
        SourceText?Text=JsonFile.GetText(CancellationToken);
        if(Text is null){
            return new SceneJsonAnalysisResult(null,Diagnostics.ToImmutable());
        }

        SceneJsonDocument?Document;
        try {
            Document=JsonSerializer.Deserialize<SceneJsonDocument>(Text.ToString(),JsonOptions);
        } catch(JsonException Ex){
            Diagnostics.Add(Diagnostic.Create(SceneDslDiagnostics.InvalidSceneJson,FileLocation(JsonFile),Ex.Message));
            return new SceneJsonAnalysisResult(null,Diagnostics.ToImmutable());
        }

        if(Document is null||string.IsNullOrWhiteSpace(Document.ClassName)){
            Diagnostics.Add(Diagnostic.Create(
                SceneDslDiagnostics.InvalidSceneJson,FileLocation(JsonFile),"'className' is required."));
            return new SceneJsonAnalysisResult(null,Diagnostics.ToImmutable());
        }

        if(Document.Root is null){
            Diagnostics.Add(Diagnostic.Create(
                SceneDslDiagnostics.InvalidSceneJson,FileLocation(JsonFile),"'root' is required."));
            return new SceneJsonAnalysisResult(null,Diagnostics.ToImmutable());
        }

        var HasErrors=false;
        SceneEmitNode Root=BuildNode(
            Document.Root,ImmutableArray<int>.Empty,IsRoot:true,Compilation,JsonFile,Diagnostics,ref HasErrors);

        string?Namespace=string.IsNullOrWhiteSpace(Document.Namespace)?null:Document.Namespace;
        string ClassFullyQualifiedName=Namespace is null?Document.ClassName:$"{Namespace}.{Document.ClassName}";
        var Model=new SceneEmitModel(
            Namespace,
            Document.ClassName,
            ClassFullyQualifiedName,
            Root,
            CountNodes(Root),
            IsStartupScene:Document.StartupOrder is not null,
            StartupOrder:Document.StartupOrder??0,
            HasErrors,
            JsonFile.Path,
            DeclarationPosition:0);
        return new SceneJsonAnalysisResult(Model,Diagnostics.ToImmutable());
    }

    private static int CountNodes(SceneEmitNode Node){
        var Count=1;
        foreach(SceneEmitNode Child in Node.Children){
            Count+=CountNodes(Child);
        }
        return Count;
    }

    private static SceneEmitNode BuildNode(
        SceneJsonNode Json,ImmutableArray<int>Path,bool IsRoot,Compilation Compilation,AdditionalText File,
        ImmutableArray<Diagnostic>.Builder Diagnostics,ref bool HasErrors){
        string LocalName=Path.IsEmpty?"Node":"Node_"+string.Join("_",Path);
        string PathDisplay=Path.IsEmpty?"(root)":string.Join(".",Path);

        ImmutableArray<SceneEmitComponent>.Builder Components=
            ImmutableArray.CreateBuilder<SceneEmitComponent>(Json.Components?.Count??0);
        HashSet<string>SeenTypes=new(StringComparer.Ordinal);
        foreach(SceneJsonComponent JsonComponent in Json.Components??Enumerable.Empty<SceneJsonComponent>()){
            if(!TryBuildComponent(JsonComponent,Compilation,File,Diagnostics,out SceneEmitComponent Component)){
                HasErrors=true;
                continue;
            }
            if(!SeenTypes.Add(Component.TypeFullyQualifiedName)){
                Diagnostics.Add(Diagnostic.Create(
                    SceneDslDiagnostics.DuplicateComponentType,FileLocation(File),PathDisplay,Component.TypeFullyQualifiedName));
                HasErrors=true;
                continue;
            }
            Components.Add(Component);
        }

        ImmutableArray<SceneEmitNode>.Builder Children=
            ImmutableArray.CreateBuilder<SceneEmitNode>(Json.Children?.Count??0);
        var ChildIndex=0;
        foreach(SceneJsonNode Child in Json.Children??Enumerable.Empty<SceneJsonNode>()){
            Children.Add(BuildNode(Child,Path.Add(ChildIndex),IsRoot:false,Compilation,File,Diagnostics,ref HasErrors));
            ChildIndex++;
        }

        return new SceneEmitNode(LocalName,PathDisplay,Json.Name,Components.ToImmutable(),Children.ToImmutable());
    }

    private static bool TryBuildComponent(
        SceneJsonComponent Json,Compilation Compilation,AdditionalText File,
        ImmutableArray<Diagnostic>.Builder Diagnostics,out SceneEmitComponent Component){
        Component=default;
        if(string.IsNullOrWhiteSpace(Json.Type)){
            Diagnostics.Add(Diagnostic.Create(
                SceneDslDiagnostics.InvalidSceneJson,FileLocation(File),"Component 'type' is required."));
            return false;
        }

        INamedTypeSymbol?TypeSymbol=Compilation.GetTypeByMetadataName(Json.Type);
        if(TypeSymbol is null||!SceneTypePredicates.IsNonNullableStruct(TypeSymbol)){
            Diagnostics.Add(Diagnostic.Create(SceneDslDiagnostics.ComponentMustBeStructCreation,FileLocation(File),Json.Type));
            return false;
        }

        string TypeFullyQualifiedName=SceneTypePredicates.ToFullyQualifiedName(TypeSymbol);
        if(SceneTypePredicates.IsReservedComponent(TypeFullyQualifiedName)){
            Diagnostics.Add(Diagnostic.Create(SceneDslDiagnostics.ReservedComponentType,FileLocation(File),TypeFullyQualifiedName));
            return false;
        }

        List<string>MemberAssignments=[];
        var Succeeded=true;
        foreach(KeyValuePair<string,JsonElement>Member in Json.Members??
            new Dictionary<string,JsonElement>()){
            if(!TryResolveMember(TypeSymbol,Member.Key,out ITypeSymbol?MemberType)){
                Diagnostics.Add(Diagnostic.Create(
                    SceneDslDiagnostics.UnknownJsonMember,FileLocation(File),Member.Key,TypeFullyQualifiedName));
                Succeeded=false;
                continue;
            }

            if(!TryBuildValueExpression(MemberType!,Member.Value,File,Diagnostics,out string?ValueExpression)){
                Succeeded=false;
                continue;
            }

            MemberAssignments.Add(SceneEmitExpressionWriter.WriteMemberAssignment(Member.Key,ValueExpression!));
        }

        if(!Succeeded){
            return false;
        }

        string EmitExpressionText=SceneEmitExpressionWriter.WriteObjectCreation(
            TypeFullyQualifiedName,Arguments:[],MemberAssignments);
        Component=new SceneEmitComponent(TypeFullyQualifiedName,EmitExpressionText);
        return true;
    }

    /// <summary>Componentの型からJSONメンバー名に対応する公開フィールド/プロパティの宣言型を引く。</summary>
    private static bool TryResolveMember(INamedTypeSymbol TypeSymbol,string MemberName,out ITypeSymbol?MemberType){
        foreach(ISymbol Candidate in TypeSymbol.GetMembers(MemberName)){
            switch(Candidate){
                case IFieldSymbol { DeclaredAccessibility:Accessibility.Public,IsConst:false,IsReadOnly:false }Field:
                    MemberType=Field.Type;
                    return true;
                case IPropertySymbol { DeclaredAccessibility:Accessibility.Public,IsReadOnly:false }Property:
                    MemberType=Property.Type;
                    return true;
            }
        }

        MemberType=null;
        return false;
    }

    /// <summary>
    /// JSON値の解釈は対象フィールド/プロパティの宣言型で決める(JSON側にタグは持たせない)。
    /// enumなら文字列をメンバー名として<c>Compilation</c>から解決し、それ以外はJSONネイティブ型を
    /// そのままC#リテラルへ変換する。
    /// </summary>
    private static bool TryBuildValueExpression(
        ITypeSymbol MemberType,JsonElement Value,AdditionalText File,
        ImmutableArray<Diagnostic>.Builder Diagnostics,out string?ValueExpression){
        ValueExpression=null;

        if(MemberType.TypeKind==TypeKind.Enum){
            if(Value.ValueKind!=JsonValueKind.String){
                Diagnostics.Add(Diagnostic.Create(
                    SceneDslDiagnostics.JsonValueTypeMismatch,FileLocation(File),
                    SceneTypePredicates.ToFullyQualifiedName(MemberType),"string (enum member name)"));
                return false;
            }

            string MemberName=Value.GetString()??"";
            ISymbol?EnumField=MemberType.GetMembers(MemberName)
                .FirstOrDefault(static Candidate=>Candidate is IFieldSymbol { IsConst:true });
            if(EnumField is null){
                Diagnostics.Add(Diagnostic.Create(
                    SceneDslDiagnostics.UnknownEnumMember,FileLocation(File),MemberName,
                    SceneTypePredicates.ToFullyQualifiedName(MemberType)));
                return false;
            }

            ValueExpression=SceneEmitExpressionWriter.WriteSymbolReference(EnumField);
            return true;
        }

        switch(MemberType.SpecialType){
            case SpecialType.System_String:
                if(Value.ValueKind!=JsonValueKind.String){
                    return Mismatch(MemberType,"string",File,Diagnostics,out ValueExpression);
                }
                ValueExpression=SceneEmitExpressionWriter.WriteConstant(Value.GetString());
                return true;
            case SpecialType.System_Boolean:
                if(Value.ValueKind is not(JsonValueKind.True or JsonValueKind.False)){
                    return Mismatch(MemberType,"bool",File,Diagnostics,out ValueExpression);
                }
                ValueExpression=SceneEmitExpressionWriter.WriteConstant(Value.GetBoolean());
                return true;
            case SpecialType.System_Single:
                return TryNumber(MemberType,Value,File,Diagnostics,out ValueExpression,
                    static El=>SceneEmitExpressionWriter.WriteConstant(El.GetSingle()));
            case SpecialType.System_Double:
                return TryNumber(MemberType,Value,File,Diagnostics,out ValueExpression,
                    static El=>SceneEmitExpressionWriter.WriteConstant(El.GetDouble()));
            case SpecialType.System_Int32:
                return TryNumber(MemberType,Value,File,Diagnostics,out ValueExpression,
                    static El=>SceneEmitExpressionWriter.WriteConstant(El.GetInt32()));
            case SpecialType.System_Int64:
                return TryNumber(MemberType,Value,File,Diagnostics,out ValueExpression,
                    static El=>SceneEmitExpressionWriter.WriteConstant(El.GetInt64()));
            default:
                Diagnostics.Add(Diagnostic.Create(
                    SceneDslDiagnostics.UnsupportedJsonMemberType,FileLocation(File),
                    SceneTypePredicates.ToFullyQualifiedName(MemberType)));
                return false;
        }
    }

    private static bool TryNumber(
        ITypeSymbol MemberType,JsonElement Value,AdditionalText File,ImmutableArray<Diagnostic>.Builder Diagnostics,
        out string?ValueExpression,Func<JsonElement,string>Format){
        if(Value.ValueKind!=JsonValueKind.Number){
            return Mismatch(MemberType,"number",File,Diagnostics,out ValueExpression);
        }
        ValueExpression=Format(Value);
        return true;
    }

    private static bool Mismatch(
        ITypeSymbol MemberType,string ExpectedJsonKind,AdditionalText File,
        ImmutableArray<Diagnostic>.Builder Diagnostics,out string?ValueExpression){
        ValueExpression=null;
        Diagnostics.Add(Diagnostic.Create(
            SceneDslDiagnostics.JsonValueTypeMismatch,FileLocation(File),
            SceneTypePredicates.ToFullyQualifiedName(MemberType),ExpectedJsonKind));
        return false;
    }

    private static Location FileLocation(AdditionalText File)=>
        Location.Create(File.Path,default,default);
}
