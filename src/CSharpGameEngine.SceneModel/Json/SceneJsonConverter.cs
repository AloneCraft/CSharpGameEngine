using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
namespace CSharpGameEngine.SceneAnalysis.Json;
/// <summary>
/// Converts a C# <c>IScene</c> class (its analyzed <see cref="SceneModel"/>) into a scene JSON
/// document (ADR-0070 cutover). The document compiles to the same scene: a constructor call becomes an
/// object keyed by the constructor's parameter names, member assignments add their member names, and
/// constants, enum members and collections become JSON values. Values JSON cannot hold (non-constant
/// static members, factory calls, NaN and infinities, unsupported syntax) are reported instead of guessed. The document is schema
/// version 3: every number inside an <c>[Angle]</c> member is written in turns, under the member's key without its
/// C# unit word (ADR-0074).
/// </summary>
internal static class SceneJsonConverter {
    internal sealed class Result {
        internal Result(string?Text,IReadOnlyList<string>Problems){
            this.Text=Text;
            this.Problems=Problems;
        }
        /// <summary>The document text, or <c>null</c> when a value could not be converted.</summary>
        internal string?Text { get; }
        internal IReadOnlyList<string>Problems { get; }
    }
    /// <param name="SchemaReference">The <c>"$schema"</c> path relative to the scene file.</param>
    /// <param name="IdFor">The existing id of the node at a path (the editor keeps ids across edits); <c>null</c> derives one.</param>
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
    /// <summary>A stable node id derived from the scene and the node path, so conversion is reproducible.</summary>
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
    /// <summary>Member values are written inline so an edit touches one line.</summary>
    private static SceneJsonOut RebuildInline(SceneJsonOut Multiline){
        var Inline=SceneJsonOut.Object();
        foreach(var Property in Multiline.Properties!){ Inline.Add(Property.Key,Property.Value); }
        return Inline;
    }
    /// <summary>
    /// <c>default</c> in the member type's JSON form. A struct is an empty object (every member default);
    /// <c>bool</c>, numbers and enums have their own zero value, which an empty object would not fit.
    /// </summary>
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
    private static SceneJsonOut Constant(object?Value,string Where,List<string>Problems){
        switch(Value){
            case null:return SceneJsonOut.Null();
            case bool Boolean:return SceneJsonOut.Boolean(Boolean);
            case string Text:return SceneJsonOut.String(Text);
            case float Single when !float.IsNaN(Single)&&!float.IsInfinity(Single):return SceneJsonOut.Number(SceneJsonNumbers.FormatSingle(Single));
            case sbyte or byte or short or ushort or int or uint or long or ulong:
                return SceneJsonOut.Number(((IFormattable)Value).ToString(null,CultureInfo.InvariantCulture));
            case IFormattable Other:{
                // Double constants get the same shortest text on .NET Framework and .NET (SceneJsonNumbers); decimal keeps
                // its own text. NaN and the infinities (float or double) have none: a JSON number is finite.
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
