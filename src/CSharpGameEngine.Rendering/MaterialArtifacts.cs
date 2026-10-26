using System.Buffers.Binary;
using System.Numerics;
using CSharpGameEngine.Content;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.Rendering;

public readonly record struct RenderMaterialArtifactDefinition(
    Vector4 BaseColorFactor,float RoughnessFactor,float MetallicFactor,
    Vector3 EmissiveNits,float EmissiveTextureLuminanceScale,
    AssetArtifactReference BaseColorTexture,
    AssetArtifactReference NormalTexture,
    AssetArtifactReference MetallicRoughnessTexture,
    AssetArtifactReference EmissiveTexture);

public sealed class RenderTextureResource {
    private readonly byte[]PixelsStorage;

    internal RenderTextureResource(Hash256 ContentHash,int Width,int Height,byte[]Pixels,TextureOutputData?Prepared=null){
        this.PreparedMipChain=Prepared is null?null:RenderTextureMipChain.FromImported(Prepared);
        this.PreparedColorSpace=Prepared is null?null:Prepared.Srgb?RenderTextureColorSpace.Srgb:RenderTextureColorSpace.Linear;
        this.ContentHash=ContentHash;
        this.Width=Width;
        this.Height=Height;
        this.PixelsStorage=Pixels;
    }

    public RenderTextureMipChain?PreparedMipChain { get; }
    public RenderTextureColorSpace?PreparedColorSpace { get; }
    public Hash256 ContentHash { get; }
    public int Width { get; }
    public int Height { get; }
    public ReadOnlySpan<byte>Pixels=>this.PixelsStorage;

    internal Vector4 Sample(float U,float V){
        float ClampedU=Math.Clamp(U,0f,1f);
        float ClampedV=Math.Clamp(V,0f,1f);
        int X=Math.Min(this.Width-1,(int)(ClampedU*this.Width));
        int Y=Math.Min(this.Height-1,(int)(ClampedV*this.Height));
        int Offset=(Y*this.Width+X)*4;
        const float Scale=1f/255f;
        return new Vector4(
            this.PixelsStorage[Offset]*Scale,
            this.PixelsStorage[Offset+1]*Scale,
            this.PixelsStorage[Offset+2]*Scale,
            this.PixelsStorage[Offset+3]*Scale);
    }
}

public sealed class RenderMaterialResource {
    internal RenderMaterialResource(
        Hash256 ArtifactAddress,in RenderMaterialArtifactDefinition Definition,
        RenderTextureResource BaseColorTexture,RenderTextureResource NormalTexture,
        RenderTextureResource MetallicRoughnessTexture,RenderTextureResource EmissiveTexture){
        this.ArtifactAddress=ArtifactAddress;
        this.BaseColorFactor=Definition.BaseColorFactor;
        this.RoughnessFactor=Definition.RoughnessFactor;
        this.MetallicFactor=Definition.MetallicFactor;
        this.EmissiveNits=Definition.EmissiveNits;
        this.EmissiveTextureLuminanceScale=Definition.EmissiveTextureLuminanceScale;
        this.BaseColorTexture=BaseColorTexture;
        this.NormalTexture=NormalTexture;
        this.MetallicRoughnessTexture=MetallicRoughnessTexture;
        this.EmissiveTexture=EmissiveTexture;
    }

    public Hash256 ArtifactAddress { get; }
    public Vector4 BaseColorFactor { get; }
    public float RoughnessFactor { get; }
    public float MetallicFactor { get; }
    public Vector3 EmissiveNits { get; }
    public float EmissiveTextureLuminanceScale { get; }
    public RenderTextureResource BaseColorTexture { get; }
    public RenderTextureResource NormalTexture { get; }
    public RenderTextureResource MetallicRoughnessTexture { get; }
    public RenderTextureResource EmissiveTexture { get; }

    public Vector4 ShadeBaseColor(float U,float V)=>
        this.BaseColorTexture.Sample(U,V)*this.BaseColorFactor;
}

