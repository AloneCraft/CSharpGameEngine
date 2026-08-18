using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// M0050: <c>*.scene.json</c>をVSIXが表示・編集する<see cref="SceneModel"/>へ変換する。
/// Runtimeコンパイルパイプライン側の<c>SceneJsonAnalyzer</c>(<c>src/CSharpGameEngine.CodeAnalysis/
/// SceneJsonAnalyzer.cs</c>)と同じ値解釈ロジック(宣言型で決める、enumはメンバー名文字列、それ以外は
/// JSONネイティブ型)を踏襲するが、出力先が文字列専用の<c>SceneEmitModel</c>ではなく、VSIXが描画・編集に
/// 使う<see cref="SceneModel"/>/<see cref="SceneNode"/>/<see cref="SceneComponent"/>/
/// <see cref="SceneComponentMember"/>/<see cref="SceneValue"/>である点が異なる。
///
/// <para>
/// 位置情報(<see cref="SceneSourceLocation"/>)はまだJSON内の正確なテキスト位置を追跡しない
/// (将来Phaseで<c>Utf8JsonReader</c>ベースの追跡を実装するまでは<see cref="SceneSourceLocation.
/// FromJsonFile"/>でファイルパスだけを持たせる)。
/// </para>
/// </summary>
internal static class SceneJsonModelBuilder {
    private static readonly JsonSerializerOptions JsonOptions=new(){ PropertyNameCaseInsensitive=true };

    internal static SceneModel Build(
        string FilePath,SourceText Text,Compilation Compilation,CancellationToken CancellationToken){
        ImmutableArray<Diagnostic>.Builder Diagnostics=ImmutableArray.CreateBuilder<Diagnostic>();

        SceneJsonDocument?Document;
        try {
            Document=JsonSerializer.Deserialize<SceneJsonDocument>(Text.ToString(),JsonOptions);
        } catch(JsonException Ex){
            Diagnostics.Add(Diagnostic.Create(SceneDslDiagnostics.InvalidSceneJson,FileLocation(FilePath),Ex.Message));
            return Failed(null,string.Empty,FilePath,Diagnostics);
        }

        if(Document is null||string.IsNullOrWhiteSpace(Document.ClassName)){
            Diagnostics.Add(Diagnostic.Create(
                SceneDslDiagnostics.InvalidSceneJson,FileLocation(FilePath),"'className' is required."));
            return Failed(null,string.Empty,FilePath,Diagnostics);
        }

        string?Namespace=string.IsNullOrWhiteSpace(Document.Namespace)?null:Document.Namespace;

        if(Document.Root is null){
            Diagnostics.Add(Diagnostic.Create(
                SceneDslDiagnostics.InvalidSceneJson,FileLocation(FilePath),"'root' is required."));
            return Failed(Namespace,Document.ClassName,FilePath,Diagnostics);
        }

        var HasErrors=false;
        SceneNode Root=BuildNode(Document.Root,NodePath.Root,Compilation,FilePath,Diagnostics,ref HasErrors);

        if(HasErrors){
            return Failed(Namespace,Document.ClassName,FilePath,Diagnostics);
        }

        string ClassFullyQualifiedName=Namespace is null?Document.ClassName:$"{Namespace}.{Document.ClassName}";
        INamedTypeSymbol?ClassSymbol=Compilation.GetTypeByMetadataName(ClassFullyQualifiedName);

        return new SceneModel(
            Namespace,
            Document.ClassName,
            ClassFullyQualifiedName,
            ClassSymbol,
            Root,
            IsStartupScene:Document.StartupOrder is not null,
            StartupOrder:Document.StartupOrder??0,
            Diagnostics.ToImmutable(),
            FilePath,
            DeclarationPosition:0);
    }

    private static SceneModel Failed(
        string?Namespace,string ClassName,string FilePath,ImmutableArray<Diagnostic>.Builder Diagnostics){
        string ClassFullyQualifiedName=Namespace is null?ClassName:$"{Namespace}.{ClassName}";
        return SceneModel.Failed(Namespace,ClassName,ClassFullyQualifiedName,null,Diagnostics.ToImmutable(),FilePath,0);
    }

