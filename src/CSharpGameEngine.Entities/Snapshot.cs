using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Core;
using CSharpGameEngine.Numerics;

namespace CSharpGameEngine.Entities;

public enum EcsSnapshotErrorCode {
    None,InvalidHeader,UnsupportedVersion,InvalidLength,ChecksumMismatch,InvalidPayload,
    SchemaMismatch,WorldBusy,CellMismatch,EntityMissing,ArchetypeMismatch,ComponentMismatch,
}

public readonly record struct EcsSnapshotError(EcsSnapshotErrorCode Code,string Message) {
    public static EcsSnapshotError None=>new(EcsSnapshotErrorCode.None,string.Empty);
}

public sealed record EcsSnapshotComponentData(string TypeId,bool Enabled,byte[]Payload);
public sealed record EcsSnapshotEntityData(WorldCellId Cell,PersistentEntityId Persistent,string ArchetypeKey,bool Enabled,IReadOnlyList<EcsSnapshotComponentData>Components);

public sealed class EcsSnapshotDocument {
    internal EcsSnapshotDocument(string SchemaFingerprint,ulong Tick,string StateHash,string EventHash,IReadOnlyList<WorldCellId>CapturedCells,IReadOnlyList<EcsSnapshotEntityData>Entities){
        this.SchemaFingerprint=SchemaFingerprint;
        this.Tick=Tick;
        this.StateHash=StateHash;
        this.EventHash=EventHash;
        this.CapturedCells=CapturedCells;
        this.Entities=Entities;
    }
    public string SchemaFingerprint{get;}
    public ulong Tick{get;}
    public string StateHash{get;}
    public string EventHash{get;}
    public IReadOnlyList<WorldCellId>CapturedCells{get;}
    public IReadOnlyList<EcsSnapshotEntityData>Entities{get;}
}

public sealed class EcsSnapshotPending {
    internal EcsSnapshotPending(EcsSnapshotDocument Document){ this.Document=Document; }
    internal EcsSnapshotDocument Document{get;}
    public int CellCount=>this.Document.CapturedCells.Count;
    public int EntityCount=>this.Document.Entities.Count;
    public bool IsEmpty=>this.CellCount==0;
    public ulong Tick=>this.Document.Tick;
    public string StateHash=>this.Document.StateHash;
    public string EventHash=>this.Document.EventHash;
}

public readonly record struct EcsSnapshotRestoreResult(bool Succeeded,int AppliedEntityCount,int RemovedEntityCount,EcsSnapshotPending?Pending,EcsSnapshotError Error) {
    internal static EcsSnapshotRestoreResult Reject(EcsSnapshotErrorCode Code,string Message)=>new(false,0,0,null,new EcsSnapshotError(Code,Message));
}

public sealed record EcsSnapshotComponentDescriptor(string TypeId,bool Transient,string Shape);

public sealed class EcsSnapshotArchetypeDescriptor {
    private EcsSnapshotArchetypeDescriptor(string Key,IReadOnlyList<EcsSnapshotComponentDescriptor>Components){ this.Key=Key;this.Components=Components; }
    public string Key{get;}
    public IReadOnlyList<EcsSnapshotComponentDescriptor>Components{get;}
    public static EcsSnapshotArchetypeDescriptor Create(params EcsSnapshotComponentDescriptor[]Components){
        ArgumentNullException.ThrowIfNull(Components);
        var Ordered=Components.OrderBy(static Component=>Component.TypeId,StringComparer.Ordinal).ToArray();
        if(Ordered.Length==0||Ordered.Any(static Component=>string.IsNullOrWhiteSpace(Component.TypeId)||string.IsNullOrWhiteSpace(Component.Shape))){ throw new InvalidOperationException("Snapshot archetype components must have canonical descriptors."); }
        if(Ordered.Select(static Component=>Component.TypeId).Distinct(StringComparer.Ordinal).Count()!=Ordered.Length){ throw new InvalidOperationException("Snapshot archetype component IDs must be unique."); }
        using var Hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach(var Component in Ordered){ Append(Hash,Component.TypeId);Append(Hash,Component.Transient?"transient":"persistent");Append(Hash,Component.Shape); }
        return new EcsSnapshotArchetypeDescriptor(Convert.ToHexString(Hash.GetHashAndReset()).ToLowerInvariant(),Ordered);
    }
    private static void Append(IncrementalHash Hash,string Value){
        byte[]Bytes=Encoding.UTF8.GetBytes(Value);
        Span<byte>Length=stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(Length,Bytes.Length);
        Hash.AppendData(Length);
        Hash.AppendData(Bytes);
    }
}

