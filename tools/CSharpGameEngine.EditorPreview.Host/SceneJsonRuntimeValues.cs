using System.Collections;
using System.Globalization;
using System.Reflection;
using CSharpGameEngine.SceneAnalysis.Json;

namespace CSharpGameEngine.EditorPreview.Host;

/// <summary>
/// Turns a scene JSON value into a value of the game's own type by reflection (ADR-0070 decision 6).
/// It follows the generator's rules (<c>SceneJsonSceneAnalyzer</c>): enums are member names, numbers
/// fill numeric members or types with an implicit conversion from <c>float</c>, and objects use the
/// public constructor that consumes the most keys and then settable members. The runtime view uses it
/// to apply the editor's values to the running world; the generator has already checked the document.
/// In a schema version 2 or later document every number inside an <c>[Angle]</c> member is in turns, and an angle's key
/// may leave out the C# unit word (ADR-0074, <see cref="SceneJsonAngles.KeyFor"/>).
/// </summary>
internal static class SceneJsonRuntimeValues {
    internal static object?Create(Type Type,SceneJsonValue Value,string Where)=>Create(Type,Value,Where,new Reading(Turns:false),null);

    /// <summary>A component: its members object, read as the component type's object value.</summary>
    internal static object Component(Type Type,SceneJsonValue Members,string Where,bool Turns=false)=>
        Object(Type,Members,Where,new Reading(Turns),null);

    /// <summary>Whether the document's angles are turns: <c>schemaVersion</c> 2 or later.</summary>
    internal static bool UsesTurns(SceneJsonValue Document)=>
        Document.Get("schemaVersion") is { Kind:SceneJsonKind.Number }Version
        &&int.TryParse(Version.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var Number)
        &&Number>=SceneJsonAngles.TurnsSchemaVersion;

    private sealed record Reading(bool Turns);

    private static object?Create(Type Type,SceneJsonValue Value,string Where,Reading Reading,SceneAngleUnit?Unit){
        if(Value.Kind==SceneJsonKind.Null){
            return !Type.IsValueType||Nullable.GetUnderlyingType(Type) is not null
                ?null
                :throw Mismatch(Where,Type,"null is only valid for reference and nullable types");
        }
        if(Nullable.GetUnderlyingType(Type) is { }Underlying){ return Create(Underlying,Value,Where,Reading,Unit); }
        if(Type.IsEnum){
            return Value.Kind==SceneJsonKind.String&&Enum.GetNames(Type).Contains(Value.Text,StringComparer.Ordinal)
                ?Enum.Parse(Type,Value.Text!)
                :throw Mismatch(Where,Type,"an enum value is written as the member name string");
        }
        if(Type==typeof(bool)){
            return Value.Kind switch {
                SceneJsonKind.True=>true,
                SceneJsonKind.False=>false,
                _=>throw Mismatch(Where,Type,"expected true or false"),
            };
        }
        if(Type==typeof(string)){
            return Value.Kind==SceneJsonKind.String?Value.Text!:throw Mismatch(Where,Type,"expected a string");
        }
        if(Value.Kind==SceneJsonKind.Number){
            // An angle in turns becomes the member's unit; the rest of the number rules then apply unchanged.
            var Text=Unit is { }AngleUnit&&Reading.Turns&&SceneJsonAngles.IsNumber(Value.Text)
                ?SceneJsonAngles.FromTurns(Value.Text!,AngleUnit).ToString("R",CultureInfo.InvariantCulture)
                :Value.Text!;
            return Number(Type,Text,Where);
        }
        if(Value.Kind==SceneJsonKind.Array){ return Collection(Type,Value,Where,Reading,Unit); }
        if(Value.Kind==SceneJsonKind.Object&&(Type.IsValueType||Type.IsClass)){ return Object(Type,Value,Where,Reading,Unit); }
        throw Mismatch(Where,Type,"this JSON value cannot be converted to the member's type");
    }