    private static SceneNode BuildNode(
        SceneJsonNode Json,NodePath Path,Compilation Compilation,string FilePath,
        ImmutableArray<Diagnostic>.Builder Diagnostics,ref bool HasErrors){
        SceneSourceLocation Location=SceneSourceLocation.FromJsonFile(FilePath);

        ImmutableArray<SceneComponent>.Builder Components=
            ImmutableArray.CreateBuilder<SceneComponent>(Json.Components?.Count??0);
        HashSet<string>SeenTypes=new(StringComparer.Ordinal);
        foreach(SceneJsonComponent JsonComponent in Json.Components??Enumerable.Empty<SceneJsonComponent>()){
            if(!TryBuildComponent(JsonComponent,Compilation,FilePath,Diagnostics,out SceneComponent?Component)){
                HasErrors=true;
                continue;
            }
            if(!SeenTypes.Add(Component.TypeFullyQualifiedName)){
                Diagnostics.Add(Diagnostic.Create(
                    SceneDslDiagnostics.DuplicateComponentType,FileLocation(FilePath),
                    Path.ToDisplayString(),Component.TypeFullyQualifiedName));
                HasErrors=true;
                continue;
            }
            Components.Add(Component);
        }

        ImmutableArray<SceneNode>.Builder Children=
            ImmutableArray.CreateBuilder<SceneNode>(Json.Children?.Count??0);
        var ChildIndex=0;
        foreach(SceneJsonNode Child in Json.Children??Enumerable.Empty<SceneJsonNode>()){
            Children.Add(BuildNode(Child,Path.Append(ChildIndex),Compilation,FilePath,Diagnostics,ref HasErrors));
            ChildIndex++;
        }

        return new SceneNode(
            Path,
            Json.Name,
            Components.ToImmutable(),
            Children.ToImmutable(),
            Location,
            InitializerLocation:Location,
            NameValueLocation:Location,
            ComponentsListLocation:Location,
            ChildrenListLocation:Location);
    }

    private static bool TryBuildComponent(
        SceneJsonComponent Json,Compilation Compilation,string FilePath,
        ImmutableArray<Diagnostic>.Builder Diagnostics,[NotNullWhen(true)]out SceneComponent?Component){
        Component=null;
        if(string.IsNullOrWhiteSpace(Json.Type)){
            Diagnostics.Add(Diagnostic.Create(
                SceneDslDiagnostics.InvalidSceneJson,FileLocation(FilePath),"Component 'type' is required."));
            return false;
        }

        INamedTypeSymbol?TypeSymbol=Compilation.GetTypeByMetadataName(Json.Type);
        if(TypeSymbol is null||!SceneTypePredicates.IsNonNullableStruct(TypeSymbol)){
            Diagnostics.Add(Diagnostic.Create(SceneDslDiagnostics.ComponentMustBeStructCreation,FileLocation(FilePath),Json.Type));
            return false;
        }

        string TypeFullyQualifiedName=SceneTypePredicates.ToFullyQualifiedName(TypeSymbol);
        if(SceneTypePredicates.IsReservedComponent(TypeFullyQualifiedName)){
            Diagnostics.Add(Diagnostic.Create(SceneDslDiagnostics.ReservedComponentType,FileLocation(FilePath),TypeFullyQualifiedName));
            return false;
        }

        SceneSourceLocation Location=SceneSourceLocation.FromJsonFile(FilePath);
        ImmutableArray<SceneComponentMember>.Builder Members=ImmutableArray.CreateBuilder<SceneComponentMember>();
        var Succeeded=true;
        foreach(KeyValuePair<string,JsonElement>Member in Json.Members??new Dictionary<string,JsonElement>()){
            if(!TryResolveMember(TypeSymbol,Member.Key,out ITypeSymbol?MemberType,out ISymbol?MemberSymbol)){
                Diagnostics.Add(Diagnostic.Create(
                    SceneDslDiagnostics.UnknownJsonMember,FileLocation(FilePath),Member.Key,TypeFullyQualifiedName));
                Succeeded=false;
                continue;
            }

            if(!TryBuildValue(MemberType!,Member.Value,FilePath,Diagnostics,out SceneValue?Value)){
                Succeeded=false;
                continue;
            }

            Members.Add(new SceneComponentMember(Member.Key,MemberSymbol,Value,Location));
        }

        if(!Succeeded){
            return false;
        }

        ImmutableArray<SceneComponentMember>MembersArray=Members.ToImmutable();
        string EmitExpressionText=SceneEmitExpressionWriter.WriteObjectCreation(
            TypeFullyQualifiedName,
            Arguments:[],
            MemberAssignments:[.. MembersArray.Select(static M=>SceneEmitExpressionWriter.WriteMemberAssignment(M.MemberName,M.Value.EmitExpressionText))]);

        Component=new SceneComponent(
            TypeSymbol,TypeFullyQualifiedName,ImmutableArray<SceneValue>.Empty,MembersArray,EmitExpressionText,Location);
        return true;
    }