public static class EcsSnapshotSchema {
    public static string ComputeFingerprint(params EcsSnapshotArchetypeDescriptor[]Archetypes){
        ArgumentNullException.ThrowIfNull(Archetypes);
        string[]Keys=Archetypes.Select(static Archetype=>Archetype.Key).Order(StringComparer.Ordinal).ToArray();
        if(Keys.Distinct(StringComparer.Ordinal).Count()!=Keys.Length){ throw new InvalidOperationException("Snapshot archetype schemas must be unique."); }
        using var Hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach(string Key in Keys){ Hash.AppendData(Convert.FromHexString(Key)); }
        return Convert.ToHexString(Hash.GetHashAndReset()).ToLowerInvariant();
    }
}

public static class EcsSnapshotComponentCodec<T>where T:struct {
    private static readonly JsonSerializerOptions Options=CreateOptions();
    public static bool IsTransient{get;}=typeof(IRuntimeComponent).IsAssignableFrom(typeof(T))||typeof(T).IsDefined(typeof(EcsSnapshotTransientAttribute),false);
    public static EcsSnapshotComponentDescriptor Descriptor{get;}=new(CanonicalTypeId(typeof(T)),IsTransient,IsTransient?"transient":Describe(typeof(T),new HashSet<Type>()));
    public static byte[]Serialize(in T Value){
        if(IsTransient){ throw new InvalidOperationException($"Transient snapshot component {Descriptor.TypeId} has no payload."); }
        return JsonSerializer.SerializeToUtf8Bytes(Value,Options);
    }
    public static bool TryDeserialize(byte[]Payload,out T Value,out string Error){
        Value=default;
        Error=string.Empty;
        if(IsTransient){ Error=$"Transient snapshot component {Descriptor.TypeId} cannot be deserialized.";return false; }
        if(Payload is null||Payload.Length==0){ Error=$"Snapshot component {Descriptor.TypeId} has an empty payload.";return false; }
        try {
            T Parsed=JsonSerializer.Deserialize<T>(Payload,Options);
            byte[]Canonical=JsonSerializer.SerializeToUtf8Bytes(Parsed,Options);
            if(!Payload.AsSpan().SequenceEqual(Canonical)){ Error=$"Snapshot component {Descriptor.TypeId} payload is not canonical.";return false; }
            Value=Parsed;
            return true;
        } catch(Exception Ex)when(Ex is JsonException or NotSupportedException or FormatException or OverflowException){ Error=$"Snapshot component {Descriptor.TypeId} cannot be read: {Ex.Message}";return false; }
    }
    private static JsonSerializerOptions CreateOptions(){
        var Resolver=new DefaultJsonTypeInfoResolver();
        Resolver.Modifiers.Add(static TypeInfo=>{
            int Order=0;
            foreach(JsonPropertyInfo Property in TypeInfo.Properties.OrderBy(static Property=>Property.Name,StringComparer.Ordinal)){ Property.Order=Order++; }
        });
        var Result=new JsonSerializerOptions { IncludeFields=true,PropertyNamingPolicy=null,WriteIndented=false,TypeInfoResolver=Resolver };
        Result.Converters.Add(new LogicalScalarJsonConverter());
        Result.Converters.Add(new StableIdJsonConverter());
        Result.Converters.Add(new ReferenceFrameIdJsonConverter());
        Result.Converters.Add(new WorldCellIdJsonConverter());
        Result.Converters.Add(new PersistentEntityIdJsonConverter());
        Result.Converters.Add(new Hash256JsonConverter());
        Result.Converters.Add(new AssetIdJsonConverter());
        Result.Converters.Add(new SubAssetIdJsonConverter());
        Result.Converters.Add(new ConstructedValueJsonConverterFactory());
        return Result;
    }
    private static string Describe(Type Type,HashSet<Type>Stack){
        if(IsTerminal(Type)){ return CanonicalTypeId(Type); }
        Type?Nullable=System.Nullable.GetUnderlyingType(Type);
        if(Nullable is not null){ return "nullable("+Describe(Nullable,Stack)+")"; }
        if(Type.IsEnum){ return "enum("+CanonicalTypeId(Type)+":"+CanonicalTypeId(Enum.GetUnderlyingType(Type))+")"; }
        if(!Type.IsValueType){ throw new NotSupportedException($"Snapshot component field type {CanonicalTypeId(Type)} must be a string or value type."); }
        if(!Stack.Add(Type)){ throw new NotSupportedException($"Snapshot component field type {CanonicalTypeId(Type)} is recursive."); }
        var Members=new List<(string Name,Type Type)>();
        if(ConstructedValueShape.TryGet(Type,out var Constructed)){
            // 検証済み値型の状態はコンストラクターのパラメーターで決まり、IsValidなどの計算プロパティは含まない。
            foreach(PropertyInfo Property in Constructed.Properties){ Members.Add((Property.Name,Property.PropertyType)); }
        } else {
            foreach(FieldInfo Field in Type.GetFields(BindingFlags.Instance|BindingFlags.Public)){ Members.Add((Field.Name,Field.FieldType)); }
            foreach(PropertyInfo Property in Type.GetProperties(BindingFlags.Instance|BindingFlags.Public)){
                if(Property.GetMethod is not null&&Property.GetIndexParameters().Length==0){ Members.Add((Property.Name,Property.PropertyType)); }
            }
        }
        string Result=CanonicalTypeId(Type)+"{"+string.Join(";",Members.OrderBy(static Member=>Member.Name,StringComparer.Ordinal).Select(Member=>Member.Name+":"+Describe(Member.Type,Stack)))+"}";
        Stack.Remove(Type);
        return Result;
    }
    private static bool IsTerminal(Type Type)=>Type.IsPrimitive||Type==typeof(string)||Type==typeof(Int128)||Type==typeof(UInt128)
        ||Type==typeof(LogicalScalar)||Type==typeof(StableId)||Type==typeof(ReferenceFrameId)||Type==typeof(WorldCellId)||Type==typeof(PersistentEntityId)
        ||Type==typeof(Hash256)||Type==typeof(AssetId)||Type==typeof(SubAssetId);
    private static string CanonicalTypeId(Type Type)=>Type.FullName??Type.Name;
}