public static class RenderArtifactCodec {
    private const uint TextureMagic=0x31584554;
    private const uint MaterialMagic=0x3154414D;
    private const ushort SchemaVersion=1;
    private const ushort Rgba8UnormFormat=1;
    private const int TextureHeaderSize=16;
    private const int MaterialHeaderSize=48;
    private const int TextureSlotCount=4;
    private const int MaterialPayloadSize=MaterialHeaderSize+
        TextureSlotCount*CanonicalAssetKeyCodec.EncodedLength;
    private const int MaximumTextureDimension=16384;

    public static byte[]CreateTextureEnvelope(
        ArtifactProvenance Provenance,int Width,int Height,ReadOnlySpan<byte>Rgba8Pixels){
        byte[]Payload=CreateTexturePayload(Width,Height,Rgba8Pixels);
        return ContentArtifactCodec.Write(new ContentArtifact(
            ContentArtifactKind.Texture,Provenance,[],Payload));
    }

    public static RenderTextureResource CreateProceduralTexture(
        int Width,int Height,ReadOnlySpan<byte>Rgba8Pixels){
        byte[]Payload=CreateTexturePayload(Width,Height,Rgba8Pixels);
        return new RenderTextureResource(
            Hash256.Compute(Payload),Width,Height,Rgba8Pixels.ToArray());
    }