    /// <summary>Componentの型からJSONメンバー名に対応する公開フィールド/プロパティの宣言型とシンボルを引く。</summary>
    private static bool TryResolveMember(
        INamedTypeSymbol TypeSymbol,string MemberName,out ITypeSymbol?MemberType,out ISymbol?MemberSymbol){
        foreach(ISymbol Candidate in TypeSymbol.GetMembers(MemberName)){
            switch(Candidate){
                case IFieldSymbol { DeclaredAccessibility:Accessibility.Public,IsConst:false,IsReadOnly:false }Field:
                    MemberType=Field.Type;
                    MemberSymbol=Field;
                    return true;
                case IPropertySymbol { DeclaredAccessibility:Accessibility.Public,IsReadOnly:false }Property:
                    MemberType=Property.Type;
                    MemberSymbol=Property;
                    return true;
            }
        }

        MemberType=null;
        MemberSymbol=null;
        return false;
    }

    /// <summary>
    /// JSON値の解釈は対象フィールド/プロパティの宣言型で決める(JSON側にタグは持たせない)。
    /// enumなら文字列をメンバー名として<c>Compilation</c>から解決し、それ以外はJSONネイティブ型を
    /// そのまま<see cref="SceneValue"/>へ変換する。
    /// </summary>
    private static bool TryBuildValue(
        ITypeSymbol MemberType,JsonElement Value,string FilePath,
        ImmutableArray<Diagnostic>.Builder Diagnostics,[NotNullWhen(true)]out SceneValue?Result){
        Result=null;
        SceneSourceLocation Location=SceneSourceLocation.FromJsonFile(FilePath);

        if(MemberType.TypeKind==TypeKind.Enum){
            if(Value.ValueKind!=JsonValueKind.String){
                Diagnostics.Add(Diagnostic.Create(
                    SceneDslDiagnostics.JsonValueTypeMismatch,FileLocation(FilePath),
                    SceneTypePredicates.ToFullyQualifiedName(MemberType),"string (enum member name)"));
                return false;
            }

            string MemberName=Value.GetString()??"";
            ISymbol?EnumField=MemberType.GetMembers(MemberName)
                .FirstOrDefault(static Candidate=>Candidate is IFieldSymbol { IsConst:true });
            if(EnumField is null){
                Diagnostics.Add(Diagnostic.Create(
                    SceneDslDiagnostics.UnknownEnumMember,FileLocation(FilePath),MemberName,
                    SceneTypePredicates.ToFullyQualifiedName(MemberType)));
                return false;
            }

            string EmitText=SceneEmitExpressionWriter.WriteSymbolReference(EnumField);
            object?ConstantValue=EnumField is IFieldSymbol EnumFieldSymbol?EnumFieldSymbol.ConstantValue:null;
            Result=Create(SceneValueKind.EnumMember,MemberType,ConstantValue,EnumField,EmitText,Location);
            return true;
        }

        switch(MemberType.SpecialType){
            case SpecialType.System_String:
                if(Value.ValueKind!=JsonValueKind.String){
                    return Mismatch(MemberType,"string",FilePath,Diagnostics,out Result);
                }
                return Literal(MemberType,Value.GetString(),SceneEmitExpressionWriter.WriteConstant(Value.GetString()),Location,out Result);
            case SpecialType.System_Boolean:
                if(Value.ValueKind is not(JsonValueKind.True or JsonValueKind.False)){
                    return Mismatch(MemberType,"bool",FilePath,Diagnostics,out Result);
                }
                return Literal(MemberType,Value.GetBoolean(),SceneEmitExpressionWriter.WriteConstant(Value.GetBoolean()),Location,out Result);
            case SpecialType.System_Single:
                return TryNumber(MemberType,Value,FilePath,Diagnostics,Location,out Result,
                    static El=>(El.GetSingle(),SceneEmitExpressionWriter.WriteConstant(El.GetSingle())));
            case SpecialType.System_Double:
                return TryNumber(MemberType,Value,FilePath,Diagnostics,Location,out Result,
                    static El=>(El.GetDouble(),SceneEmitExpressionWriter.WriteConstant(El.GetDouble())));
            case SpecialType.System_Int32:
                return TryNumber(MemberType,Value,FilePath,Diagnostics,Location,out Result,
                    static El=>((object)El.GetInt32(),SceneEmitExpressionWriter.WriteConstant(El.GetInt32())));
            case SpecialType.System_Int64:
                return TryNumber(MemberType,Value,FilePath,Diagnostics,Location,out Result,
                    static El=>((object)El.GetInt64(),SceneEmitExpressionWriter.WriteConstant(El.GetInt64())));
            default:
                Diagnostics.Add(Diagnostic.Create(
                    SceneDslDiagnostics.UnsupportedJsonMemberType,FileLocation(FilePath),
                    SceneTypePredicates.ToFullyQualifiedName(MemberType)));
                return false;
        }
    }