public static class EcsSnapshotArtifactCodec {
    private static readonly byte[]Magic=Encoding.ASCII.GetBytes("CGECSNP1");
    private const uint Version=1;
    private const int HeaderSize=80;
    private const int MaximumArtifactBytes=512*1024*1024;
    public static byte[]Write(string SchemaFingerprint,ulong Tick,string EventHash,IEnumerable<WorldCellId>CapturedCells,IEnumerable<EcsSnapshotEntityData>Entities){
        if(!IsHash(SchemaFingerprint)){ throw new ArgumentException("Snapshot schema fingerprint must be canonical SHA-256.",nameof(SchemaFingerprint)); }
        if(!IsHash(EventHash)){ throw new ArgumentException("Snapshot event hash must be canonical SHA-256.",nameof(EventHash)); }
        var Cells=CapturedCells.Distinct().Order().ToArray();
        if(Cells.Any(static Cell=>!Cell.IsValid)){ throw new ArgumentException("Snapshot captured cells must be valid.",nameof(CapturedCells)); }
        var CellSet=Cells.ToHashSet();
        EcsSnapshotEntityData[]OrderedEntities=Entities.Select(Canonicalize).OrderBy(static Entity=>Entity.Cell).ThenBy(static Entity=>Entity.Persistent.ToString(),StringComparer.Ordinal).ToArray();
        ValidateCanonical(Cells,OrderedEntities,CellSet);
        byte[]StateSection=WriteStateSection(Cells,OrderedEntities);
        string StateHash=Hash(StateSection);
        byte[]Payload;
        using(var Stream=new MemoryStream()){
            using var Writer=new BinaryWriter(Stream,new UTF8Encoding(false),true);
            Writer.Write(Tick);
            Writer.Write(Convert.FromHexString(StateHash));
            Writer.Write(Convert.FromHexString(EventHash));
            Writer.Write(StateSection);
            Writer.Flush();
            Payload=Stream.ToArray();
        }
        if(Payload.Length>MaximumArtifactBytes){ throw new InvalidOperationException("Snapshot artifact exceeds the maximum size."); }
        byte[]Result=new byte[checked(HeaderSize+Payload.Length)];
        Magic.CopyTo(Result,0);
        BinaryPrimitives.WriteUInt32LittleEndian(Result.AsSpan(8),Version);
        BinaryPrimitives.WriteInt32LittleEndian(Result.AsSpan(12),Payload.Length);
        Convert.FromHexString(SchemaFingerprint).CopyTo(Result,16);
        SHA256.HashData(Payload).CopyTo(Result,48);
        Payload.CopyTo(Result,HeaderSize);
        return Result;
    }
    public static bool TryRead(ReadOnlySpan<byte>Artifact,string ExpectedSchema,out EcsSnapshotDocument?Document,out EcsSnapshotError Error){
        Document=null;
        if(Artifact.Length<HeaderSize||!Artifact[..Magic.Length].SequenceEqual(Magic)){ Error=Create(EcsSnapshotErrorCode.InvalidHeader,"Snapshot magic/header is invalid.");return false; }
        if(BinaryPrimitives.ReadUInt32LittleEndian(Artifact[8..])!=Version){ Error=Create(EcsSnapshotErrorCode.UnsupportedVersion,"Snapshot version is unsupported; no legacy reader is retained.");return false; }
        int PayloadLength=BinaryPrimitives.ReadInt32LittleEndian(Artifact[12..]);
        if(PayloadLength<72||PayloadLength>MaximumArtifactBytes||Artifact.Length!=HeaderSize+PayloadLength){ Error=Create(EcsSnapshotErrorCode.InvalidLength,"Snapshot payload length is invalid.");return false; }
        string Schema=Convert.ToHexString(Artifact.Slice(16,32)).ToLowerInvariant();
        if(!string.Equals(Schema,ExpectedSchema,StringComparison.Ordinal)){ Error=Create(EcsSnapshotErrorCode.SchemaMismatch,$"Snapshot schema {Schema} does not match {ExpectedSchema}.");return false; }
        ReadOnlySpan<byte>Payload=Artifact[HeaderSize..];
        Span<byte>Checksum=stackalloc byte[32];
        SHA256.HashData(Payload,Checksum);
        if(!Checksum.SequenceEqual(Artifact.Slice(48,32))){ Error=Create(EcsSnapshotErrorCode.ChecksumMismatch,"Snapshot payload checksum is invalid.");return false; }
        try {
            using var Stream=new MemoryStream(Payload.ToArray(),false);
            using var Reader=new BinaryReader(Stream,Encoding.UTF8,true);
            ulong Tick=Reader.ReadUInt64();
            string StateHash=Convert.ToHexString(ReadExact(Reader,32)).ToLowerInvariant();
            string EventHash=Convert.ToHexString(ReadExact(Reader,32)).ToLowerInvariant();
            long StateOffset=Stream.Position;
            WorldCellId[]Cells=ReadCells(Reader);
            EcsSnapshotEntityData[]Entities=ReadEntities(Reader,Cells.ToHashSet());
            if(Stream.Position!=Stream.Length){ throw new InvalidDataException("Snapshot payload has trailing bytes."); }
            string ActualStateHash=Hash(Payload[(int)StateOffset..].ToArray());
            if(!string.Equals(StateHash,ActualStateHash,StringComparison.Ordinal)){ Error=Create(EcsSnapshotErrorCode.ChecksumMismatch,"Snapshot deterministic state hash is invalid.");return false; }
            if(!IsHash(EventHash)){ throw new InvalidDataException("Snapshot event hash is invalid."); }
            Document=new EcsSnapshotDocument(Schema,Tick,StateHash,EventHash,Cells,Entities);
            Error=EcsSnapshotError.None;
            return true;
        } catch(Exception Ex)when(Ex is EndOfStreamException or InvalidDataException or IOException or ArgumentException or OverflowException){ Error=Create(EcsSnapshotErrorCode.InvalidPayload,$"Snapshot payload is invalid: {Ex.Message}");return false; }
    }
    public static byte[]Merge(EcsSnapshotDocument Previous,string SchemaFingerprint,ulong Tick,string EventHash,IEnumerable<WorldCellId>CapturedCells,IEnumerable<EcsSnapshotEntityData>Entities){
        ArgumentNullException.ThrowIfNull(Previous);
        if(!string.Equals(Previous.SchemaFingerprint,SchemaFingerprint,StringComparison.Ordinal)){ throw new InvalidOperationException("A snapshot can only merge with the current schema."); }
        WorldCellId[]ReplacementCells=CapturedCells.Distinct().ToArray();
        var Replacement=ReplacementCells.ToHashSet();
        var MergedCells=Previous.CapturedCells.Where(Cell=>!Replacement.Contains(Cell)).Concat(ReplacementCells);
        var MergedEntities=Previous.Entities.Where(Entity=>!Replacement.Contains(Entity.Cell)).Concat(Entities);
        return Write(SchemaFingerprint,Tick,EventHash,MergedCells,MergedEntities);
    }
    internal static EcsSnapshotDocument Filter(EcsSnapshotDocument Source,IReadOnlySet<WorldCellId>Cells){
        WorldCellId[]FilteredCells=Source.CapturedCells.Where(Cells.Contains).ToArray();
        EcsSnapshotEntityData[]FilteredEntities=Source.Entities.Where(Entity=>Cells.Contains(Entity.Cell)).ToArray();
        return new EcsSnapshotDocument(Source.SchemaFingerprint,Source.Tick,Source.StateHash,Source.EventHash,FilteredCells,FilteredEntities);
    }
    private static EcsSnapshotEntityData Canonicalize(EcsSnapshotEntityData Entity)=>Entity with{Components=Entity.Components.OrderBy(static Component=>Component.TypeId,StringComparer.Ordinal).Select(static Component=>Component with{Payload=(byte[])Component.Payload.Clone()}).ToArray()};
    private static void ValidateCanonical(WorldCellId[]Cells,EcsSnapshotEntityData[]Entities,HashSet<WorldCellId>CellSet){
        var Persistent=new HashSet<PersistentEntityId>();
        foreach(var Entity in Entities){
            if(!Entity.Cell.IsValid||!Entity.Persistent.IsValid||!CellSet.Contains(Entity.Cell)){ throw new ArgumentException("Snapshot entity identity or captured cell is invalid."); }
            if(!IsHash(Entity.ArchetypeKey)){ throw new ArgumentException("Snapshot archetype key must be canonical SHA-256."); }
            if(!Persistent.Add(Entity.Persistent)){ throw new ArgumentException("Snapshot persistent entity IDs must be globally unique."); }
            string?Previous=null;
            foreach(var Component in Entity.Components){
                if(string.IsNullOrWhiteSpace(Component.TypeId)||Component.Payload is null||Component.Payload.Length==0){ throw new ArgumentException("Snapshot component metadata/payload is invalid."); }
                if(Previous is not null&&string.CompareOrdinal(Previous,Component.TypeId)>=0){ throw new ArgumentException("Snapshot components must have unique canonical order."); }
                Previous=Component.TypeId;
            }
        }
    }
    private static byte[]WriteStateSection(IReadOnlyList<WorldCellId>Cells,IReadOnlyList<EcsSnapshotEntityData>Entities){
        using var Stream=new MemoryStream();
        using var Writer=new BinaryWriter(Stream,new UTF8Encoding(false),true);
        Writer.Write(Cells.Count);
        foreach(WorldCellId Cell in Cells){ WriteString(Writer,Cell.ToString()); }
        Writer.Write(Entities.Count);
        foreach(var Entity in Entities){
            WriteString(Writer,Entity.Cell.ToString());
            WriteString(Writer,Entity.Persistent.ToString());
            Writer.Write(Convert.FromHexString(Entity.ArchetypeKey));
            Writer.Write(Entity.Enabled);
            Writer.Write(Entity.Components.Count);
            foreach(var Component in Entity.Components){ WriteString(Writer,Component.TypeId);Writer.Write(Component.Enabled);Writer.Write(Component.Payload.Length);Writer.Write(Component.Payload); }
        }
        Writer.Flush();
        return Stream.ToArray();
    }
    private static WorldCellId[]ReadCells(BinaryReader Reader){
        int Count=ReadCount(Reader,"captured cell",10_000_000);
        var Cells=new WorldCellId[Count];
        WorldCellId Previous=default;
        for(int Index=0;Index<Count;Index++){
            if(!WorldCellId.TryParse(ReadString(Reader),out var Cell)||!Cell.IsValid){ throw new InvalidDataException("Snapshot captured cell ID is invalid."); }
            if(Index>0&&Previous.CompareTo(Cell)>=0){ throw new InvalidDataException("Snapshot captured cells are not canonically ordered and unique."); }
            Cells[Index]=Cell;Previous=Cell;
        }
        return Cells;
    }
    private static EcsSnapshotEntityData[]ReadEntities(BinaryReader Reader,HashSet<WorldCellId>Cells){
        int Count=ReadCount(Reader,"entity",10_000_000);
        var Entities=new EcsSnapshotEntityData[Count];
        var PersistentIds=new HashSet<PersistentEntityId>();
        WorldCellId PreviousCell=default;
        string?PreviousPersistent=null;
        for(int Index=0;Index<Count;Index++){
            if(!WorldCellId.TryParse(ReadString(Reader),out var Cell)||!Cells.Contains(Cell)){ throw new InvalidDataException("Snapshot entity cell is not captured."); }
            PersistentEntityId Persistent;
            try { Persistent=PersistentEntityId.Parse(ReadString(Reader)); }catch(FormatException){ throw new InvalidDataException("Snapshot persistent entity ID is invalid."); }
            if(!Persistent.IsValid||!PersistentIds.Add(Persistent)){ throw new InvalidDataException("Snapshot persistent entity IDs are invalid or duplicated."); }
            if(Index>0&&(PreviousCell.CompareTo(Cell)>0||(PreviousCell==Cell&&string.CompareOrdinal(PreviousPersistent,Persistent.ToString())>=0))){ throw new InvalidDataException("Snapshot entities are not canonically ordered."); }
            string ArchetypeKey=Convert.ToHexString(ReadExact(Reader,32)).ToLowerInvariant();
            bool Enabled=Reader.ReadBoolean();
            int ComponentCount=ReadCount(Reader,"component",128);
            var Components=new EcsSnapshotComponentData[ComponentCount];
            string?PreviousType=null;
            for(int ComponentIndex=0;ComponentIndex<ComponentCount;ComponentIndex++){
                string TypeId=ReadString(Reader);
                if(string.IsNullOrWhiteSpace(TypeId)||(PreviousType is not null&&string.CompareOrdinal(PreviousType,TypeId)>=0)){ throw new InvalidDataException("Snapshot components are not canonically ordered and unique."); }
                bool ComponentEnabled=Reader.ReadBoolean();
                int Length=ReadCount(Reader,"component payload byte",64*1024*1024);
                if(Length==0){ throw new InvalidDataException("Snapshot component payload is empty."); }
                Components[ComponentIndex]=new EcsSnapshotComponentData(TypeId,ComponentEnabled,ReadExact(Reader,Length));
                PreviousType=TypeId;
            }
            Entities[Index]=new EcsSnapshotEntityData(Cell,Persistent,ArchetypeKey,Enabled,Components);
            PreviousCell=Cell;PreviousPersistent=Persistent.ToString();
        }
        return Entities;
    }
    private static int ReadCount(BinaryReader Reader,string Name,int Maximum){ int Value=Reader.ReadInt32();if(Value<0||Value>Maximum){ throw new InvalidDataException($"Snapshot {Name} count is invalid."); }return Value; }
    private static byte[]ReadExact(BinaryReader Reader,int Length){ byte[]Bytes=Reader.ReadBytes(Length);if(Bytes.Length!=Length){ throw new EndOfStreamException(); }return Bytes; }
    private static void WriteString(BinaryWriter Writer,string Value){ byte[]Bytes=Encoding.UTF8.GetBytes(Value);if(Bytes.Length>ushort.MaxValue){ throw new InvalidOperationException("Snapshot string exceeds the canonical limit."); }Writer.Write((ushort)Bytes.Length);Writer.Write(Bytes); }
    private static string ReadString(BinaryReader Reader){ int Length=Reader.ReadUInt16();byte[]Bytes=ReadExact(Reader,Length);string Value=new UTF8Encoding(false,true).GetString(Bytes);if(!Encoding.UTF8.GetBytes(Value).AsSpan().SequenceEqual(Bytes)){ throw new InvalidDataException("Snapshot string is not canonical UTF-8."); }return Value; }
    private static string Hash(byte[]Bytes)=>Convert.ToHexString(SHA256.HashData(Bytes)).ToLowerInvariant();
    private static bool IsHash(string?Value)=>Value is{Length:64}&&Value.All(static Character=>(Character>='0'&&Character<='9')||(Character>='a'&&Character<='f'));
    private static EcsSnapshotError Create(EcsSnapshotErrorCode Code,string Message)=>new(Code,Message);
}