    private static byte[]CreateTexturePayload(
        int Width,int Height,ReadOnlySpan<byte>Rgba8Pixels){
        ValidateTexture(Width,Height,Rgba8Pixels);
        byte[]Payload=new byte[checked(TextureHeaderSize+Rgba8Pixels.Length)];
        BinaryPrimitives.WriteUInt32LittleEndian(Payload.AsSpan(0,4),TextureMagic);
        BinaryPrimitives.WriteUInt16LittleEndian(Payload.AsSpan(4,2),SchemaVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(Payload.AsSpan(6,2),Rgba8UnormFormat);
        BinaryPrimitives.WriteUInt32LittleEndian(Payload.AsSpan(8,4),(uint)Width);
        BinaryPrimitives.WriteUInt32LittleEndian(Payload.AsSpan(12,4),(uint)Height);
        Rgba8Pixels.CopyTo(Payload.AsSpan(TextureHeaderSize));
        return Payload;
    }

    public static byte[]CreateMaterialEnvelope(
        ArtifactProvenance Provenance,in RenderMaterialArtifactDefinition Definition){
        ValidateMaterial(Definition);
        AssetArtifactReference[]Slots=GetSlots(Definition);
        Dictionary<AssetKey,AssetArtifactReference>Dependencies=[];
        foreach(AssetArtifactReference Slot in Slots){
            if(Dependencies.TryGetValue(Slot.Key,out AssetArtifactReference Existing)
                &&Existing.ContentHash!=Slot.ContentHash){
                throw new ArgumentException(
                    $"Texture dependency '{Slot.Key}' has conflicting content hashes.",
                    nameof(Definition));
            }
            Dependencies[Slot.Key]=Slot;
        }
        byte[]Payload=new byte[MaterialPayloadSize];
        BinaryPrimitives.WriteUInt32LittleEndian(Payload.AsSpan(0,4),MaterialMagic);
        BinaryPrimitives.WriteUInt16LittleEndian(Payload.AsSpan(4,2),SchemaVersion);
        WriteVector4(Payload.AsSpan(8,16),Definition.BaseColorFactor);
        WriteSingle(Payload.AsSpan(24,4),Definition.RoughnessFactor);
        WriteSingle(Payload.AsSpan(28,4),Definition.MetallicFactor);
        WriteVector3(Payload.AsSpan(32,12),Definition.EmissiveNits);
        WriteSingle(Payload.AsSpan(44,4),Definition.EmissiveTextureLuminanceScale);
        for(int Index=0;Index<Slots.Length;Index++){
            CanonicalAssetKeyCodec.Write(
                Payload.AsSpan(
                    MaterialHeaderSize+Index*CanonicalAssetKeyCodec.EncodedLength,
                    CanonicalAssetKeyCodec.EncodedLength),
                Slots[Index].Key);
        }
        return ContentArtifactCodec.Write(new ContentArtifact(
            ContentArtifactKind.Material,Provenance,Dependencies.Values,Payload));
    }

    internal static bool TryReadTexture(
        ContentArtifact Artifact,out int Width,out int Height,out byte[]Pixels,out string Error){
        Width=0;
        Height=0;
        Pixels=[];
        Error=string.Empty;
        if(Artifact.Kind!=ContentArtifactKind.Texture){
            Error="Artifact kind is not Texture.";
            return false;
        }
        ReadOnlySpan<byte>Payload=Artifact.Payload.AsSpan();
        if(Payload.Length>=4&&BinaryPrimitives.ReadUInt32LittleEndian(Payload)==0x31544743){
            try { var Texture=ImageArtifactCodec.ReadTexture(Payload);Width=Texture.Levels[0].Width;Height=Texture.Levels[0].Height;Pixels=Texture.Levels[0].Pixels;return true; }
            catch(Exception Failure)when(Failure is IOException or ArgumentException or OverflowException){ Error=Failure.Message;return false; }
        }
        if(Payload.Length<TextureHeaderSize
            ||BinaryPrimitives.ReadUInt32LittleEndian(Payload.Slice(0,4))!=TextureMagic
            ||BinaryPrimitives.ReadUInt16LittleEndian(Payload.Slice(4,2))!=SchemaVersion
            ||BinaryPrimitives.ReadUInt16LittleEndian(Payload.Slice(6,2))!=Rgba8UnormFormat){
            Error="Texture payload header is invalid or unsupported.";
            return false;
        }
        uint RawWidth=BinaryPrimitives.ReadUInt32LittleEndian(Payload.Slice(8,4));
        uint RawHeight=BinaryPrimitives.ReadUInt32LittleEndian(Payload.Slice(12,4));
        if(RawWidth is 0 or >MaximumTextureDimension
            ||RawHeight is 0 or >MaximumTextureDimension){
            Error="Texture dimensions are outside the supported range.";
            return false;
        }
        int ExpectedLength;
        try { ExpectedLength=checked(TextureHeaderSize+(int)RawWidth*(int)RawHeight*4); }
        catch(OverflowException){
            Error="Texture payload dimensions overflow its byte length.";
            return false;
        }
        if(Payload.Length!=ExpectedLength){
            Error="Texture payload byte length does not match its dimensions.";
            return false;
        }
        Width=(int)RawWidth;
        Height=(int)RawHeight;
        Pixels=Payload.Slice(TextureHeaderSize).ToArray();
        return true;
    }

    internal static bool TryReadMaterial(
        ContentArtifact Artifact,out RenderMaterialArtifactDefinition Definition,out string Error){
        Definition=default;
        Error=string.Empty;
        if(Artifact.Kind!=ContentArtifactKind.Material){
            Error="Artifact kind is not Material.";
            return false;
        }
        ReadOnlySpan<byte>Payload=Artifact.Payload.AsSpan();
        if(Payload.Length!=MaterialPayloadSize
            ||BinaryPrimitives.ReadUInt32LittleEndian(Payload.Slice(0,4))!=MaterialMagic
            ||BinaryPrimitives.ReadUInt16LittleEndian(Payload.Slice(4,2))!=SchemaVersion
            ||BinaryPrimitives.ReadUInt16LittleEndian(Payload.Slice(6,2))!=0){
            Error="Material payload header is invalid or unsupported.";
            return false;
        }
        AssetKey[]Keys=new AssetKey[TextureSlotCount];
        for(int Index=0;Index<Keys.Length;Index++){
            if(!CanonicalAssetKeyCodec.TryRead(
                Payload.Slice(
                    MaterialHeaderSize+Index*CanonicalAssetKeyCodec.EncodedLength,
                    CanonicalAssetKeyCodec.EncodedLength),
                out Keys[Index])){
                Error=$"Material texture slot {Index} has an invalid AssetKey.";
                return false;
            }
        }
        Dictionary<AssetKey,Hash256>Dependencies=[];
        foreach(AssetArtifactReference Dependency in Artifact.Dependencies){
            Dependencies.Add(Dependency.Key,Dependency.ContentHash);
        }
        var Slots=new AssetArtifactReference[TextureSlotCount];
        for(int Index=0;Index<Slots.Length;Index++){
            if(!Dependencies.TryGetValue(Keys[Index],out Hash256 Hash)){
                Error=$"Material texture slot {Index} is absent from envelope dependencies.";
                return false;
            }
            Slots[Index]=new AssetArtifactReference(Keys[Index],Hash);
        }
        if(Dependencies.Keys.Any(Key=>!Keys.Contains(Key))){
            Error="Material envelope contains an undeclared texture dependency.";
            return false;
        }
        Definition=new RenderMaterialArtifactDefinition(
            ReadVector4(Payload.Slice(8,16)),
            ReadSingle(Payload.Slice(24,4)),
            ReadSingle(Payload.Slice(28,4)),
            ReadVector3(Payload.Slice(32,12)),
            ReadSingle(Payload.Slice(44,4)),
            Slots[0],Slots[1],Slots[2],Slots[3]);
        try { ValidateMaterial(Definition); }
        catch(ArgumentException Exception){
            Error=Exception.Message;
            Definition=default;
            return false;
        }
        return true;
    }

    private static AssetArtifactReference[]GetSlots(
        in RenderMaterialArtifactDefinition Definition)=>
        [Definition.BaseColorTexture,Definition.NormalTexture,
            Definition.MetallicRoughnessTexture,Definition.EmissiveTexture];

    private static void ValidateTexture(int Width,int Height,ReadOnlySpan<byte>Pixels){
        if(Width is <1 or >MaximumTextureDimension){ throw new ArgumentOutOfRangeException(nameof(Width)); }
        if(Height is <1 or >MaximumTextureDimension){ throw new ArgumentOutOfRangeException(nameof(Height)); }
        int Expected=checked(Width*Height*4);
        if(Pixels.Length!=Expected){
            throw new ArgumentException("RGBA8 byte length does not match texture dimensions.",nameof(Pixels));
        }
    }

    private static void ValidateMaterial(in RenderMaterialArtifactDefinition Definition){
        ValidateFinite(Definition.BaseColorFactor,nameof(Definition.BaseColorFactor));
        if(Definition.BaseColorFactor.X is <0f or >1f
            ||Definition.BaseColorFactor.Y is <0f or >1f
            ||Definition.BaseColorFactor.Z is <0f or >1f
            ||Definition.BaseColorFactor.W is <0f or >1f){
            throw new ArgumentOutOfRangeException(nameof(Definition),"Base color factor must be within zero and one.");
        }
        if(!float.IsFinite(Definition.RoughnessFactor)
            ||Definition.RoughnessFactor is <0f or >1f){
            throw new ArgumentOutOfRangeException(nameof(Definition),"Roughness must be within zero and one.");
        }
        if(!float.IsFinite(Definition.MetallicFactor)
            ||Definition.MetallicFactor is <0f or >1f){
            throw new ArgumentOutOfRangeException(nameof(Definition),"Metallic must be within zero and one.");
        }
        ValidateFinite(Definition.EmissiveNits,nameof(Definition.EmissiveNits));
        if(Definition.EmissiveNits.X<0f||Definition.EmissiveNits.Y<0f
            ||Definition.EmissiveNits.Z<0f){
            throw new ArgumentOutOfRangeException(nameof(Definition),"Emissive nits cannot be negative.");
        }
        if(!float.IsFinite(Definition.EmissiveTextureLuminanceScale)
            ||Definition.EmissiveTextureLuminanceScale<0f){
            throw new ArgumentOutOfRangeException(
                nameof(Definition),"Emissive texture luminance scale cannot be negative.");
        }
        foreach(AssetArtifactReference Slot in GetSlots(Definition)){
            if(!Slot.IsValid){
                throw new ArgumentException("Every material texture slot requires a valid artifact reference.",nameof(Definition));
            }
        }
    }

    private static void ValidateFinite(Vector4 Value,string Name){
        if(!float.IsFinite(Value.X)||!float.IsFinite(Value.Y)
            ||!float.IsFinite(Value.Z)||!float.IsFinite(Value.W)){
            throw new ArgumentException($"{Name} must be finite.",Name);
        }
    }

    private static void ValidateFinite(Vector3 Value,string Name){
        if(!float.IsFinite(Value.X)||!float.IsFinite(Value.Y)||!float.IsFinite(Value.Z)){
            throw new ArgumentException($"{Name} must be finite.",Name);
        }
    }

    private static void WriteSingle(Span<byte>Bytes,float Value){
        if(Value==0f){ Value=0f; }
        BinaryPrimitives.WriteInt32LittleEndian(Bytes,BitConverter.SingleToInt32Bits(Value));
    }
    private static float ReadSingle(ReadOnlySpan<byte>Bytes)=>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(Bytes));
    private static void WriteVector3(Span<byte>Bytes,Vector3 Value){
        WriteSingle(Bytes.Slice(0,4),Value.X);
        WriteSingle(Bytes.Slice(4,4),Value.Y);
        WriteSingle(Bytes.Slice(8,4),Value.Z);
    }
    private static Vector3 ReadVector3(ReadOnlySpan<byte>Bytes)=>new(
        ReadSingle(Bytes.Slice(0,4)),
        ReadSingle(Bytes.Slice(4,4)),
        ReadSingle(Bytes.Slice(8,4)));
    private static void WriteVector4(Span<byte>Bytes,Vector4 Value){
        WriteVector3(Bytes.Slice(0,12),new Vector3(Value.X,Value.Y,Value.Z));
        WriteSingle(Bytes.Slice(12,4),Value.W);
    }
    private static Vector4 ReadVector4(ReadOnlySpan<byte>Bytes){
        Vector3 Xyz=ReadVector3(Bytes.Slice(0,12));
        return new Vector4(Xyz,ReadSingle(Bytes.Slice(12,4)));
    }
}

