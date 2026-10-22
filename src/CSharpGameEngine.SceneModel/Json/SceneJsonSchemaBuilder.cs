using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
namespace CSharpGameEngine.SceneAnalysis.Json;
/// <summary>
/// Builds the per-project JSON Schema for scene JSON (ADR-0070) from the same Roslyn rules as
/// <see cref="SceneJsonSceneAnalyzer"/>. The VS JSON editor uses it to warn about undefined component
/// types, members and value kinds while the user types, and to complete names. The build still
/// reports the authoritative SCENE2xx errors.
/// </summary>
internal static class SceneJsonSchemaBuilder {
    internal const string FileName="scene.schema.json";
    internal static string Build(Compilation Compilation){
        var Definitions=SceneJsonOut.Object(Multiline:true);
        var Defined=new HashSet<string>(StringComparer.Ordinal);
        var ComponentTypes=EnumerateComponentTypes(Compilation).OrderBy(static Type=>MetadataName(Type),StringComparer.Ordinal).ToArray();
        var TypeNames=SceneJsonOut.Array();
        var Conditions=SceneJsonOut.Array(Multiline:true);
        foreach(var Type in ComponentTypes){
            var Name=MetadataName(Type);
            TypeNames.Add(SceneJsonOut.String(Name));
            Conditions.Add(SceneJsonOut.Object()
                .Add("if",SceneJsonOut.Object().Add("properties",SceneJsonOut.Object().Add("type",SceneJsonOut.Object().Add("const",SceneJsonOut.String(Name)))))
                .Add("then",SceneJsonOut.Object().Add("properties",SceneJsonOut.Object().Add("members",Reference(Type,Compilation,Definitions,Defined)))));
        }
        Definitions.Add("node",SceneJsonOut.Object(Multiline:true)
            .Add("type",SceneJsonOut.String("object"))
            .Add("additionalProperties",SceneJsonOut.Boolean(false))
            .Add("properties",SceneJsonOut.Object(Multiline:true)
                .Add("id",Described(SceneJsonOut.Object().Add("type",SceneJsonOut.String("string")),"Stable node identity used by the editor."))
                .Add("name",SceneJsonOut.Object().Add("type",SceneJsonOut.String("string")))
                .Add("components",SceneJsonOut.Object().Add("type",SceneJsonOut.String("array")).Add("items",SceneJsonOut.Object().Add("$ref",SceneJsonOut.String("#/definitions/component"))))
                .Add("children",SceneJsonOut.Object().Add("type",SceneJsonOut.String("array")).Add("items",SceneJsonOut.Object().Add("$ref",SceneJsonOut.String("#/definitions/node"))))));
        Definitions.Add("component",SceneJsonOut.Object(Multiline:true)
            .Add("type",SceneJsonOut.String("object"))
            .Add("required",SceneJsonOut.Array().Add(SceneJsonOut.String("type")))
            .Add("additionalProperties",SceneJsonOut.Boolean(false))
            .Add("properties",SceneJsonOut.Object(Multiline:true)
                .Add("type",Described(SceneJsonOut.Object().Add("enum",TypeNames),"Full name of the component's C# type."))
                .Add("members",SceneJsonOut.Object().Add("type",SceneJsonOut.String("object"))))
            .Add("allOf",Conditions));
        var Document=SceneJsonOut.Object(Multiline:true)
            .Add("$schema",SceneJsonOut.String("http://json-schema.org/draft-07/schema#"))
            .Add("title",SceneJsonOut.String("C# Game Engine scene ("+Compilation.AssemblyName+")"))
            .Add("type",SceneJsonOut.String("object"))
            .Add("required",SceneJsonOut.Array().Add(SceneJsonOut.String("schemaVersion")).Add(SceneJsonOut.String("className")).Add(SceneJsonOut.String("root")))
            .Add("additionalProperties",SceneJsonOut.Boolean(false))
            .Add("properties",SceneJsonOut.Object(Multiline:true)
                .Add("$schema",SceneJsonOut.Object().Add("type",SceneJsonOut.String("string")))
                .Add("schemaVersion",Described(SceneJsonOut.Object().Add("enum",SceneJsonOut.Array().Add(SceneJsonOut.Number("1")).Add(SceneJsonOut.Number("2")).Add(SceneJsonOut.Number("3"))),
                    "3 stores every angle in turns (1 = one full rotation) under a key without the C# unit word; 2 uses the C# names; 1 holds each member's own unit. All are read."))
                .Add("namespace",SceneJsonOut.Object().Add("type",SceneJsonOut.String("string")))
                .Add("className",SceneJsonOut.Object().Add("type",SceneJsonOut.String("string")))
                .Add("startupOrder",SceneJsonOut.Object().Add("type",SceneJsonOut.String("integer")))
                .Add("root",SceneJsonOut.Object().Add("$ref",SceneJsonOut.String("#/definitions/node"))))
            .Add("definitions",Definitions);
        return SceneJsonWriter.Write(Document);
    }
    /// <summary>
    /// Component candidates: non-generic structs that the scene JSON check accepts, declared in the
    /// game's compilation (including the engine's shared runtime sources and the plugins' Runtime modules), in the engine's
    /// assemblies (Camera and PrimitiveMesh live in CSharpGameEngine.Windows; StaticMesh is in the Entities sources), or in a
    /// plugin's Library module (<c>[assembly: PluginLibrary]</c>, ADR-0079).
    /// </summary>
    internal static IEnumerable<INamedTypeSymbol>EnumerateComponentTypes(Compilation Compilation){
        var Assemblies=new List<IAssemblySymbol>{ Compilation.Assembly };
        Assemblies.AddRange(Compilation.SourceModule.ReferencedAssemblySymbols.Where(static Assembly=>Assembly.Name.StartsWith("CSharpGameEngine",StringComparison.Ordinal)||IsPluginLibrary(Assembly)));
        foreach(var Assembly in Assemblies){
            foreach(var Type in EnumerateTypes(Assembly.GlobalNamespace)){
                if(Type.TypeKind!=TypeKind.Struct||Type.IsGenericType||!IsVisibleTo(Type,Compilation)){ continue; }
                if(SceneJsonSceneAnalyzer.TryResolveComponentType(MetadataName(Type),Compilation,out var Resolved,out _)
                    &&SymbolEqualityComparer.Default.Equals(Resolved,Type)){
                    yield return Type;
                }
            }
        }
    }
    internal const string PluginLibraryAttributeMetadataName="CSharpGameEngine.Authoring.PluginLibraryAttribute";
    /// <summary>Whether a referenced DLL is a plugin's Library module (<c>[assembly: PluginLibrary]</c>, ADR-0079).</summary>
    internal static bool IsPluginLibrary(IAssemblySymbol Assembly)=>
        Assembly.GetAttributes().Any(static Attribute=>Attribute.AttributeClass?.ToDisplayString()==PluginLibraryAttributeMetadataName);
    private static IEnumerable<INamedTypeSymbol>EnumerateTypes(INamespaceSymbol Namespace){
        foreach(var Member in Namespace.GetMembers()){
            if(Member is INamespaceSymbol Child){
                foreach(var Type in EnumerateTypes(Child)){ yield return Type; }
            } else if(Member is INamedTypeSymbol Type){
                yield return Type;
                foreach(var Nested in Type.GetTypeMembers()){ yield return Nested; }
            }
        }
    }
    private static bool IsVisibleTo(INamedTypeSymbol Type,Compilation Compilation)=>
        Compilation.IsSymbolAccessibleWithin(Type,Compilation.Assembly);
    internal static string MetadataName(INamedTypeSymbol Type)=>
        SceneTypePredicates.ToFullyQualifiedName(Type).Replace("global::",string.Empty);
    private static SceneJsonOut Reference(INamedTypeSymbol Type,Compilation Compilation,SceneJsonOut Definitions,HashSet<string>Defined){
        var Key="type:"+MetadataName(Type);
        if(Defined.Add(Key)){
            var Properties=SceneJsonOut.Object(Multiline:true);
            var Names=new HashSet<string>(StringComparer.Ordinal);
            foreach(var Constructor in Type.InstanceConstructors.Where(static Candidate=>Candidate.DeclaredAccessibility==Accessibility.Public)){
                foreach(var Parameter in Constructor.Parameters){
                    var ParameterKey=SceneJsonSceneAnalyzer.KeyOf(Parameter);
                    if(Names.Add(ParameterKey)){
                        Properties.Add(ParameterKey,Described(ValueSchema(Parameter.Type,Compilation,Definitions,Defined),
                            AngleNote(Parameter)+"Constructor parameter of "+Type.Name+"."));
                    }
                }
            }
            foreach(var Member in SettableMembers(Type)){
                var MemberKey=SceneJsonSceneAnalyzer.KeyOf(Member.Symbol);
                if(!Names.Add(MemberKey)){ continue; }
                var Schema=ValueSchema(Member.Type,Compilation,Definitions,Defined);
                Properties.Add(MemberKey,AngleNote(Member.Symbol) is { Length:>0 }Note?Described(Schema,Note.TrimEnd()):Schema);
            }
            Definitions.Add(Key,SceneJsonOut.Object(Multiline:true)
                .Add("type",SceneJsonOut.String("object"))
                .Add("additionalProperties",SceneJsonOut.Boolean(false))
                .Add("properties",Properties));
        }
        return SceneJsonOut.Object().Add("$ref",SceneJsonOut.String("#/definitions/"+Key));
    }
    /// <summary>ADR-0074: an angle member's numbers are turns in the document, whatever its C# unit.</summary>
    private static string AngleNote(ISymbol Symbol)=>
        SceneJsonSceneAnalyzer.AngleUnitOf(Symbol) is { }Unit
            ?"Angle in turns (1 = one full rotation; the C# member "+Symbol.Name+" is in "+(Unit==SceneAngleUnit.Radians?"radians":"degrees")+"). "
            :string.Empty;
    private static IEnumerable<(string Name,ITypeSymbol Type,ISymbol Symbol)>SettableMembers(INamedTypeSymbol Type){
        for(ITypeSymbol?Current=Type;Current is not null&&Current.SpecialType!=SpecialType.System_ValueType&&Current.SpecialType!=SpecialType.System_Object;Current=Current.BaseType){
            foreach(var Member in Current.GetMembers()){
                if(Member.DeclaredAccessibility!=Accessibility.Public||Member.IsStatic||Member.IsImplicitlyDeclared){ continue; }
                if(Member is IFieldSymbol{ IsReadOnly:false,IsConst:false }Field){ yield return (Field.Name,Field.Type,Field); }
                if(Member is IPropertySymbol{ SetMethod:{ DeclaredAccessibility:Accessibility.Public },IsIndexer:false }Property){ yield return (Property.Name,Property.Type,Property); }
            }
        }
    }
    private static SceneJsonOut ValueSchema(ITypeSymbol Type,Compilation Compilation,SceneJsonOut Definitions,HashSet<string>Defined){
        if(Type is INamedTypeSymbol{ OriginalDefinition.SpecialType:SpecialType.System_Nullable_T }Nullable){
            return SceneJsonOut.Object().Add("anyOf",SceneJsonOut.Array().Add(ValueSchema(Nullable.TypeArguments[0],Compilation,Definitions,Defined)).Add(SceneJsonOut.Object().Add("type",SceneJsonOut.String("null"))));
        }
        if(Type.TypeKind==TypeKind.Enum){
            var Names=SceneJsonOut.Array();
            foreach(var Field in Type.GetMembers().OfType<IFieldSymbol>().Where(static Field=>Field.HasConstantValue)){ Names.Add(SceneJsonOut.String(Field.Name)); }
            return SceneJsonOut.Object().Add("enum",Names);
        }
        switch(Type.SpecialType){
            case SpecialType.System_Boolean:return Typed("boolean");
            case SpecialType.System_String:return SceneJsonOut.Object().Add("type",SceneJsonOut.Array().Add(SceneJsonOut.String("string")).Add(SceneJsonOut.String("null")));
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal:return Typed("number");
            case SpecialType.System_SByte:
            case SpecialType.System_Byte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:return Typed("integer");
        }
        // A value the build rejects (SCENE203) matches nothing, so the editor warns while typing as the build would.
        if(Type is IArrayTypeSymbol Array){
            return Array.Rank>1
                ?Unwritable("A multidimensional array cannot be written in scene JSON.")
                :SceneJsonOut.Object().Add("type",SceneJsonOut.String("array")).Add("items",ValueSchema(Array.ElementType,Compilation,Definitions,Defined));
        }
        if(Type.SpecialType==SpecialType.None&&CollectionElement(Type,Compilation) is { }Element){
            return SceneJsonSceneAnalyzer.IsCollectionExpressionTarget(Type,Element,Compilation)
                ?SceneJsonOut.Object().Add("type",SceneJsonOut.String("array")).Add("items",ValueSchema(Element,Compilation,Definitions,Defined))
                :Unwritable("This collection type cannot be created from a list of elements.");
        }
        if(Type is INamedTypeSymbol{ IsTupleType:true }){ return Unwritable("A tuple cannot be written in scene JSON."); }
        if(Type is INamedTypeSymbol{ IsAbstract:true,TypeKind:TypeKind.Class }){ return Unwritable("An abstract type cannot be created."); }
        var AcceptsNumber=Compilation.ClassifyConversion(Compilation.GetSpecialType(SpecialType.System_Single),Type).IsImplicit;
        if(Type is INamedTypeSymbol Named&&Named.TypeKind is TypeKind.Struct or TypeKind.Class&&Named.SpecialType==SpecialType.None){
            var Object=Reference(Named,Compilation,Definitions,Defined);
            return AcceptsNumber?SceneJsonOut.Object().Add("anyOf",SceneJsonOut.Array().Add(Typed("number")).Add(Object)):Object;
        }
        return AcceptsNumber?Typed("number"):SceneJsonOut.Object();
    }
    /// <summary>A schema no value matches, with the reason for the editor's warning.</summary>
    private static SceneJsonOut Unwritable(string Reason)=>Described(SceneJsonOut.Object().Add("not",SceneJsonOut.Object()),Reason);
    /// <summary>The element of an enumerable, the type itself included (an IEnumerable&lt;T&gt; member), as the analyzer reads it.</summary>
    private static ITypeSymbol?CollectionElement(ITypeSymbol Type,Compilation Compilation){
        var Enumerable=Compilation.GetSpecialType(SpecialType.System_Collections_Generic_IEnumerable_T);
        foreach(var Interface in Type.AllInterfaces.Prepend(Type as INamedTypeSymbol).OfType<INamedTypeSymbol>()){
            if(SymbolEqualityComparer.Default.Equals(Interface.OriginalDefinition,Enumerable)){ return Interface.TypeArguments[0]; }
        }
        return null;
    }
    private static SceneJsonOut Typed(string Name)=>SceneJsonOut.Object().Add("type",SceneJsonOut.String(Name));
    private static SceneJsonOut Described(SceneJsonOut Schema,string Description)=>Schema.Add("description",SceneJsonOut.String(Description));
}
