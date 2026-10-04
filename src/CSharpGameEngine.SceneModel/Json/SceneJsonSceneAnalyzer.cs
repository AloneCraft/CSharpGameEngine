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
/// <summary>A key that names an angle by its C# name where the current schema writes <see cref="Key"/> (ADR-0074).</summary>
internal readonly struct SceneJsonKeyRename {
    internal SceneJsonKeyRename(int Start,int End,string Name,string Key){
        this.Start=Start;
        this.End=End;
        this.Name=Name;
        this.Key=Key;
    }
    /// <summary>The offsets of the key's text, quotes included.</summary>
    internal int Start { get; }
    internal int End { get; }
    internal string Name { get; }
    internal string Key { get; }
}
/// <summary>A number in the document that an angle member holds (ADR-0074), with the member's C# unit.</summary>
internal readonly struct SceneJsonAngleValue {
    internal SceneJsonAngleValue(int Start,int End,string Text,SceneAngleUnit Unit){
        this.Start=Start;
        this.End=End;
        this.Text=Text;
        this.Unit=Unit;
    }
    internal int Start { get; }
    internal int End { get; }
    internal string Text { get; }
    internal SceneAngleUnit Unit { get; }
}
internal sealed class SceneJsonScene {
    internal SceneJsonScene(string?Namespace,string ClassName,int?StartupOrder,SceneJsonNode?Root,ImmutableArray<SceneJsonProblem>Problems,
        int SchemaVersion=SceneJsonSceneAnalyzer.SchemaVersion,ImmutableArray<SceneJsonAngleValue>AngleValues=default,
        ImmutableArray<SceneJsonKeyRename>KeyRenames=default){
        this.Namespace=Namespace;
        this.ClassName=ClassName;
        this.StartupOrder=StartupOrder;
        this.Root=Root;
        this.Problems=Problems;
        this.SchemaVersion=SchemaVersion;
        this.AngleValues=AngleValues.IsDefault?ImmutableArray<SceneJsonAngleValue>.Empty:AngleValues;
        this.KeyRenames=KeyRenames.IsDefault?ImmutableArray<SceneJsonKeyRename>.Empty:KeyRenames;
    }
    /// <summary>
    /// The document's <c>schemaVersion</c>: 3 writes angle keys without the C# unit word, 2 and 3 store angles in
    /// turns, and 1 holds each member's own unit.
    /// </summary>
    internal int SchemaVersion { get; }
    /// <summary>Every angle key that uses the C# name where the current schema writes the unit-free key.</summary>
    internal ImmutableArray<SceneJsonKeyRename>KeyRenames { get; }
    /// <summary>Every number an angle member holds, in document order.</summary>
    internal ImmutableArray<SceneJsonAngleValue>AngleValues { get; }
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
/// Reads a scene JSON document against a <see cref="Compilation"/> (ADR-0070). Values follow
/// the member's declared C# type; there are no tags in the JSON:
/// <list type="bullet">
/// <item>enums are member-name strings; bool, string and numeric members use JSON literals, and a
/// number's text is emitted unchanged with the literal suffix of the target type;</item>
/// <item>a struct or class value is an object whose keys are constructor parameter names (the public
/// constructor consuming the most keys wins) followed by settable members;</item>
/// <item>a number may also fill a type with an implicit conversion from <c>float</c>
/// (for example <c>LogicalScalar</c>).</item>
/// <item>every number inside a member or constructor parameter marked <c>[Angle]</c> is in turns from schema
/// version 2 on and becomes the member's unit in the emitted literal (ADR-0074); version 1 holds the unit itself.
/// Its key may leave out the C# unit word (<see cref="KeyOf"/>), which version 3 writes; the C# name still reads.</item>
/// </list>
/// Every rejected entry becomes a <see cref="SceneJsonProblem"/> so the whole document is reported at once.
/// </summary>
internal static class SceneJsonSceneAnalyzer {
    /// <summary>The version this code writes (ADR-0074: angles in turns under unit-free keys). Versions 1 and 2 are still read.</summary>
    internal const int SchemaVersion=SceneJsonAngles.UnitFreeKeysSchemaVersion;
    /// <summary>How the numbers of angle members read: in turns or in the member's unit, collected for an upgrade.</summary>
    private sealed class AngleReading {
        internal AngleReading(bool Turns){
            this.Turns=Turns;
        }
        internal bool Turns { get; }
        internal List<SceneJsonAngleValue>Values { get; }=[];
        internal List<SceneJsonKeyRename>KeyRenames { get; }=[];
    }
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
        var DocumentVersion=SchemaVersion;
        if(Version is { Kind:SceneJsonKind.Number,Text:"1" or "2" or "3" }){
            DocumentVersion=int.Parse(Version.Text!,CultureInfo.InvariantCulture);
        } else {
            Problems.Add(Invalid(Version??Document,$"'schemaVersion' must be 1, 2 or {SchemaVersion}."));
        }
        var Angles=new AngleReading(DocumentVersion>=SceneJsonAngles.TurnsSchemaVersion);
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
            Root=AnalyzeNode(RootValue,ImmutableArray<int>.Empty,Compilation,Problems,Angles);
        } else {
            Problems.Add(Invalid(Document,"'root' is required."));
        }
        return new SceneJsonScene(Namespace,ClassName,StartupOrder,Root,Problems.ToImmutableArray(),DocumentVersion,Angles.Values.ToImmutableArray(),
            Angles.KeyRenames.ToImmutableArray());
    }
    private static SceneJsonNode?AnalyzeNode(SceneJsonValue Node,ImmutableArray<int>Path,Compilation Compilation,List<SceneJsonProblem>Problems,AngleReading Angles){
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
                    if(AnalyzeComponent(Item,Compilation,Problems,Angles) is not { }Component){ continue; }
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
                    if(AnalyzeNode(ChildList.Items[Index],Path.Add(Index),Compilation,Problems,Angles) is { }Child){ Children.Add(Child); }
                }
            }
        }
        return new SceneJsonNode(Id,Name,Path,Components.ToImmutable(),Children.ToImmutable());
    }
    private static SceneJsonComponent?AnalyzeComponent(SceneJsonValue Component,Compilation Compilation,List<SceneJsonProblem>Problems,AngleReading Angles){
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
        var Expression=ObjectExpression(Type!,Members,TypeName,Compilation,Problems,Angles,null);
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
    private static string ObjectExpression(INamedTypeSymbol Type,SceneJsonValue?Value,string DisplayName,Compilation Compilation,List<SceneJsonProblem>Problems,
        AngleReading Angles,SceneAngleUnit?Unit){
        var TypeText=SceneTypePredicates.ToFullyQualifiedName(Type);
        var Properties=Value?.Properties??Array.Empty<SceneJsonProperty>();
        var Keys=new HashSet<string>(Properties.Select(static Property=>Property.Name),StringComparer.Ordinal);
        var Constructor=ChooseConstructor(Type,Keys);
        var Arguments=new List<string>();
        var Consumed=new HashSet<string>(StringComparer.Ordinal);
        // An angle named by both its C# name and its unit-free key is reported and set from the first of the two.
        void ReportSameTarget(string Earlier,SceneJsonProperty Later,string Target)=>
            Problems.Add(new SceneJsonProblem(SceneJsonDiagnostics.InvalidDocument,Later.NameStart,Later.NameStart+Later.Name.Length+2,
                $"'{Earlier}' and '{Later.Name}' name the same member '{Target}' of {TypeText.Replace("global::",string.Empty)}; keep one."));
        void RecordKey(SceneJsonProperty Property,string Key){
            if(Property.Name!=Key){
                Angles.KeyRenames.Add(new SceneJsonKeyRename(Property.NameStart,Property.NameStart+Property.Name.Length+2,Property.Name,Key));
            }
        }
        if(Constructor is not null){
            foreach(var Parameter in Constructor.Parameters){
                var Key=KeyOf(Parameter);
                var Property=Properties.FirstOrDefault(Candidate=>Candidate.Name==Parameter.Name||Candidate.Name==Key);
                if(Property is null){ continue; }
                if(Properties.FirstOrDefault(Candidate=>Candidate!=Property&&(Candidate.Name==Parameter.Name||Candidate.Name==Key)) is { }Other){
                    Consumed.Add(Other.Name);
                    ReportSameTarget(Property.Name,Other,Parameter.Name);
                }
                Consumed.Add(Property.Name);
                RecordKey(Property,Key);
                Arguments.Add(Identifier(Parameter.Name)+": "+ValueExpression(Parameter.Type,Property.Value,DisplayName+"."+Property.Name,Compilation,Problems,
                    Angles,Unit??AngleUnitOf(Parameter)));
            }
            // A physical light value's constructor rejects values outside its range when the scene is instantiated,
            // so the build reports them here, as the Scene DSL analyzer does for the editor's model.
            if(Value is not null&&LightValueContract.Find(Type) is { }Physical&&Physical.MatchesConstructor(Constructor)){
                var Numbers=new float[Constructor.Parameters.Length];
                var Complete=true;
                for(var Index=0;Index<Numbers.Length&&Complete;Index++){
                    var Parameter=Constructor.Parameters[Index];
                    var Key=KeyOf(Parameter);
                    var Property=Properties.FirstOrDefault(Candidate=>Candidate.Name==Parameter.Name||Candidate.Name==Key);
                    if(Property is not null&&NumberIn(Property.Value,Angles,Unit??AngleUnitOf(Parameter)) is { }Number){
                        Numbers[Index]=Number;
                    } else {
                        Complete=false;
                    }
                }
                if(Complete&&!Physical.Validate(Numbers)){
                    Problems.Add(new SceneJsonProblem(SceneJsonDiagnostics.ValueMismatch,Value.Start,Value.End,DisplayName,Type.ToDisplayString(),
                        $"a {Physical.Name} needs {Physical.Requirement}"));
                }
            }
        }
        var Assignments=new List<string>();
        var IncompleteConstructorReported=false;
        var SeenMembers=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var Property in Properties){
            if(Consumed.Contains(Property.Name)){ continue; }
            var MemberSymbol=FindSettableMemberSymbol(Type,Property.Name)??FindSettableAngleByKey(Type,Property.Name);
            if(TypeOf(MemberSymbol) is not { }MemberType){
                // A constructor parameter name that no usable constructor consumed means the other
                // required arguments are missing; say which, once, instead of "no such member".
                if(IncompleteConstructorFor(Type,Keys,Property.Name) is { }Incomplete){
                    if(!IncompleteConstructorReported&&Value is not null){
                        IncompleteConstructorReported=true;
                        var Missing=Incomplete.Parameters.Where(Parameter=>!NamesParameter(Keys,Parameter)&&!Parameter.HasExplicitDefaultValue).Select(static Parameter=>"'"+KeyOf(Parameter)+"'");
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
            if(SeenMembers.TryGetValue(MemberSymbol!.Name,out var Earlier)){
                ReportSameTarget(Earlier,Property,MemberSymbol.Name);
                continue;
            }
            SeenMembers.Add(MemberSymbol.Name,Property.Name);
            RecordKey(Property,KeyOf(MemberSymbol));
            Assignments.Add(Identifier(MemberSymbol.Name)+" = "+ValueExpression(MemberType,Property.Value,DisplayName+"."+Property.Name,Compilation,Problems,
                Angles,Unit??AngleUnitOf(MemberSymbol)));
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
                if(NamesParameter(Keys,Parameter)){
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
            .Where(Candidate=>Candidate.Parameters.Any(Parameter=>Parameter.Name==Name||KeyOf(Parameter)==Name))
            .OrderByDescending(Candidate=>Candidate.Parameters.Count(Parameter=>NamesParameter(Keys,Parameter)))
            .FirstOrDefault();
    private static bool NamesParameter(HashSet<string>Keys,IParameterSymbol Parameter)=>
        Keys.Contains(Parameter.Name)||Keys.Contains(KeyOf(Parameter));
    /// <summary>
    /// The key a member or constructor parameter has in the document: an <c>[Angle]</c>'s name without its C# unit
    /// word (ADR-0074, schema version 3), any other name as it is. The C# name of an angle still reads.
    /// </summary>
    internal static string KeyOf(ISymbol Symbol){
        if(AngleUnitOf(Symbol) is null){ return Symbol.Name; }
        var Type=Symbol is IParameterSymbol{ ContainingSymbol:IMethodSymbol Method }?Method.ContainingType:Symbol.ContainingType;
        return SceneJsonAngles.KeyFor(Symbol.Name,Key=>IsNameTaken(Type,Key));
    }
    /// <summary>Whether a type has a member, or a public constructor a parameter, with this name.</summary>
    private static bool IsNameTaken(INamedTypeSymbol?Type,string Name){
        for(ITypeSymbol?Current=Type;Current is not null;Current=Current.BaseType){
            if(!Current.GetMembers(Name).IsEmpty){ return true; }
        }
        return Type is not null&&Type.InstanceConstructors.Any(Constructor=>
            Constructor.DeclaredAccessibility==Accessibility.Public&&Constructor.Parameters.Any(Parameter=>Parameter.Name==Name));
    }
    /// <summary>The settable angle member whose unit-free key is <paramref name="Key"/>.</summary>
    private static ISymbol?FindSettableAngleByKey(INamedTypeSymbol Type,string Key){
        for(ITypeSymbol?Current=Type;Current is not null;Current=Current.BaseType){
            foreach(var Member in Current.GetMembers()){
                if(Member.Name!=Key&&KeyOf(Member)==Key&&FindSettableMemberSymbol(Type,Member.Name) is { }Settable){ return Settable; }
            }
        }
        return null;
    }
    internal static ITypeSymbol?FindSettableMember(INamedTypeSymbol Type,string Name)=>TypeOf(FindSettableMemberSymbol(Type,Name));
    /// <summary>The public instance field or settable property a JSON key assigns.</summary>
    internal static ISymbol?FindSettableMemberSymbol(INamedTypeSymbol Type,string Name){
        for(ITypeSymbol?Current=Type;Current is not null;Current=Current.BaseType){
            foreach(var Member in Current.GetMembers(Name)){
                if(Member.DeclaredAccessibility!=Accessibility.Public||Member.IsStatic){ continue; }
                if(Member is IFieldSymbol{ IsReadOnly:false,IsConst:false }){ return Member; }
                if(Member is IPropertySymbol{ SetMethod:{ DeclaredAccessibility:Accessibility.Public } }){ return Member; }
            }
        }
        return null;
    }
    private static ITypeSymbol?TypeOf(ISymbol?Member)=>Member switch {
        IFieldSymbol Field=>Field.Type,
        IPropertySymbol Property=>Property.Type,
        _=>null,
    };
    /// <summary>The unit an <c>[Angle]</c> attribute declares on a field, property or parameter (ADR-0074).</summary>
    internal static SceneAngleUnit?AngleUnitOf(ISymbol?Symbol){
        if(Symbol is null){ return null; }
        foreach(var Attribute in Symbol.GetAttributes()){
            if(Attribute.AttributeClass?.ToDisplayString()==SceneJsonAngles.AttributeFullName&&Attribute.ConstructorArguments.Length==1){
                return SceneJsonAngles.UnitFromArgument(Attribute.ConstructorArguments[0].Value);
            }
        }
        return null;
    }
    private static string ValueExpression(ITypeSymbol Type,SceneJsonValue Value,string MemberName,Compilation Compilation,List<SceneJsonProblem>Problems,
        AngleReading Angles,SceneAngleUnit?Unit){
        string Mismatch(string Reason){
            Problems.Add(new SceneJsonProblem(SceneJsonDiagnostics.ValueMismatch,Value.Start,Value.End,MemberName,Type.ToDisplayString(),Reason));
            return "default";
        }
        if(Value.Kind==SceneJsonKind.Null){
            return Type.IsReferenceType||Type.OriginalDefinition.SpecialType==SpecialType.System_Nullable_T?"null":Mismatch("null is only valid for reference and nullable types");
        }
        if(Type is INamedTypeSymbol{ OriginalDefinition.SpecialType:SpecialType.System_Nullable_T }Nullable){
            return ValueExpression(Nullable.TypeArguments[0],Value,MemberName,Compilation,Problems,Angles,Unit);
        }
        if(Type.TypeKind==TypeKind.Enum){
            if(Value.Kind!=SceneJsonKind.String){ return Mismatch("an enum value is written as the member name string"); }
            var Member=Type.GetMembers(Value.Text!).OfType<IFieldSymbol>().FirstOrDefault(static Field=>Field.HasConstantValue);
            return Member is null?Mismatch($"'{Value.Text}' is not a member of the enum"):SceneTypePredicates.ToFullyQualifiedName(Type)+"."+Identifier(Member.Name);
        }
        switch(Type.SpecialType){
            case SpecialType.System_Boolean:
                return Value.Kind==SceneJsonKind.True?"true":Value.Kind==SceneJsonKind.False?"false":Mismatch("expected true or false");
            case SpecialType.System_String:
                return Value.Kind==SceneJsonKind.String?SymbolDisplay.FormatLiteral(Value.Text!,quote:true):Mismatch("expected a string");
            case SpecialType.System_Single:return FloatRange(Value,Mismatch)??RealLiteral(AngleText(Value,Angles,Unit),Value,"f",Mismatch);
            case SpecialType.System_Double:return RealLiteral(AngleText(Value,Angles,Unit),Value,"d",Mismatch);
            case SpecialType.System_Decimal:return RealLiteral(AngleText(Value,Angles,Unit),Value,"m",Mismatch);
            case SpecialType.System_SByte:return IntegerLiteral(Value,sbyte.MinValue,(ulong)sbyte.MaxValue,"(sbyte)",Mismatch);
            case SpecialType.System_Byte:return IntegerLiteral(Value,byte.MinValue,byte.MaxValue,"(byte)",Mismatch);
            case SpecialType.System_Int16:return IntegerLiteral(Value,short.MinValue,(ulong)short.MaxValue,"(short)",Mismatch);
            case SpecialType.System_UInt16:return IntegerLiteral(Value,ushort.MinValue,ushort.MaxValue,"(ushort)",Mismatch);
            case SpecialType.System_Int32:return IntegerLiteral(Value,int.MinValue,(ulong)int.MaxValue,string.Empty,Mismatch);
            case SpecialType.System_UInt32:return IntegerLiteral(Value,uint.MinValue,uint.MaxValue,"(uint)",Mismatch);
            case SpecialType.System_Int64:return IntegerLiteral(Value,long.MinValue,(ulong)long.MaxValue,"(long)",Mismatch);
            case SpecialType.System_UInt64:return IntegerLiteral(Value,0,ulong.MaxValue,"(ulong)",Mismatch);
        }
        if(Value.Kind==SceneJsonKind.Number){
            var Single=Compilation.GetSpecialType(SpecialType.System_Single);
            // Every emitted position (named argument, initializer, array or collection element) is
            // target-typed, so the implicit conversion applies without a cast. A cast would also make
            // the value unreadable to the Scene DSL analyzer that the editor runs over this text.
            if(!Compilation.ClassifyConversion(Single,Type).IsImplicit){
                return Mismatch("a number needs a numeric member or a type convertible from float");
            }
            if(FloatRange(Value,Mismatch) is { }OutOfFloat){ return OutOfFloat; }
            // LogicalScalar's conversion from float throws beyond Q64.64 when the scene is instantiated.
            if(SceneTypePredicates.ToFullyQualifiedName(Type)=="global::CSharpGameEngine.Numerics.LogicalScalar"
                &&NumberIn(Value,Angles,Unit) is { }Scalar&&!(Math.Abs(Scalar)<LogicalScalarLimit)){
                return Mismatch("the number is beyond the Q64.64 range of about ±9.2e18");
            }
            return AngleText(Value,Angles,Unit)+"f";
        }
        if(Value.Kind==SceneJsonKind.Array){
            var Element=Type is IArrayTypeSymbol Array?Array.ElementType:ElementTypeOf(Type,Compilation);
            if(Element is null){ return Mismatch("an array needs an array or collection member"); }
            var Items=Value.Items.Select((Item,Index)=>ValueExpression(Element,Item,MemberName+"["+Index.ToString(CultureInfo.InvariantCulture)+"]",Compilation,Problems,Angles,Unit));
            return Type is IArrayTypeSymbol
                ?"new "+SceneTypePredicates.ToFullyQualifiedName(Element)+"[] { "+string.Join(", ",Items)+" }"
                :"["+string.Join(", ",Items)+"]";
        }
        if(Value.Kind==SceneJsonKind.Object&&Type is INamedTypeSymbol Named&&Named.TypeKind is TypeKind.Struct or TypeKind.Class){
            return ObjectExpression(Named,Value,MemberName,Compilation,Problems,Angles,Unit);
        }
        return Mismatch("this JSON value cannot be converted to the member's type");
    }
    /// <summary>
    /// A number's literal text. Inside an angle member it is recorded, and a turns document's value becomes the
    /// member's unit (ADR-0074). The literal is the shortest text of that float in either version, so a version 1
    /// document and its upgrade emit the same text.
    /// </summary>
    private static string AngleText(SceneJsonValue Value,AngleReading Angles,SceneAngleUnit?Unit){
        if(Unit is not { }AngleUnit||Value.Kind!=SceneJsonKind.Number){ return Value.Text??string.Empty; }
        Angles.Values.Add(new SceneJsonAngleValue(Value.Start,Value.End,Value.Text!,AngleUnit));
        if(!SceneJsonAngles.IsNumber(Value.Text)){ return Value.Text!; }
        return SceneJsonNumbers.FormatSingle(Angles.Turns
            ?SceneJsonAngles.FromTurns(Value.Text!,AngleUnit)
            :float.Parse(Value.Text!,NumberStyles.Float,CultureInfo.InvariantCulture));
    }
    private static ITypeSymbol?ElementTypeOf(ITypeSymbol Type,Compilation Compilation){
        var Enumerable=Compilation.GetSpecialType(SpecialType.System_Collections_Generic_IEnumerable_T);
        foreach(var Interface in Type.AllInterfaces.Prepend(Type as INamedTypeSymbol).OfType<INamedTypeSymbol>()){
            if(SymbolEqualityComparer.Default.Equals(Interface.OriginalDefinition,Enumerable)){ return Interface.TypeArguments[0]; }
        }
        return null;
    }
    /// <summary>A member, parameter or enum member name as C# source: a reserved keyword (a field named <c>@class</c>) needs the verbatim prefix.</summary>
    private static string Identifier(string Name)=>SyntaxFacts.GetKeywordKind(Name)==SyntaxKind.None?Name:"@"+Name;
    /// <summary>2^63, the magnitude where a float stops fitting Q64.64's integer part (<c>LogicalScalar.TryFromSingle</c>).</summary>
    private const float LogicalScalarLimit=9.223372e18f;
    /// <summary>A mismatch for a number a float cannot hold (its literal would not compile), or null.</summary>
    private static string?FloatRange(SceneJsonValue Value,Func<string,string>Mismatch)=>
        Value.Kind==SceneJsonKind.Number&&float.TryParse(Value.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var Number)&&float.IsInfinity(Number)
            ?Mismatch("the number is beyond the range of float")
            :null;
    /// <summary>A number in its member's unit (a turns document's angle converted, as the emitted literal is), or null.</summary>
    private static float?NumberIn(SceneJsonValue Value,AngleReading Angles,SceneAngleUnit?Unit){
        if(Value.Kind!=SceneJsonKind.Number||!SceneJsonAngles.IsNumber(Value.Text)){ return null; }
        return Unit is { }AngleUnit&&Angles.Turns
            ?SceneJsonAngles.FromTurns(Value.Text!,AngleUnit)
            :float.Parse(Value.Text!,NumberStyles.Float,CultureInfo.InvariantCulture);
    }
    /// <summary>
    /// A real literal, or a mismatch for a number its type cannot hold: the compiler's lexer decides, as it rejects the
    /// generated literal (CS0594). A JSON minus sign is a separate C# token, so it is left out of the check.
    /// </summary>
    private static string RealLiteral(string Text,SceneJsonValue Value,string Suffix,Func<string,string>Mismatch){
        if(Value.Kind!=SceneJsonKind.Number){ return Mismatch("expected a number"); }
        if(SyntaxFactory.ParseToken(Text.TrimStart('-')+Suffix).ContainsDiagnostics){
            return Mismatch("the number is beyond the range of "+(Suffix=="m"?"decimal":"double"));
        }
        return Text+Suffix;
    }
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