public static class RenderMaterialDefaults {
    public static RenderTextureResource White { get; }=
        RenderArtifactCodec.CreateProceduralTexture(1,1,[255,255,255,255]);
    public static RenderTextureResource FlatNormal { get; }=
        RenderArtifactCodec.CreateProceduralTexture(1,1,[128,128,255,255]);
}

public sealed class RenderMaterialCatalog {
    private readonly Dictionary<AssetKey,TextureRegistration>Textures=[];
    private readonly Dictionary<Hash256,RenderTextureResource>TextureResidency=[];
    private readonly Dictionary<AssetKey,MaterialRegistration>Materials=[];

    public int TextureAssetCount=>this.Textures.Count;
    public int ResidentTextureCount=>this.TextureResidency.Count;
    public int MaterialCount=>this.Materials.Count;

    public RenderTextureResource RegisterTexture(AssetKey Key,ReadOnlySpan<byte>Envelope){
        if(!Key.IsValid){ throw new ArgumentException("A valid texture AssetKey is required.",nameof(Key)); }
        byte[]CanonicalEnvelope=Envelope.ToArray();
        if(!ContentArtifactCodec.TryRead(
            CanonicalEnvelope,out ContentArtifact?Artifact,out ContentArtifactError Error)
            ||Artifact is null){
            throw new InvalidDataException($"Texture envelope is invalid: {Error.Field}: {Error.Message}");
        }
        if(!RenderArtifactCodec.TryReadTexture(
            Artifact,out int Width,out int Height,out byte[]Pixels,out string PayloadError)){
            throw new InvalidDataException(PayloadError);
        }
        Hash256 Address=ContentArtifactCodec.ComputeAddress(CanonicalEnvelope);
        if(this.Textures.TryGetValue(Key,out TextureRegistration?Existing)){
            if(Existing.Address!=Address){
                throw new InvalidOperationException($"Texture AssetKey is already bound to another artifact: {Key}.");
            }
            return Existing.Resource;
        }
        if(!this.TextureResidency.TryGetValue(Artifact.ContentHash,out RenderTextureResource?Resource)){
            Resource=new RenderTextureResource(Artifact.ContentHash,Width,Height,Pixels,Artifact.Payload.Length>=4&&BinaryPrimitives.ReadUInt32LittleEndian(Artifact.Payload.AsSpan())==0x31544743?ImageArtifactCodec.ReadTexture(Artifact.Payload.AsSpan()):null);
            this.TextureResidency.Add(Artifact.ContentHash,Resource);
        }
        this.Textures.Add(Key,new TextureRegistration(Address,Resource));
        return Resource;
    }