    /// <summary>The unit an <c>[Angle]</c> attribute declares, matched by name so the game's own load of Contracts counts.</summary>
    private static SceneAngleUnit?AngleUnitOf(IEnumerable<CustomAttributeData>Attributes){
        foreach(var Attribute in Attributes){
            if(Attribute.AttributeType.FullName==SceneJsonAngles.AttributeFullName&&Attribute.ConstructorArguments is [{ }Argument]){
                return SceneJsonAngles.UnitFromArgument(Argument.Value);
            }
        }
        return null;
    }

    private static object Number(Type Type,string Text,string Where){
        IFormatProvider Culture=CultureInfo.InvariantCulture;
        if(!Type.IsEnum&&Type.IsPrimitive&&Type!=typeof(bool)&&Type!=typeof(char)||Type.GetTypeCode(Type)==TypeCode.Decimal){
            try {
                return Convert.ChangeType(Text,Type,Culture);
            } catch(Exception Error)when(Error is FormatException or OverflowException or InvalidCastException){
                throw Mismatch(Where,Type,Error.Message);
            }
        }
        // The generator writes the literal with an f suffix into a target-typed position, so the type's
        // implicit conversion from float (or a wider real reached from float) applies.
        foreach(var Conversion in Type.GetMethods(BindingFlags.Public|BindingFlags.Static)){
            if(Conversion.Name=="op_Implicit"&&Conversion.ReturnType==Type
                &&Conversion.GetParameters() is [{ }Only]
                &&Type.GetTypeCode(Only.ParameterType) is TypeCode.Single or TypeCode.Double){
                return Conversion.Invoke(null,[Convert.ChangeType(float.Parse(Text,Culture),Only.ParameterType,Culture)])!;
            }
        }
        throw Mismatch(Where,Type,"a number needs a numeric member or a type convertible from float");
    }

    private static object Collection(Type Type,SceneJsonValue Value,string Where,Reading Reading,SceneAngleUnit?Unit){
        var Element=Type.IsArray?Type.GetElementType():ElementType(Type);
        if(Element is null){ throw Mismatch(Where,Type,"an array needs an array or collection member"); }
        var Items=Array.CreateInstance(Element,Value.Items.Count);
        for(var Index=0;Index<Items.Length;Index++){
            Items.SetValue(Create(Element,Value.Items[Index],Where+"["+Index.ToString(CultureInfo.InvariantCulture)+"]",Reading,Unit),Index);
        }
        if(Type.IsAssignableFrom(Items.GetType())){ return Items; }
        if(Type.IsGenericType&&Type.GetGenericTypeDefinition().FullName=="System.Collections.Immutable.ImmutableArray`1"){
            var Create=Type.Assembly.GetType("System.Collections.Immutable.ImmutableArray")!
                .GetMethods().First(static Method=>Method.Name=="Create"&&Method.GetParameters() is [{ ParameterType.IsArray:true }]);
            return Create.MakeGenericMethod(Element).Invoke(null,[Items])!;
        }
        if(Activator.CreateInstance(Type) is IList List){
            foreach(var Item in Items){ List.Add(Item); }
            return List;
        }
        throw Mismatch(Where,Type,"this collection type cannot be filled from a JSON array");
    }

    private static Type?ElementType(Type Type)=>
        Type.GetInterfaces().Prepend(Type)
            .FirstOrDefault(static Interface=>Interface.IsGenericType&&Interface.GetGenericTypeDefinition()==typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];