    private static bool TryNumber(
        ITypeSymbol MemberType,JsonElement Value,string FilePath,ImmutableArray<Diagnostic>.Builder Diagnostics,
        SceneSourceLocation Location,[NotNullWhen(true)]out SceneValue?Result,Func<JsonElement,(object Value,string EmitText)>Format){
        if(Value.ValueKind!=JsonValueKind.Number){
            return Mismatch(MemberType,"number",FilePath,Diagnostics,out Result);
        }
        (object NumberValue,string EmitText)=Format(Value);
        return Literal(MemberType,NumberValue,EmitText,Location,out Result);
    }

    private static bool Literal(
        ITypeSymbol MemberType,object?ConstantValue,string EmitText,SceneSourceLocation Location,[NotNullWhen(true)]out SceneValue?Result){
        Result=Create(SceneValueKind.Literal,MemberType,ConstantValue,null,EmitText,Location);
        return true;
    }

    private static bool Mismatch(
        ITypeSymbol MemberType,string ExpectedJsonKind,string FilePath,
        ImmutableArray<Diagnostic>.Builder Diagnostics,[NotNullWhen(true)]out SceneValue?Result){
        Result=null;
        Diagnostics.Add(Diagnostic.Create(
            SceneDslDiagnostics.JsonValueTypeMismatch,FileLocation(FilePath),
            SceneTypePredicates.ToFullyQualifiedName(MemberType),ExpectedJsonKind));
        return false;
    }

    private static SceneValue Create(
        SceneValueKind Kind,ITypeSymbol?Type,object?ConstantValue,ISymbol?ReferencedSymbol,
        string EmitExpressionText,SceneSourceLocation Location){
        return new SceneValue(
            Kind,
            Type,
            ConstantValue,
            ReferencedSymbol,
            ImmutableArray<SceneComponentMember>.Empty,
            ImmutableArray<SceneValue>.Empty,
            ImmutableArray<SceneValue>.Empty,
            EmitExpressionText,
            EmitExpressionText,
            Location);
    }

    private static Location FileLocation(string FilePath)=>Location.Create(FilePath,default,default);
}