    public RenderMaterialResource RegisterMaterial(AssetKey Key,ReadOnlySpan<byte>Envelope){
        if(!Key.IsValid){ throw new ArgumentException("A valid material AssetKey is required.",nameof(Key)); }
        byte[]CanonicalEnvelope=Envelope.ToArray();
        if(!ContentArtifactCodec.TryRead(
            CanonicalEnvelope,out ContentArtifact?Artifact,out ContentArtifactError Error)
            ||Artifact is null){
            throw new InvalidDataException($"Material envelope is invalid: {Error.Field}: {Error.Message}");
        }
        if(!RenderArtifactCodec.TryReadMaterial(
            Artifact,out RenderMaterialArtifactDefinition Definition,out string PayloadError)){
            throw new InvalidDataException(PayloadError);
        }
        Hash256 Address=ContentArtifactCodec.ComputeAddress(CanonicalEnvelope);
        if(this.Materials.TryGetValue(Key,out MaterialRegistration?Existing)){
            if(Existing.Address!=Address){
                throw new InvalidOperationException($"Material AssetKey is already bound to another artifact: {Key}.");
            }
            return Existing.Resource;
        }
        RenderTextureResource BaseColor=Resolve(Definition.BaseColorTexture);
        RenderTextureResource Normal=Resolve(Definition.NormalTexture);
        RenderTextureResource MetallicRoughness=Resolve(Definition.MetallicRoughnessTexture);
        RenderTextureResource Emissive=Resolve(Definition.EmissiveTexture);
        RenderMaterialResource Resource=new(
            Address,Definition,BaseColor,Normal,MetallicRoughness,Emissive);
        this.Materials.Add(Key,new MaterialRegistration(Address,Resource));
        return Resource;
    }

    public bool TryGetMaterial(AssetKey Key,out RenderMaterialResource?Material){
        if(this.Materials.TryGetValue(Key,out MaterialRegistration?Registration)){
            Material=Registration.Resource;
            return true;
        }
        Material=null;
        return false;
    }

    private RenderTextureResource Resolve(AssetArtifactReference Reference){
        if(!this.Textures.TryGetValue(Reference.Key,out TextureRegistration?Registration)){
            throw new InvalidDataException($"Material texture dependency is not resident: {Reference.Key}.");
        }
        if(Registration.Resource.ContentHash!=Reference.ContentHash){
            throw new InvalidDataException($"Material texture dependency hash does not match: {Reference.Key}.");
        }
        return Registration.Resource;
    }

    private sealed record TextureRegistration(Hash256 Address,RenderTextureResource Resource);
    private sealed record MaterialRegistration(Hash256 Address,RenderMaterialResource Resource);
}