    private static object Object(Type Type,SceneJsonValue Value,string Where,Reading Reading,SceneAngleUnit?Unit){
        var Keys=Value.Properties.Select(static Property=>Property.Name).ToHashSet(StringComparer.Ordinal);
        var Constructor=ChooseConstructor(Type,Keys);
        HashSet<string>Consumed=new(StringComparer.Ordinal);
        object Instance;
        if(Constructor is not null){
            var Arguments=Constructor.GetParameters().Select(Parameter=>{
                var Key=KeyOf(Type,Parameter.Name!,Parameter.CustomAttributes);
                if((Value.Get(Parameter.Name!)??Value.Get(Key)) is { }Argument){
                    Consumed.Add(Parameter.Name!);
                    Consumed.Add(Key);
                    return Create(Parameter.ParameterType,Argument,Where+"."+Parameter.Name,Reading,Unit??AngleUnitOf(Parameter.CustomAttributes));
                }
                return Parameter.DefaultValue;
            }).ToArray();
            Instance=Constructor.Invoke(Arguments);
        } else {
            // A struct's parameterless constructor runs its field initializers, as `new T { ... }` does.
            Instance=Activator.CreateInstance(Type)
                ??throw Mismatch(Where,Type,"the type cannot be constructed");
        }
        foreach(var Property in Value.Properties){
            if(Consumed.Contains(Property.Name)){ continue; }
            switch(FindSettableMember(Type,Property.Name)){
                case FieldInfo Field:
                    Field.SetValue(Instance,Create(Field.FieldType,Property.Value,Where+"."+Property.Name,Reading,Unit??AngleUnitOf(Field.CustomAttributes)));
                    break;
                case PropertyInfo Member:
                    Member.SetValue(Instance,Create(Member.PropertyType,Property.Value,Where+"."+Property.Name,Reading,Unit??AngleUnitOf(Member.CustomAttributes)));
                    break;
                default:
                    throw Mismatch(Where,Type,$"'{Property.Name}' is not a settable member or constructor parameter");
            }
        }
        return Instance;
    }

    /// <summary>The generator's choice: the public constructor whose parameters are all named or defaulted and that consumes the most keys.</summary>
    private static ConstructorInfo?ChooseConstructor(Type Type,HashSet<string>Keys){
        ConstructorInfo?Best=null;
        var BestCount=-1;
        foreach(var Candidate in Type.GetConstructors(BindingFlags.Public|BindingFlags.Instance)){
            var Count=0;
            var Usable=true;
            foreach(var Parameter in Candidate.GetParameters()){
                if(Keys.Contains(Parameter.Name!)||Keys.Contains(KeyOf(Type,Parameter.Name!,Parameter.CustomAttributes))){
                    Count++;
                } else if(!Parameter.HasDefaultValue){
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

    /// <summary>The settable member a key names: by its C# name, or an angle by its unit-free key.</summary>
    private static MemberInfo?FindSettableMember(Type Type,string Name){
        if(FindSettableMemberNamed(Type,Name) is { }Named){ return Named; }
        foreach(var Member in Type.GetMembers(BindingFlags.Public|BindingFlags.Instance)){
            if(Member.Name!=Name&&KeyOf(Type,Member.Name,Member.CustomAttributes)==Name&&FindSettableMemberNamed(Type,Member.Name) is { }Angle){
                return Angle;
            }
        }
        return null;
    }

    private static MemberInfo?FindSettableMemberNamed(Type Type,string Name){
        if(Type.GetField(Name,BindingFlags.Public|BindingFlags.Instance) is { IsInitOnly:false,IsLiteral:false }Field){ return Field; }
        return Type.GetProperty(Name,BindingFlags.Public|BindingFlags.Instance) is { SetMethod.IsPublic:true }Property?Property:null;
    }

    /// <summary>The key of a member or constructor parameter, by the generator's rule (<c>SceneJsonSceneAnalyzer.KeyOf</c>).</summary>
    private static string KeyOf(Type Type,string Name,IEnumerable<CustomAttributeData>Attributes)=>
        AngleUnitOf(Attributes) is null?Name:SceneJsonAngles.KeyFor(Name,Key=>
            Type.GetMember(Key,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static).Length>0
            ||Type.GetConstructors(BindingFlags.Public|BindingFlags.Instance).Any(Constructor=>Constructor.GetParameters().Any(Parameter=>Parameter.Name==Key)));

    private static InvalidOperationException Mismatch(string Where,Type Type,string Reason)=>
        new($"The value for '{Where}' does not fit '{Type.FullName}': {Reason}.");
}
