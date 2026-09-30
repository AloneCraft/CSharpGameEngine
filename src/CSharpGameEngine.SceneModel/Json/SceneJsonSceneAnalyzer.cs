using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
namespace CSharpGameEngine.SceneAnalysis.Json;
/// <summary>One problem found in a scene document, located by character offsets in its text.</summary>
internal readonly struct SceneJsonProblem {
    internal SceneJsonProblem(DiagnosticDescriptor Descriptor,int Start,int End,params object[]Arguments){
        this.Descriptor=Descriptor;
        this.Start=Start;
        this.End=End;
        this.Arguments=Arguments;
    }
    internal DiagnosticDescriptor Descriptor { get; }
    internal int Start { get; }
    internal int End { get; }
    internal object[]Arguments { get; }
}
/// <summary>A component of a node: its <c>global::</c> type name and the C# creation expression.</summary>
internal readonly struct SceneJsonComponent {
    internal SceneJsonComponent(string TypeFullyQualifiedName,string EmitExpressionText){
        this.TypeFullyQualifiedName=TypeFullyQualifiedName;
        this.EmitExpressionText=EmitExpressionText;
    }
    internal string TypeFullyQualifiedName { get; }
    internal string EmitExpressionText { get; }
}
internal sealed class SceneJsonNode {
    internal SceneJsonNode(string?Id,string?Name,ImmutableArray<int>Path,ImmutableArray<SceneJsonComponent>Components,ImmutableArray<SceneJsonNode>Children){
        this.Id=Id;
        this.Name=Name;
        this.Path=Path;
        this.Components=Components;
        this.Children=Children;
    }
    internal string?Id { get; }
    internal string?Name { get; }
    /// <summary>Child indices from the root, like <see cref="NodePath"/>.</summary>
    internal ImmutableArray<int>Path { get; }
    internal ImmutableArray<SceneJsonComponent>Components { get; }
    internal ImmutableArray<SceneJsonNode>Children { get; }
}
internal sealed class SceneJsonScene {
    internal SceneJsonScene(string?Namespace,string ClassName,int?StartupOrder,SceneJsonNode?Root,ImmutableArray<SceneJsonProblem>Problems){
        this.Namespace=Namespace;
        this.ClassName=ClassName;
        this.StartupOrder=StartupOrder;
        this.Root=Root;
        this.Problems=Problems;
    }
    internal string?Namespace { get; }
    internal string ClassName { get; }
    internal int?StartupOrder { get; }
    internal SceneJsonNode?Root { get; }
    internal ImmutableArray<SceneJsonProblem>Problems { get; }
    internal bool HasErrors=>!this.Problems.IsEmpty||this.Root is null;
    /// <summary>Each node's id by its path; the editor carries ids across edits with it.</summary>
    internal Dictionary<NodePath,string>NodeIdsByPath(){
        var Result=new Dictionary<NodePath,string>();
        void Walk(SceneJsonNode Node){
            if(Node.Id is { Length:>0 }Id){ Result[new NodePath(Node.Path)]=Id; }
            foreach(var Child in Node.Children){ Walk(Child); }
        }
        if(this.Root is not null){ Walk(this.Root); }
        return Result;
    }
}
/// <summary>
/// Reads a <c>*.scene.json</c> document against a <see cref="Compilation"/> (ADR-0070). Values follow
/// the member's declared C# type; there are no tags in the JSON:
/// <list type="bullet">
/// <item>enums are member-name strings; bool, string and numeric members use JSON literals, and a
/// number's text is emitted unchanged with the literal suffix of the target type;</item>
/// <item>a struct or class value is an object whose keys are constructor parameter names (the public
/// constructor consuming the most keys wins) followed by settable members;</item>
/// <item>a number may also fill a type with an implicit conversion from <c>float</c>
/// (for example <c>LogicalScalar</c>).</item>
/// </list>
/// Every rejected entry becomes a <see cref="SceneJsonProblem"/> so the whole document is reported at once.
/// </summary>
internal static class SceneJsonSceneAnalyzer {
    internal const int SchemaVersion=1;
    private static readonly string[]DocumentKeys=["$schema","schemaVersion","namespace","className","startupOrder","root"];
    private static readonly string[]NodeKeys=["id","name","components","children"];
    private static readonly string[]ComponentKeys=["type","members"];
    internal static SceneJsonScene Analyze(string Text,Compilation Compilation){
        List<SceneJsonProblem>Problems=[];
        SceneJsonValue Document;
        try {
            Document=SceneJsonReader.Parse(Text);
        } catch(SceneJsonException Error){
            Problems.Add(new SceneJsonProblem(SceneJsonDiagnostics.InvalidDocument,Error.Position,Error.Position+1,Error.Message));
            return new SceneJsonScene(null,string.Empty,null,null,Problems.ToImmutableArray());
        }
        if(Document.Kind!=SceneJsonKind.Object){
            Problems.Add(Invalid(Document,"A scene document must be a JSON object."));
            return new SceneJsonScene(null,string.Empty,null,null,Problems.ToImmutableArray());
        }
        RejectUnknownKeys(Document,DocumentKeys,"document",Problems);
        var Version=Document.Get("schemaVersion");
        if(Version is null||Version.Kind!=SceneJsonKind.Number||Version.Text!="1"){
            Problems.Add(Invalid(Version??Document,$"'schemaVersion' must be {SchemaVersion}."));
        }
        var Namespace=OptionalString(Document,"namespace",Problems);
        if(Namespace is not null&&!Namespace.Split('.').All(IsIdentifier)){
            Problems.Add(Invalid(Document.Get("namespace")!,$"'{Namespace}' is not a namespace name."));
        }
        var ClassName=OptionalString(Document,"className",Problems);
        if(ClassName is null){
            Problems.Add(Invalid(Document,"'className' is required."));
            ClassName=string.Empty;
        } else if(!IsIdentifier(ClassName)){
            Problems.Add(Invalid(Document.Get("className")!,$"'{ClassName}' is not a C# identifier."));
        }
        int?StartupOrder=null;
        if(Document.Get("startupOrder") is { }Order){
            if(Order.Kind==SceneJsonKind.Number&&int.TryParse(Order.Text,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var Parsed)){
                StartupOrder=Parsed;
            } else {
                Problems.Add(Invalid(Order,"'startupOrder' must be an integer."));
            }
        }
        SceneJsonNode?Root=null;
        if(Document.Get("root") is { }RootValue){
            Root=AnalyzeNode(RootValue,ImmutableArray<int>.Empty,Compilation,Problems);
        } else {
            Problems.Add(Invalid(Document,"'root' is required."));
        }
        return new SceneJsonScene(Namespace,ClassName,StartupOrder,Root,Problems.ToImmutableArray());
    }
    private static SceneJsonNode?AnalyzeNode(SceneJsonValue Node,ImmutableArray<int>Path,Compilation Compilation,List<SceneJsonProblem>Problems){
        if(Node.Kind!=SceneJsonKind.Object){
            Problems.Add(Invalid(Node,"A node must be a JSON object."));
            return null;
        }
        RejectUnknownKeys(Node,NodeKeys,"node",Problems);
        var Id=OptionalString(Node,"id",Problems);
        var Name=OptionalString(Node,"name",Problems);
        var Components=ImmutableArray.CreateBuilder<SceneJsonComponent>();
        if(Node.Get("components") is { }ComponentList){
            if(ComponentList.Kind!=SceneJsonKind.Array){
                Problems.Add(Invalid(ComponentList,"'components' must be an array."));
            } else {
                HashSet<string>Seen=new(StringComparer.Ordinal);
                foreach(var Item in ComponentList.Items){
                    if(AnalyzeComponent(Item,Compilation,Problems) is not { }Component){ continue; }
                    if(!Seen.Add(Component.TypeFullyQualifiedName)){
                        var Type=Item.Get("type")!;
                        Problems.Add(new SceneJsonProblem(SceneJsonDiagnostics.DuplicateComponent,Type.Start,Type.End,Type.Text!));
                        continue;
                    }
                    Components.Add(Component);
                }
            }
        }
        var Children=ImmutableArray.CreateBuilder<SceneJsonNode>();
        if(Node.Get("children") is { }ChildList){
            if(ChildList.Kind!=SceneJsonKind.Array){
                Problems.Add(Invalid(ChildList,"'children' must be an array."));
            } else {
                for(var Index=0;Index<ChildList.Items.Count;Index++){
                    if(AnalyzeNode(ChildList.Items[Index],Path.Add(Index),Compilation,Problems) is { }Child){ Children.Add(Child); }
                }
            }
        }
        return new SceneJsonNode(Id,Name,Path,Components.ToImmutable(),Children.ToImmutable());
    }
    private static SceneJsonComponent?AnalyzeComponent(SceneJsonValue Component,Compilation Compilation,List<SceneJsonProblem>Problems){
        if(Component.Kind!=SceneJsonKind.Object){
            Problems.Add(Invalid(Component,"A component must be a JSON object."));
            return null;
        }
        RejectUnknownKeys(Component,ComponentKeys,"component",Problems);
        var TypeValue=Component.Get("type");
        if(TypeValue is null||TypeValue.Kind!=SceneJsonKind.String||string.IsNullOrWhiteSpace(TypeValue.Text)){
            Problems.Add(Invalid(TypeValue??Component,"A component needs a 'type' string with the component's full type name."));
            return null;
        }
        var TypeName=TypeValue.Text!;
        if(!TryResolveComponentType(TypeName,Compilation,out var Type,out var Reason)){
            Problems.Add(new SceneJsonProblem(SceneJsonDiagnostics.UnknownComponentType,TypeValue.Start,TypeValue.End,TypeName,Reason));
            return null;
        }
        var Members=Component.Get("members");
        if(Members is not null&&Members.Kind!=SceneJsonKind.Object){
            Problems.Add(Invalid(Members,"'members' must be a JSON object."));
            return null;
        }
        var ProblemCount=Problems.Count;
        var Expression=ObjectExpression(Type!,Members,TypeName,Compilation,Problems);
        if(Problems.Count!=ProblemCount){ return null; }
        return new SceneJsonComponent(SceneTypePredicates.ToFullyQualifiedName(Type!),Expression);
    }
    /// <summary>A component type is a non-generic, non-reserved struct that is not a runtime companion.</summary>
    internal static bool TryResolveComponentType(string TypeName,Compilation Compilation,out INamedTypeSymbol?Type,out string Reason){
        Type=Compilation.GetTypeByMetadataName(TypeName);
        if(Type is null){
            Reason="no type with this full name is visible to the project";
            return false;
        }
        if(!SceneTypePredicates.IsNonNullableStruct(Type)){
            Reason="components must be structs";
            return false;
        }
        if(Type.IsGenericType||Type.IsRefLikeType){
            Reason="generic and ref struct types cannot be components";
            return false;
        }
        if(SceneTypePredicates.IsReservedComponent(SceneTypePredicates.ToFullyQualifiedName(Type))){
            Reason="the engine reserves this type";
            return false;
        }
        if(SceneTypePredicates.ImplementsRuntimeComponentInterface(Type,Compilation)){
            Reason="runtime component types are attached automatically; declare the authored component instead";
            return false;
        }
        if(!SceneTypePredicates.IsAddableComponent(Type,Compilation)){
            Reason="this type is not a scene component (it is engine infrastructure, a system, or has required members)";
            return false;
        }
        Reason=string.Empty;
        return true;
    }
    private static string ObjectExpression(INamedTypeSymbol Type,SceneJsonValue?Value,string DisplayName,Compilation Compilation,List<SceneJsonProblem>Problems){
        var TypeText=SceneTypePredicates.ToFullyQualifiedName(Type);
        var Properties=Value?.Properties??Array.Empty<SceneJsonProperty>();
        var Keys=new HashSet<string>(Properties.Select(static Property=>Property.Name),StringComparer.Ordinal);
        var Constructor=ChooseConstructor(Type,Keys);
        var Arguments=new List<string>();
        var Consumed=new HashSet<string>(StringComparer.Ordinal);
        if(Constructor is not null){
            foreach(var Parameter in Constructor.Parameters){
                var Property=Properties.FirstOrDefault(Candidate=>Candidate.Name==Parameter.Name);
                if(Property is null){ continue; }
                Consumed.Add(Property.Name);
                Arguments.Add(Parameter.Name+": "+ValueExpression(Parameter.Type,Property.Value,DisplayName+"."+Property.Name,Compilation,Problems));
            }
        }
        var Assignments=new List<string>();
        var IncompleteConstructorReported=false;
        foreach(var Property in Properties){
            if(Consumed.Contains(Property.Name)){ continue; }
            if(FindSettableMember(Type,Property.Name) is not { }MemberType){
                // A constructor parameter name that no usable constructor consumed means the other
                // required arguments are missing; say which, once, instead of "no such member".
                if(IncompleteConstructorFor(Type,Keys,Property.Name) is { }Incomplete){
                    if(!IncompleteConstructorReported&&Value is not null){
                        IncompleteConstructorReported=true;
                        var Missing=Incomplete.Parameters.Where(Parameter=>!Keys.Contains(Parameter.Name)&&!Parameter.HasExplicitDefaultValue).Select(static Parameter=>"'"+Parameter.Name+"'");
                        var Signature=Type.Name+"("+string.Join(", ",Incomplete.Parameters.Select(static Parameter=>Parameter.Name))+")";
                        Problems.Add(new SceneJsonProblem(SceneJsonDiagnostics.ValueMismatch,Value.Start,Value.End,DisplayName,Type.ToDisplayString(),
                            $"the constructor {Signature} also needs {string.Join(", ",Missing)}"));
                    }
                    continue;
                }
                var NameStart=Property.NameStart;
                Problems.Add(new SceneJsonProblem(SceneJsonDiagnostics.UnknownMember,NameStart,NameStart+Property.Name.Length+2,SceneTypePredicates.ToFullyQualifiedName(Type).Replace("global::",string.Empty),Property.Name));
                continue;
            }
            Assignments.Add(Property.Name+" = "+ValueExpression(MemberType,Property.Value,DisplayName+"."+Property.Name,Compilation,Problems));
        }
        var Builder=new StringBuilder("new ").Append(TypeText).Append('(').Append(string.Join(", ",Arguments)).Append(')');
        if(Assignments.Count>0){ Builder.Append(" { ").Append(string.Join(", ",Assignments)).Append(" }"); }
        return Builder.ToString();
    }
    /// <summary>
    /// The public constructor whose parameters are all named by the keys (or have defaults) and that
    /// consumes the most keys. A parameterless constructor (implicit for structs) consumes none.
    /// </summary>
    private static IMethodSymbol?ChooseConstructor(INamedTypeSymbol Type,HashSet<string>Keys){
        IMethodSymbol?Best=null;
        var BestCount=-1;
        foreach(var Candidate in Type.InstanceConstructors){
            if(Candidate.DeclaredAccessibility!=Accessibility.Public){ continue; }
            var Count=0;
            var Usable=true;
            foreach(var Parameter in Candidate.Parameters){
                if(Keys.Contains(Parameter.Name)){
                    Count++;
                } else if(!Parameter.HasExplicitDefaultValue){
                    Usable=false;
                    break;
                }
            }
            if(Usable&&Count>BestCount){
                Best=Candidate;
                BestCount=Count;
            }
        }
        return BestCount>0?Best:null;
    }
    /// <summary>The public constructor with the most parameters named by the keys that includes <paramref name="Name"/>.</summary>
    private static IMethodSymbol?IncompleteConstructorFor(INamedTypeSymbol Type,HashSet<string>Keys,string Name)=>
        Type.InstanceConstructors
            .Where(static Candidate=>Candidate.DeclaredAccessibility==Accessibility.Public)
            .Where(Candidate=>Candidate.Parameters.Any(Parameter=>Parameter.Name==Name))
            .OrderByDescending(Candidate=>Candidate.Parameters.Count(Parameter=>Keys.Contains(Parameter.Name)))
            .FirstOrDefault();
    internal static ITypeSymbol?FindSettableMember(INamedTypeSymbol Type,string Name){
        for(ITypeSymbol?Current=Type;Current is not null;Current=Current.BaseType){
            foreach(var Member in Current.GetMembers(Name)){
                if(Member.DeclaredAccessibility!=Accessibility.Public||Member.IsStatic){ continue; }
                if(Member is IFieldSymbol{ IsReadOnly:false,IsConst:false }Field){ return Field.Type; }
                if(Member is IPropertySymbol{ SetMethod:{ DeclaredAccessibility:Accessibility.Public } }Property){ return Property.Type; }
            }
        }
        return null;
    }
    private static string ValueExpression(ITypeSymbol Type,SceneJsonValue Value,string MemberName,Compilation Compilation,List<SceneJsonProblem>Problems){
        string Mismatch(string Reason){
            Problems.Add(new SceneJsonProblem(SceneJsonDiagnostics.ValueMismatch,Value.Start,Value.End,MemberName,Type.ToDisplayString(),Reason));
            return "default";
        }
        if(Value.Kind==SceneJsonKind.Null){
            return Type.IsReferenceType||Type.OriginalDefinition.SpecialType==SpecialType.System_Nullable_T?"null":Mismatch("null is only valid for reference and nullable types");
        }
        if(Type is INamedTypeSymbol{ OriginalDefinition.SpecialType:SpecialType.System_Nullable_T }Nullable){
            return ValueExpression(Nullable.TypeArguments[0],Value,MemberName,Compilation,Problems);
        }
        if(Type.TypeKind==TypeKind.Enum){
            if(Value.Kind!=SceneJsonKind.String){ return Mismatch("an enum value is written as the member name string"); }
            var Member=Type.GetMembers(Value.Text!).OfType<IFieldSymbol>().FirstOrDefault(static Field=>Field.HasConstantValue);
            return Member is null?Mismatch($"'{Value.Text}' is not a member of the enum"):SceneTypePredicates.ToFullyQualifiedName(Type)+"."+Member.Name;
        }
        switch(Type.SpecialType){
            case SpecialType.System_Boolean:
                return Value.Kind==SceneJsonKind.True?"true":Value.Kind==SceneJsonKind.False?"false":Mismatch("expected true or false");
            case SpecialType.System_String:
                return Value.Kind==SceneJsonKind.String?SymbolDisplay.FormatLiteral(Value.Text!,quote:true):Mismatch("expected a string");
            case SpecialType.System_Single:return RealLiteral(Value,"f",Mismatch);
            case SpecialType.System_Double:return RealLiteral(Value,"d",Mismatch);
            case SpecialType.System_Decimal:return RealLiteral(Value,"m",Mismatch);
            case SpecialType.System_SByte:return IntegerLiteral(Value,sbyte.MinValue,(ulong)sbyte.MaxValue,"(sbyte)",Mismatch);
            case SpecialType.System_Byte:return IntegerLiteral(Value,byte.MinValue,byte.MaxValue,"(byte)",Mismatch);
            case SpecialType.System_Int16:return IntegerLiteral(Value,short.MinValue,(ulong)short.MaxValue,"(short)",Mismatch);
            case SpecialType.System_UInt16:return IntegerLiteral(Value,ushort.MinValue,ushort.MaxValue,"(ushort)",Mismatch);
            case SpecialType.System_Int32:return IntegerLiteral(Value,int.MinValue,(ulong)int.MaxValue,string.Empty,Mismatch);
            case SpecialType.System_UInt32:return IntegerLiteral(Value,uint.MinValue,uint.MaxValue,"(uint)",Mismatch);
            case SpecialType.System_Int64:return IntegerLiteral(Value,long.MinValue,(ulong)long.MaxValue,"(long)",Mismatch);
        }
        if(Value.Kind==SceneJsonKind.Number){
            var Single=Compilation.GetSpecialType(SpecialType.System_Single);
            // Every emitted position (named argument, initializer, array or collection element) is
            // target-typed, so the implicit conversion applies without a cast. A cast would also make
            // the value unreadable to the Scene DSL analyzer that the editor runs over this text.
            return Compilation.ClassifyConversion(Single,Type).IsImplicit
                ?Value.Text+"f"
                :Mismatch("a number needs a numeric member or a type convertible from float");
        }
        if(Value.Kind==SceneJsonKind.Array){
            var Element=Type is IArrayTypeSymbol Array?Array.ElementType:ElementTypeOf(Type,Compilation);
            if(Element is null){ return Mismatch("an array needs an array or collection member"); }
            var Items=Value.Items.Select((Item,Index)=>ValueExpression(Element,Item,MemberName+"["+Index.ToString(CultureInfo.InvariantCulture)+"]",Compilation,Problems));
            return Type is IArrayTypeSymbol
                ?"new "+SceneTypePredicates.ToFullyQualifiedName(Element)+"[] { "+string.Join(", ",Items)+" }"
                :"["+string.Join(", ",Items)+"]";
        }
        if(Value.Kind==SceneJsonKind.Object&&Type is INamedTypeSymbol Named&&Named.TypeKind is TypeKind.Struct or TypeKind.Class){
            return ObjectExpression(Named,Value,MemberName,Compilation,Problems);
        }
        return Mismatch("this JSON value cannot be converted to the member's type");
    }
    private static ITypeSymbol?ElementTypeOf(ITypeSymbol Type,Compilation Compilation){
        var Enumerable=Compilation.GetSpecialType(SpecialType.System_Collections_Generic_IEnumerable_T);
        foreach(var Interface in Type.AllInterfaces.Prepend(Type as INamedTypeSymbol).OfType<INamedTypeSymbol>()){
            if(SymbolEqualityComparer.Default.Equals(Interface.OriginalDefinition,Enumerable)){ return Interface.TypeArguments[0]; }
        }
        return null;
    }
    private static string RealLiteral(SceneJsonValue Value,string Suffix,Func<string,string>Mismatch)=>
        Value.Kind==SceneJsonKind.Number?Value.Text+Suffix:Mismatch("expected a number");
    private static string IntegerLiteral(SceneJsonValue Value,long Minimum,ulong Maximum,string Cast,Func<string,string>Mismatch){
        if(Value.Kind!=SceneJsonKind.Number){ return Mismatch("expected an integer"); }
        bool InRange;
        if(long.TryParse(Value.Text,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var Signed)){
            InRange=Signed>=Minimum&&(Signed<0||(ulong)Signed<=Maximum);
        } else if(ulong.TryParse(Value.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var Unsigned)){
            InRange=Unsigned<=Maximum;
        } else {
            return Mismatch("expected an integer without a fraction or exponent");
        }
        if(!InRange){ return Mismatch("the integer is out of range"); }
        // A cast needs parentheses around a negative literal: (short)(-5), not (short)-5.
        return Cast.Length==0?Value.Text!:Cast+"("+Value.Text+")";
    }
    private static void RejectUnknownKeys(SceneJsonValue Object,string[]Allowed,string Kind,List<SceneJsonProblem>Problems){
        foreach(var Property in Object.Properties){
            if(Array.IndexOf(Allowed,Property.Name)<0){
                Problems.Add(new SceneJsonProblem(SceneJsonDiagnostics.InvalidDocument,Property.NameStart,Property.NameStart+Property.Name.Length+2,
                    $"'{Property.Name}' is not a {Kind} key (expected {string.Join(", ",Allowed)})."));
            }
        }
    }
    private static string?OptionalString(SceneJsonValue Object,string Name,List<SceneJsonProblem>Problems){
        if(Object.Get(Name) is not { }Value){ return null; }
        if(Value.Kind!=SceneJsonKind.String){
            Problems.Add(Invalid(Value,$"'{Name}' must be a string."));
            return null;
        }
        return Value.Text;
    }
    private static SceneJsonProblem Invalid(SceneJsonValue At,string Message)=>new(SceneJsonDiagnostics.InvalidDocument,At.Start,At.End,Message);
    private static bool IsIdentifier(string Value)=>SyntaxFacts.IsValidIdentifier(Value)&&SyntaxFacts.GetKeywordKind(Value)==SyntaxKind.None;
}
