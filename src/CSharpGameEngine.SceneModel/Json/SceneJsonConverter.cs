using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
namespace CSharpGameEngine.SceneAnalysis.Json;
/// <summary>
/// C# の <c>IScene</c> クラス（解析済みの <see cref="SceneModel"/>）を Scene JSON ドキュメントに変換する（ADR-0070 の切り替え）。
/// ドキュメントから元と同じ Scene をコンパイルできる。コンストラクター呼び出しは引数名をキーにしたオブジェクトになり、メンバーへの代入はメンバー名の項目になり、定数、列挙メンバー、コレクションは
/// JSON 値になる。JSON で表現できない値（非定数の static メンバー、ファクトリー呼び出し、NaN と無限大、未対応構文）は推測せず、エラーとして報告する。ドキュメントのスキーマバージョン
/// は3。<c>[Angle]</c> メンバー内の各数値は回転数で書き込み、キーから C# の単位名を省く（ADR-0074）。
/// </summary>
internal static class SceneJsonConverter {
    internal static Result ConvertDataAsset(ITypeSymbol Type,IReadOnlyList<SceneComponentMember>Members){
        var Problems=new List<string>();
        var Name=Type.ToDisplayString();
        var Values=ObjectValue(Type,Array.Empty<SceneValue>(),Members,null,Name,Problems,null);
        var Document=SceneJsonOut.Object(Multiline:true).Add("schemaVersion",SceneJsonOut.Number("3"))
            .Add("type",SceneJsonOut.String(Name)).Add("members",Values);
        return new Result(Problems.Count==0?SceneJsonWriter.Write(Document):null,Problems);
    }
    internal sealed class Result {
        internal Result(string?Text,IReadOnlyList<string>Problems){
            this.Text=Text;
            this.Problems=Problems;
        }
        /// <summary>ドキュメントのテキスト。値を変換できなかった場合は <c>null</c>。</summary>
        internal string?Text { get; }
        internal IReadOnlyList<string>Problems { get; }
    }
    /// <param name="SchemaReference">Scene ファイルからの相対パスで表す <c>"$schema"</c> の値。</param>
    /// <param name="IdFor">指定パスにあるノードの既存 ID（編集後もエディターが ID を維持する）。指定が <c>null</c> なら ID を生成する。</param>
    internal static Result Convert(SceneModel Model,string?SchemaReference,Func<NodePath,string?>?IdFor=null){
        List<string>Problems=[];
        if(Model.Root is null){
            return new Result(null,["The scene has no root node."]);
        }
        var Document=SceneJsonOut.Object(Multiline:true);
        if(SchemaReference is not null){ Document.Add("$schema",SceneJsonOut.String(SchemaReference)); }
        Document.Add("schemaVersion",SceneJsonOut.Number(SceneJsonSceneAnalyzer.SchemaVersion.ToString(CultureInfo.InvariantCulture)));
        if(Model.Namespace is not null){ Document.Add("namespace",SceneJsonOut.String(Model.Namespace)); }
        Document.Add("className",SceneJsonOut.String(Model.ClassName));
        if(Model.IsStartupScene){ Document.Add("startupOrder",SceneJsonOut.Number(Model.StartupOrder.ToString(CultureInfo.InvariantCulture))); }
        Document.Add("root",ConvertNode(Model.Root,Model.ClassFullyQualifiedName,IdFor,Problems));
        return new Result(Problems.Count==0?SceneJsonWriter.Write(Document):null,Problems);
    }
    /// <summary>Scene とノードパスから導出する安定したノード ID。変換結果を再現可能にする。</summary>
    internal static string NodeId(string SceneFullName,NodePath Path){
        using var Hash=SHA256.Create();
        var Bytes=Hash.ComputeHash(Encoding.UTF8.GetBytes("cge-scene-node\n"+SceneFullName+"\n"+Path.ToDisplayString()));
        var GuidBytes=new byte[16];
        Array.Copy(Bytes,GuidBytes,16);
        GuidBytes[7]=(byte)((GuidBytes[7]&0x0F)|0x50);
        GuidBytes[8]=(byte)((GuidBytes[8]&0x3F)|0x80);
        return new Guid(GuidBytes).ToString("D");
    }
    private static SceneJsonOut ConvertNode(SceneNode Node,string SceneFullName,Func<NodePath,string?>?IdFor,List<string>Problems){
        var Result=SceneJsonOut.Object(Multiline:true).Add("id",SceneJsonOut.String(IdFor?.Invoke(Node.Path)??NodeId(SceneFullName,Node.Path)));
        if(Node.Name is not null){ Result.Add("name",SceneJsonOut.String(Node.Name)); }
        var Components=SceneJsonOut.Array(Multiline:true);
        foreach(var Component in Node.Components){
            var Where=$"{Node.Path.ToDisplayString()} {Component.TypeFullyQualifiedName}";
            var Members=ObjectValue(Component.TypeSymbol,Component.ConstructorArguments,Component.Members,null,Where,Problems,null);
            Components.Add(SceneJsonOut.Object(Multiline:true)
                .Add("type",SceneJsonOut.String(Component.TypeFullyQualifiedName.Replace("global::",string.Empty)))
                .Add("members",Members));
        }
        Result.Add("components",Components);
        var Children=SceneJsonOut.Array(Multiline:true);
        foreach(var Child in Node.Children){ Children.Add(ConvertNode(Child,SceneFullName,IdFor,Problems)); }
        Result.Add("children",Children);
        return Result;
    }
    private static SceneJsonOut ObjectValue(ITypeSymbol?Type,IReadOnlyList<SceneValue>Arguments,IReadOnlyList<SceneComponentMember>Members,ISymbol?Constructor,string Where,List<string>Problems,
        SceneAngleUnit?Unit){
        var Result=SceneJsonOut.Object(Multiline:true);
        var Names=new HashSet<string>(StringComparer.Ordinal);
        if(Arguments.Count>0){
            var Method=Constructor as IMethodSymbol
                ??(Type as INamedTypeSymbol)?.InstanceConstructors.SingleOrDefault(Candidate=>Candidate.Parameters.Length==Arguments.Count);
            if(Method is null||Method.Parameters.Length<Arguments.Count){
                Problems.Add($"{Where}: the constructor for {Arguments.Count} arguments could not be identified.");
                return Result;
            }
            for(var Index=0;Index<Arguments.Count;Index++){
                var Name=Method.Parameters[Index].Name;
                Names.Add(Name);
                Result.Add(SceneJsonSceneAnalyzer.KeyOf(Method.Parameters[Index]),
                    Value(Arguments[Index],$"{Where}.{Name}",Problems,Unit??SceneJsonSceneAnalyzer.AngleUnitOf(Method.Parameters[Index])));
            }
        }
        foreach(var Member in Members){
            if(!Names.Add(Member.MemberName)){
                Problems.Add($"{Where}: '{Member.MemberName}' is both a constructor argument and a member assignment.");
                continue;
            }
            var MemberSymbol=Member.MemberSymbol??(Type as INamedTypeSymbol is { }Named?SceneJsonSceneAnalyzer.FindSettableMemberSymbol(Named,Member.MemberName):null);
            Result.Add(MemberSymbol is null?Member.MemberName:SceneJsonSceneAnalyzer.KeyOf(MemberSymbol),
                Value(Member.Value,$"{Where}.{Member.MemberName}",Problems,Unit??SceneJsonSceneAnalyzer.AngleUnitOf(MemberSymbol)));
        }
        return Result;
    }
    private static SceneJsonOut Value(SceneValue Value,string Where,List<string>Problems,SceneAngleUnit?Unit){
        switch(Value.Kind){
            case SceneValueKind.EnumMember:
                return SceneJsonOut.String(Value.ReferencedSymbol!.Name);
            case SceneValueKind.Default:
                return DefaultValue(Value.Type,Where,Problems);
            case SceneValueKind.ObjectCreation:{
                var Inline=ObjectValue(Value.Type,Value.Arguments,Value.Members,Value.ReferencedSymbol,Where,Problems,Unit);
                return Inline.Properties!.Count==0?SceneJsonOut.Object():RebuildInline(Inline);
            }
            case SceneValueKind.Collection:{
                var Array=SceneJsonOut.Array();
                for(var Index=0;Index<Value.Elements.Length;Index++){
                    Array.Add(SceneJsonConverter.Value(Value.Elements[Index],$"{Where}[{Index}]",Problems,Unit));
                }
                return Array;
            }
            case SceneValueKind.Literal when Unit is null&&LiteralSpelling(Value) is { }Spelling:
                return SceneJsonOut.Number(Spelling);
            case SceneValueKind.Literal:
            case SceneValueKind.ConstantExpression:
            case SceneValueKind.StaticMemberRef:
                if(Value.Kind==SceneValueKind.StaticMemberRef&&Value.ConstantValue is null&&Value.ReferencedSymbol is not IFieldSymbol{ IsConst:true }){
                    break;
                }
                if(Value.Type?.TypeKind==TypeKind.Enum&&Value.ConstantValue is not null){
                    var Member=Value.Type.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(Field=>Field.HasConstantValue&&Equals(Field.ConstantValue,Value.ConstantValue));
                    if(Member is not null){ return SceneJsonOut.String(Member.Name); }
                }
                return Unit is { }AngleUnit&&Value.ConstantValue is float Angle&&!float.IsNaN(Angle)&&!float.IsInfinity(Angle)
                    ?SceneJsonOut.Number(SceneJsonAngles.FormatTurns(Angle,AngleUnit))
                    :Constant(Value.ConstantValue,Where,Problems);
        }
        Problems.Add($"{Where}: '{Value.SourceExpressionText}' ({Value.Kind}) has no JSON form; write the value itself.");
        return SceneJsonOut.Null();
    }
    /// <summary>編集時に1行だけを変更するため、メンバー値をインラインで書き込む。</summary>
    private static SceneJsonOut RebuildInline(SceneJsonOut Multiline){
        var Inline=SceneJsonOut.Object();
        foreach(var Property in Multiline.Properties!){ Inline.Add(Property.Key,Property.Value); }
        return Inline;
    }
    /// <summary>メンバー型の JSON 形式で表した <c>default</c>。構造体は空オブジェクト（すべてのメンバーが既定値）になる。<c>bool</c>、数値、列挙型はそれぞれ独自のゼロ値を持つため、空オブジェクトでは表現できない。</summary>
    private static SceneJsonOut DefaultValue(ITypeSymbol?Type,string Where,List<string>Problems){
        if(Type is null){ return SceneJsonOut.Null(); }
        if(Type.TypeKind==TypeKind.Enum){
            var Zero=Type.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(static Field=>
                Field.HasConstantValue&&System.Convert.ToString(Field.ConstantValue,CultureInfo.InvariantCulture)=="0");
            if(Zero is not null){ return SceneJsonOut.String(Zero.Name); }
            Problems.Add($"{Where}: default of {Type.Name} has no member with the value 0; write a member name.");
            return SceneJsonOut.Null();
        }
        switch(Type.SpecialType){
            case SpecialType.System_Boolean:
                return SceneJsonOut.Boolean(false);
            case SpecialType.System_SByte:
            case SpecialType.System_Byte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal:
                return SceneJsonOut.Number("0");
        }
        return Type.IsValueType?SceneJsonOut.Object():SceneJsonOut.Null();
    }
    /// <summary><see cref="Constant"/> が C# 定数を JSON として出力できるか（char と有限でない数値は表現形式がない）。</summary>
    internal static bool HasJsonForm(object?Value)=>Value switch {
        null or bool or string or sbyte or byte or short or ushort or int or uint or long or ulong=>true,
        float Single=>!float.IsNaN(Single)&&!float.IsInfinity(Single),
        IFormattable Other=>SceneJsonReader.IsNumber(SceneJsonNumbers.FormatOther(Other)),
        _=>false,
    };
    /// <summary>
    /// 数値リテラルを C# の接尾辞なしで記述したテキスト（JSON 数値として有効な場合）。例：3.14159265358979f は 3.14159265358979 となる。コンパイラーはこのテキスト
    /// から正確に値を得るため、ドキュメントをコンパイルしても同じ値になる。マージ時にはテキストが変わっていない数値をそのまま保つ（M0095）。値から再出力すると、型の精度を超える桁まで記述した数値（
    /// float に対する100.0000001など）は、Scene を編集するたびに100へ書き換えられていた。
    /// </summary>
    private static string?LiteralSpelling(SceneValue Value){
        // 文字列、文字、真偽値、null のリテラルは、どのような接尾辞を付けても JSON 数値にはならない。値を C# の規則で
        // 元のテキストから読み込んでいないリテラル（正しく丸めない .NET Framework で編集値を解析した場合など）は、
        // 再読込時に一致しない場合がある。その場合は値のテキストを
        var Text=Value.SourceExpressionText.TrimEnd('f','F','d','D','m','M','u','U','l','L');
        return SceneJsonReader.IsNumber(Text)&&SceneJsonDecimal.ReadsBack(Text,Value.ConstantValue)?Text:null;
    }
    private static SceneJsonOut Constant(object?Value,string Where,List<string>Problems){
        switch(Value){
            case null:return SceneJsonOut.Null();
            case bool Boolean:return SceneJsonOut.Boolean(Boolean);
            case string Text:return SceneJsonOut.String(Text);
            case float Single when !float.IsNaN(Single)&&!float.IsInfinity(Single):return SceneJsonOut.Number(SceneJsonNumbers.FormatSingle(Single));
            case sbyte or byte or short or ushort or int or uint or long or ulong:
                return SceneJsonOut.Number(((IFormattable)Value).ToString(null,CultureInfo.InvariantCulture));
            case IFormattable Other:{
                // .NET Framework と .NET の両方で、double 定数を同じ最短テキストで出力する（SceneJsonNumbers）。decimal は
                // 独自のテキストを保つ。NaN と正負の無限大（float または double）には形式がない。JSON 数値は有限値。
                var Text=SceneJsonNumbers.FormatOther(Other);
                if(SceneJsonReader.IsNumber(Text)){ return SceneJsonOut.Number(Text); }
                Problems.Add($"{Where}: {Text} has no JSON form; a JSON number is finite.");
                return SceneJsonOut.Null();
            }
        }
        Problems.Add($"{Where}: constant of type {Value.GetType().Name} has no JSON form.");
        return SceneJsonOut.Null();
    }
}