internal abstract class StringValueJsonConverter<T>:JsonConverter<T> {
    public override T Read(ref Utf8JsonReader Reader,Type TypeToConvert,JsonSerializerOptions Options){ if(Reader.TokenType!=JsonTokenType.String){ throw new JsonException(); }return Parse(Reader.GetString()!); }
    public override void Write(Utf8JsonWriter Writer,T Value,JsonSerializerOptions Options)=>Writer.WriteStringValue(Format(Value));
    protected abstract T Parse(string Value);
    protected abstract string Format(T Value);
}
internal sealed class LogicalScalarJsonConverter:StringValueJsonConverter<LogicalScalar>{protected override LogicalScalar Parse(string Value)=>LogicalScalar.ParseInvariant(Value);protected override string Format(LogicalScalar Value)=>Value.ToStringInvariant();}
internal sealed class StableIdJsonConverter:StringValueJsonConverter<StableId>{protected override StableId Parse(string Value)=>StableId.Parse(Value);protected override string Format(StableId Value)=>Value.ToString();}
internal sealed class ReferenceFrameIdJsonConverter:StringValueJsonConverter<ReferenceFrameId>{protected override ReferenceFrameId Parse(string Value)=>ReferenceFrameId.Parse(Value);protected override string Format(ReferenceFrameId Value)=>Value.ToString();}
internal sealed class WorldCellIdJsonConverter:StringValueJsonConverter<WorldCellId>{protected override WorldCellId Parse(string Value)=>WorldCellId.Parse(Value);protected override string Format(WorldCellId Value)=>Value.ToString();}
internal sealed class PersistentEntityIdJsonConverter:StringValueJsonConverter<PersistentEntityId>{protected override PersistentEntityId Parse(string Value)=>PersistentEntityId.Parse(Value);protected override string Format(PersistentEntityId Value)=>Value.ToString();}
internal sealed class Hash256JsonConverter:StringValueJsonConverter<Hash256>{protected override Hash256 Parse(string Value)=>Hash256.Parse(Value);protected override string Format(Hash256 Value)=>Value.ToString();}
internal sealed class AssetIdJsonConverter:StringValueJsonConverter<AssetId>{protected override AssetId Parse(string Value)=>AssetId.Parse(Value);protected override string Format(AssetId Value)=>Value.ToString();}
internal sealed class SubAssetIdJsonConverter:StringValueJsonConverter<SubAssetId>{protected override SubAssetId Parse(string Value)=>SubAssetId.Parse(Value);protected override string Format(SubAssetId Value)=>Value.ToString();}

