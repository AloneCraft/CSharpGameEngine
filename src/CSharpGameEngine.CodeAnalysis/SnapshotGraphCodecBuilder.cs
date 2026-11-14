using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
namespace CSharpGameEngine.CodeAnalysis;
/// <summary>閉じた値型グラフから、型検索を含まない読み書きのローカル関数群を生成する。</summary>
internal sealed class SnapshotGraphCodecBuilder {
    private readonly Dictionary<ITypeSymbol,int>Indices=new(SymbolEqualityComparer.Default);
    private readonly HashSet<ITypeSymbol>Active=new(SymbolEqualityComparer.Default);
    private readonly List<string>Writes=[];
    private readonly List<string>Reads=[];
    internal static (string Writer,string Reader)?BuildPortable(INamedTypeSymbol Type){
        try{
            var Builder=new SnapshotGraphCodecBuilder();
            Builder.Visit(Type);
            var Writer="using var Buffer = new global::System.IO.MemoryStream();\n"
                +"using (var Writer = new global::System.Text.Json.Utf8JsonWriter(Buffer)) { Write0(Writer, Value); }\n"
                +"return Buffer.ToArray();\n"+string.Join("\n",Builder.Writes);
            var Parse=
                "global::CSharpGameEngine.Entities.EcsSnapshotValueCodec.AssertCanonicalJson(Payload, Document.RootElement);\n"
                +"using var Normalized = new global::System.IO.MemoryStream();\n"+Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)+" Parsed;\n"
                +"using (var Writer = new global::System.Text.Json.Utf8JsonWriter(Normalized)) { Parsed = Read0(Document.RootElement, Writer); }\n"
                +"if (!global::System.MemoryExtensions.SequenceEqual<byte>(Normalized.ToArray(), $SERIALIZE$(in Parsed))) throw new global::System.Text.Json.JsonException(\"Snapshot constructor changed persisted state.\");\n";
            var Reader="Value = default; Error = string.Empty;\n"
                +"if (Payload is null || Payload.Length == 0) { Error = \"Empty snapshot component payload.\"; return false; }\n"
                +"try {\nusing var Document = global::System.Text.Json.JsonDocument.Parse(Payload);\n"+Parse
                +"Value = Parsed; return true;\n"
                +"} catch (global::System.Exception Failure) when (Failure is global::System.Text.Json.JsonException or global::System.InvalidOperationException or global::System.Collections.Generic.KeyNotFoundException or global::System.FormatException or global::System.OverflowException or global::System.ArgumentException) { Error = Failure.Message; return false; }\n"
                +string.Join("\n",Builder.Reads);
            return(Writer,Reader);
        }catch(NotSupportedException){ return null; }
    }
    private int Visit(ITypeSymbol Type){
        if(this.Active.Contains(Type)){ throw new NotSupportedException("Recursive snapshot values are unsupported."); }
        if(this.Indices.TryGetValue(Type,out var Existing)){ return Existing; }
        if(this.Indices.Count>=256){ throw new NotSupportedException("Snapshot value graph exceeds the generation limit."); }
        var Index=this.Indices.Count;
        this.Indices.Add(Type,Index);
        this.Writes.Add(string.Empty);this.Reads.Add(string.Empty);
        this.Active.Add(Type);
        var Name=Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var Methods=ScalarMethods(Type.SpecialType);
        string Write;
        string Read;
        if(Type.SpecialType is SpecialType.System_Single or SpecialType.System_Double){
            var Suffix=Type.SpecialType==SpecialType.System_Single?"Single":"Double";
            Write=$"global::CSharpGameEngine.Entities.EcsSnapshotValueCodec.Write{Suffix}(Writer, Value);";
            Read=$"var Value = global::CSharpGameEngine.Entities.EcsSnapshotValueCodec.Read{Suffix}(Element);\n{Write}\nreturn Value;";
        }else if(Name=="global::CSharpGameEngine.Numerics.LogicalScalar"){
            Write="Writer.WriteStringValue(Value.ToStringInvariant());";
            Read=$"if (!{Name}.TryReadCanonical(global::CSharpGameEngine.Entities.EcsSnapshotValueCodec.ReadNumber(Element), out var Value)) throw new global::System.Text.Json.JsonException(\"Snapshot number is outside the selected range.\");\n{Write}\nreturn Value;";
        }else if(Methods.Write is not null){
            Write=$"Writer.{Methods.Write}(Value);";
            Read=$"var Value = Element.{Methods.Read};\n"
                +(Methods.Write=="WriteNumberValue"?"global::CSharpGameEngine.Entities.EcsSnapshotValueCodec.AssertNumber(Element, Value.ToString(global::System.Globalization.CultureInfo.InvariantCulture));\n":string.Empty)
                +Write+"\nreturn Value;";
        }else if(Type.SpecialType==SpecialType.System_Char){
            Write="Writer.WriteStringValue(Value.ToString());";
            Read="var Text = Element.GetString(); if (Text is null || Text.Length != 1) throw new global::System.Text.Json.JsonException(\"Invalid character.\"); "
                +"var Value = Text[0]; "+Write+" return Value;";
        }else if(Name is "global::System.Int128" or "global::System.UInt128"){
            Write="Writer.WriteRawValue(Value.ToString(global::System.Globalization.CultureInfo.InvariantCulture), true);";
            Read=$"var Value = {Name}.Parse(Element.GetRawText(), global::System.Globalization.CultureInfo.InvariantCulture);\nglobal::CSharpGameEngine.Entities.EcsSnapshotValueCodec.AssertNumber(Element, Value.ToString(global::System.Globalization.CultureInfo.InvariantCulture));\n{Write}\nreturn Value;";
        }else if(StringValue(Type) is {}Parser){
            Write="Writer.WriteStringValue(Value."+(Parser=="ParseInvariant"?"ToStringInvariant()":"ToString()")+");";
            Read=$"var Value = {Name}.{Parser}(Element.GetString()!);\nglobal::CSharpGameEngine.Entities.EcsSnapshotValueCodec.AssertText(Element, Value.ToString());\n{Write}\nreturn Value;";
        }else if(Type is INamedTypeSymbol {OriginalDefinition.SpecialType:SpecialType.System_Nullable_T}Nullable){
            var Child=this.Visit(Nullable.TypeArguments[0]);
            Write=$"if (Value.HasValue) Write{Child}(Writer, Value.Value); else Writer.WriteNullValue();";
            Read=$"if (Element.ValueKind == global::System.Text.Json.JsonValueKind.Null) {{ Writer.WriteNullValue(); return default({Name}); }} return Read{Child}(Element, Writer);";
        }else if(Type is INamedTypeSymbol {TypeKind:TypeKind.Enum}Enum){
            var Child=this.Visit(Enum.EnumUnderlyingType!);
            var Underlying=Enum.EnumUnderlyingType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            Write=$"Write{Child}(Writer, ({Underlying})Value);";
            Read=$"return ({Name})Read{Child}(Element, Writer);";
        }else if(Type is INamedTypeSymbol {IsValueType:true}Structure){
            (Write,Read)=this.Object(Structure);
        }else{ throw new NotSupportedException("Snapshot values must be strings or supported value types."); }
        var WriteType=Type.SpecialType==SpecialType.System_String?Name+"?":Name;
        this.Writes[Index]=$"static void Write{Index}(global::System.Text.Json.Utf8JsonWriter Writer, {WriteType} Value) {{\n{Write}\n}}";
        this.Reads[Index]=$"static {Name} Read{Index}(global::System.Text.Json.JsonElement Element, global::System.Text.Json.Utf8JsonWriter Writer) {{\n{Read}\n}}";
        this.Active.Remove(Type);
        return Index;
    }
    private (string Write,string Read)Object(INamedTypeSymbol Type){
        if(HasJsonRules(Type)){ throw new NotSupportedException("Custom JSON type rules require a dedicated static codec."); }
        var Fields=Type.GetMembers().OfType<IFieldSymbol>().Where(static Field=>!Field.IsStatic&&Field.DeclaredAccessibility==Accessibility.Public).ToArray();
        var Properties=Type.GetMembers().OfType<IPropertySymbol>().Where(static Property=>!Property.IsStatic&&!Property.IsIndexer&&Property.DeclaredAccessibility==Accessibility.Public).ToArray();
        var Constructors=Type.InstanceConstructors.Where(static Constructor=>!Constructor.IsImplicitlyDeclared&&Constructor.DeclaredAccessibility==Accessibility.Public).ToArray();
        var Constructed=Fields.Length==0&&Properties.All(static Property=>Property.SetMethod is null)&&Constructors.Length==1&&Constructors[0].Parameters.Length>0;
        var Members=new List<Member>();
        var Arguments=new List<string>();
        if(Fields.Any(static Field=>Field.IsReadOnly)){
            var Candidates=Constructors.Where(Constructor=>Constructor.Parameters.Length==Fields.Length&&Properties.Length==0&&Constructor.Parameters.All(Parameter=>Parameter.RefKind==RefKind.None&&Fields.Any(Field=>string.Equals(Field.Name,Parameter.Name,StringComparison.OrdinalIgnoreCase)&&SymbolEqualityComparer.Default.Equals(Field.Type,Parameter.Type)))).ToArray();
            if(Candidates.Length!=1){ throw new NotSupportedException("Readonly fields require one matching public constructor."); }
            foreach(var Parameter in Candidates[0].Parameters){
                var Field=Fields.First(Field=>string.Equals(Field.Name,Parameter.Name,StringComparison.OrdinalIgnoreCase));
                Members.Add(CreateMember(Field,Field.Type,true));Arguments.Add(Field.Name);
            }
            Constructed=true;
        }else if(Constructed){
            foreach(var Parameter in Constructors[0].Parameters){
                var Property=Properties.FirstOrDefault(Candidate=>Candidate.GetMethod?.DeclaredAccessibility==Accessibility.Public&&string.Equals(Candidate.Name,Parameter.Name,StringComparison.OrdinalIgnoreCase)&&SymbolEqualityComparer.Default.Equals(Candidate.Type,Parameter.Type));
                if(Property is null){ throw new NotSupportedException("Constructor state cannot be resolved statically."); }
                Members.Add(CreateMember(Property,Property.Type,true));
                Arguments.Add(Property.Name);
            }
        }else{
            foreach(var Field in Fields){
                Members.Add(CreateMember(Field,Field.Type,true));
            }
            foreach(var Property in Properties.Where(static Property=>Property.GetMethod?.DeclaredAccessibility==Accessibility.Public)){
                Members.Add(CreateMember(Property,Property.Type,Property.SetMethod?.DeclaredAccessibility==Accessibility.Public));
            }
        }
        Members=Members.Where(static Item=>!Item.Ignored).OrderBy(static Item=>Item.JsonName,StringComparer.Ordinal).ToList();
        if(Members.Select(static Item=>Item.JsonName).Distinct(StringComparer.Ordinal).Count()!=Members.Count){ throw new NotSupportedException("Duplicate serialized member names."); }
        var Writes=new List<string>{"Writer.WriteStartObject();"};
        var Reads=new List<string>();
        Reads.Add("global::CSharpGameEngine.Entities.EcsSnapshotValueCodec.AssertObject(Element"+string.Concat(Members.Select(static Member=>", "+SymbolDisplay.FormatLiteral(Member.JsonName,true)))+");");Reads.Add("Writer.WriteStartObject();");
        var Values=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var Member in Members){
            var Child=this.Visit(Member.Type);
            var Literal=SymbolDisplay.FormatLiteral(Member.JsonName,true);
            Writes.Add($"Writer.WritePropertyName({Literal}); Write{Child}(Writer, Value.@{Member.Name});");
            var Value="MemberValue"+Reads.Count;
            Reads.Add($"Writer.WritePropertyName({Literal}); var {Value} = Read{Child}(Element.GetProperty({Literal}), Writer);");
            if(Member.Writable){ Values.Add(Member.Name,Value); }
        }
        Writes.Add("Writer.WriteEndObject();");
        var Name=Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if(Constructed&&Arguments.Any(Argument=>!Values.ContainsKey(Argument))){ throw new NotSupportedException("Ignored constructor state requires a dedicated static contract."); }
        var NewValue=Constructed?$"new {Name}({string.Join(", ",Arguments.Select(Argument=>Values[Argument]))})"
            :$"new {Name} {{ {string.Join(", ",Values.Select(static Pair=>"@"+Pair.Key+" = "+Pair.Value))} }}";
        Reads.Add("Writer.WriteEndObject();");
        return(string.Join("\n",Writes),string.Join("\n",Reads)+"\nreturn "+NewValue+";");
    }
    private static Member CreateMember(ISymbol Symbol,ITypeSymbol Type,bool Writable){
        var Name=Symbol.Name;
        var Ignored=false;
        foreach(var Attribute in Symbol.GetAttributes()){
            if(Attribute.AttributeClass?.ContainingNamespace.ToDisplayString()!="System.Text.Json.Serialization"){ continue; }
            switch(Attribute.AttributeClass.Name){
                case "JsonPropertyNameAttribute":
                    if(Attribute.ConstructorArguments[0].Value is not string JsonName){ throw new NotSupportedException(); }
                    Name=JsonName;
                    break;
                case "JsonIgnoreAttribute":
                    var Condition=Attribute.NamedArguments.FirstOrDefault(static Argument=>Argument.Key=="Condition");
                    var Value=Condition.Key is null?1:Convert.ToInt32(Condition.Value.Value,global::System.Globalization.CultureInfo.InvariantCulture);
                    if(Value is not (0 or 1)){ throw new NotSupportedException("Conditional omission requires a dedicated static contract."); }
                    Ignored=Value==1;
                    break;
                default:throw new NotSupportedException("Unsupported JSON member rule.");
            }
        }
        return new(Symbol.Name,Name,Type,Writable,Ignored);
    }
    private sealed class Member(string Name,string JsonName,ITypeSymbol Type,bool Writable,bool Ignored) {
        internal readonly string Name=Name;
        internal readonly string JsonName=JsonName;
        internal readonly ITypeSymbol Type=Type;
        internal readonly bool Writable=Writable;
        internal readonly bool Ignored=Ignored;
    }
    private static bool HasJsonRules(ISymbol Symbol)=>Symbol.GetAttributes().Any(static Attribute=>Attribute.AttributeClass?.ContainingNamespace.ToDisplayString()=="System.Text.Json.Serialization");
    private static string?StringValue(ITypeSymbol Type)=>Type.ToDisplayString() switch{
        "CSharpGameEngine.Numerics.LogicalScalar"=>"ParseInvariant",
        "CSharpGameEngine.Core.StableId" or "CSharpGameEngine.Contracts.ReferenceFrameId" or "CSharpGameEngine.Contracts.WorldCellId"
            or "CSharpGameEngine.Contracts.PersistentEntityId" or "CSharpGameEngine.Contracts.Hash256" or "CSharpGameEngine.Contracts.AssetId" or "CSharpGameEngine.Contracts.SubAssetId"=>"Parse",
        _=>null,
    };
    private static (string?Write,string?Read)ScalarMethods(SpecialType Type)=>Type switch{
        SpecialType.System_Boolean=>("WriteBooleanValue","GetBoolean()"),
        SpecialType.System_String=>("WriteStringValue","GetString()!"),
        SpecialType.System_Byte=>("WriteNumberValue","GetByte()"),SpecialType.System_SByte=>("WriteNumberValue","GetSByte()"),
        SpecialType.System_Int16=>("WriteNumberValue","GetInt16()"),SpecialType.System_UInt16=>("WriteNumberValue","GetUInt16()"),
        SpecialType.System_Int32=>("WriteNumberValue","GetInt32()"),SpecialType.System_UInt32=>("WriteNumberValue","GetUInt32()"),
        SpecialType.System_Int64=>("WriteNumberValue","GetInt64()"),SpecialType.System_UInt64=>("WriteNumberValue","GetUInt64()"),
        SpecialType.System_Single=>("WriteNumberValue","GetSingle()"),SpecialType.System_Double=>("WriteNumberValue","GetDouble()"),
        SpecialType.System_Decimal=>("WriteNumberValue","GetDecimal()"),
        _=>(null,null),
    };
}