/// <summary>
/// 検証済み値型（監査AUD-001）を表す。公開フィールドを持たず、setterのないプロパティだけを持つ構造体であり、
/// その状態は、型のプロパティをそれぞれ1つずつ指定する公開コンストラクターのパラメーターで決まる。
/// 物理ライト量の型はこの条件を満たす。System.Text.Jsonだけではプロパティを設定できないため、スナップショットコーデックは
/// コンストラクターを通じて再構築する。コンストラクターの範囲検証は復元時にも行われる。IsValidなど、対応する引数名のないプロパティは状態に含まない。
/// </summary>
internal sealed class ConstructedValueShape {
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type,ConstructedValueShape?>Shapes=new();
    private ConstructedValueShape(ConstructorInfo Constructor,PropertyInfo[]Properties){
        this.Constructor=Constructor;
        this.Properties=Properties;
    }
    internal ConstructorInfo Constructor{get;}
    /// <summary>状態を保持するプロパティ。コンストラクターのパラメーター順に並ぶ。</summary>
    internal PropertyInfo[]Properties{get;}
    internal static bool TryGet(Type Type,out ConstructedValueShape Shape){
        Shape=Shapes.GetOrAdd(Type,Find)!;
        return Shape is not null;
    }
    private static ConstructedValueShape?Find(Type Type){
        if(!Type.IsValueType||Type.IsPrimitive||Type.IsEnum||Nullable.GetUnderlyingType(Type) is not null){ return null; }
        if(Type.GetFields(BindingFlags.Instance|BindingFlags.Public).Length!=0){ return null; }
        PropertyInfo[]Properties=[.. Type.GetProperties(BindingFlags.Instance|BindingFlags.Public).Where(static Property=>Property.GetIndexParameters().Length==0)];
        if(Properties.Any(static Property=>Property.SetMethod is not null)){ return null; }
        ConstructorInfo[]Constructors=Type.GetConstructors(BindingFlags.Instance|BindingFlags.Public);
        if(Constructors.Length!=1){ return null; }
        ParameterInfo[]Parameters=Constructors[0].GetParameters();
        if(Parameters.Length==0){ return null; }
        var State=new PropertyInfo[Parameters.Length];
        for(var Index=0;Index<Parameters.Length;Index++){
            ParameterInfo Parameter=Parameters[Index];
            PropertyInfo?Property=Properties.FirstOrDefault(Candidate=>string.Equals(Candidate.Name,Parameter.Name,StringComparison.OrdinalIgnoreCase)
                &&Candidate.PropertyType==Parameter.ParameterType);
            if(Property is null){ return null; }
            State[Index]=Property;
        }
        return new ConstructedValueShape(Constructors[0],State);
    }
}
internal sealed class ConstructedValueJsonConverterFactory:JsonConverterFactory {
    public override bool CanConvert(Type TypeToConvert)=>ConstructedValueShape.TryGet(TypeToConvert,out _);
    public override JsonConverter CreateConverter(Type TypeToConvert,JsonSerializerOptions Options){
        ConstructedValueShape.TryGet(TypeToConvert,out var Shape);
        return (JsonConverter)Activator.CreateInstance(typeof(ConstructedValueJsonConverter<>).MakeGenericType(TypeToConvert),Shape)!;
    }
}
internal sealed class ConstructedValueJsonConverter<T>(ConstructedValueShape Shape):JsonConverter<T> {
    /// <summary>ほかの各メンバーと同様、コーデックが序数名順で書き込む。</summary>
    private readonly PropertyInfo[]Ordered=[.. Shape.Properties.OrderBy(static Property=>Property.Name,StringComparer.Ordinal)];
    public override T Read(ref Utf8JsonReader Reader,Type TypeToConvert,JsonSerializerOptions Options){
        if(Reader.TokenType!=JsonTokenType.StartObject){ throw new JsonException($"{typeof(T).FullName} must be an object."); }
        var Values=new object?[Shape.Properties.Length];
        var Seen=new bool[Values.Length];
        while(Reader.Read()&&Reader.TokenType!=JsonTokenType.EndObject){
            string?Name=Reader.TokenType==JsonTokenType.PropertyName?Reader.GetString():null;
            int Index=Array.FindIndex(Shape.Properties,Property=>Property.Name==Name);
            if(Index<0||Seen[Index]){ throw new JsonException($"{typeof(T).FullName} has no member {Name} or repeats it."); }
            Reader.Read();
            Values[Index]=JsonSerializer.Deserialize(ref Reader,Shape.Properties[Index].PropertyType,Options);
            Seen[Index]=true;
        }
        if(Reader.TokenType!=JsonTokenType.EndObject||Seen.Contains(false)){ throw new JsonException($"{typeof(T).FullName} is missing a member."); }
        try {
            return (T)Shape.Constructor.Invoke(Values);
        } catch(TargetInvocationException Ex)when(Ex.InnerException is ArgumentException Invalid){
            throw new JsonException($"{typeof(T).FullName}: {Invalid.Message}",Invalid);
        }
    }
    public override void Write(Utf8JsonWriter Writer,T Value,JsonSerializerOptions Options){
        Writer.WriteStartObject();
        foreach(PropertyInfo Property in this.Ordered){
            Writer.WritePropertyName(Property.Name);
            JsonSerializer.Serialize(Writer,Property.GetValue(Value),Property.PropertyType,Options);
        }
        Writer.WriteEndObject();
    }
}
