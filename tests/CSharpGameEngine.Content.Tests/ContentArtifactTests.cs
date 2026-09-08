using System.Buffers.Binary;
using System.Text;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Core;

namespace CSharpGameEngine.Content.Tests;

public sealed class ContentArtifactTests {
    private const int ChecksumOffset=56;

    [Fact]
    public void Envelope_IsDeterministicLittleEndianAndRoundTrips(){
        ContentArtifact Artifact=CreateArtifact();

        byte[]First=ContentArtifactCodec.Write(Artifact);
        byte[]Second=ContentArtifactCodec.Write(Artifact);

        Assert.Equal(First,Second);
        Assert.Equal("CGEA",Encoding.ASCII.GetString(First,0,4));
        Assert.Equal((ushort)1,BinaryPrimitives.ReadUInt16LittleEndian(First.AsSpan(4,2)));
        Assert.True(ContentArtifactCodec.TryRead(First,out var Read,out var Error),Error.Message);
        Assert.NotNull(Read);
        Assert.Equal(Artifact.Kind,Read!.Kind);
        Assert.Equal(Artifact.ContentHash,Read.ContentHash);
        Assert.True(Artifact.Payload.AsSpan().SequenceEqual(Read.Payload.AsSpan()));
        Assert.Equal(2,Read.Dependencies.Length);
        Assert.True(Read.Dependencies[0].Key.CompareTo(Read.Dependencies[1].Key)<0);
        Assert.Equal(ContentArtifactCodec.ComputeAddress(First),ContentArtifactCodec.ComputeAddress(Second));
    }

    [Fact]
    public void Envelope_RejectsCorruptionWithoutPublishingAValue(){
        byte[]Bytes=ContentArtifactCodec.Write(CreateArtifact());
        Bytes[^1]^=0x40;

        Assert.False(ContentArtifactCodec.TryRead(Bytes,out var Artifact,out var Error));
        Assert.Null(Artifact);
        Assert.Equal(ContentArtifactErrorCode.InvalidChecksum,Error.Code);
        Assert.Equal("checksum",Error.Field);
    }

    [Fact]
    public void Envelope_ContentHashIsIndependentFromChecksum(){
        byte[]Bytes=ContentArtifactCodec.Write(CreateArtifact());
        Bytes[^1]^=0x20;
        Array.Clear(Bytes,ChecksumOffset,32);
        Hash256.Compute(Bytes).ToByteArray().CopyTo(Bytes,ChecksumOffset);

        Assert.False(ContentArtifactCodec.TryRead(Bytes,out _,out var Error));
        Assert.Equal(ContentArtifactErrorCode.InvalidContentHash,Error.Code);
        Assert.Equal("contentHash",Error.Field);
    }

    [Fact]
    public void Envelope_RejectsUnknownSchemaBeforeReadingBodies(){
        byte[]Bytes=ContentArtifactCodec.Write(CreateArtifact());
        BinaryPrimitives.WriteUInt16LittleEndian(Bytes.AsSpan(4,2),ushort.MaxValue);

        Assert.False(ContentArtifactCodec.TryRead(Bytes,out _,out var Error));
        Assert.Equal(ContentArtifactErrorCode.UnsupportedSchema,Error.Code);
    }

    [Fact]
    public void Artifact_RejectsDuplicateDependencyOwnership(){
        AssetArtifactReference Dependency=CreateDependency("dependency");

        Assert.Throws<ArgumentException>(()=>new ContentArtifact(
            ContentArtifactKind.Mesh,
            CreateProvenance(),
            [Dependency,Dependency],
            [1,2,3]));
    }

    private static ContentArtifact CreateArtifact()=>new(
        ContentArtifactKind.Mesh,
        CreateProvenance(),
        [CreateDependency("z-dependency"),CreateDependency("a-dependency")],
        Encoding.UTF8.GetBytes("canonical mesh payload"));

    private static ArtifactProvenance CreateProvenance()=>new(
        "cge-content/1.0.0",
        "Assets/Models/Ship.glb",
        Hash256.Compute(Encoding.UTF8.GetBytes("source")),
        "SharpGLTF/1.0.6",
        Hash256.Compute(Encoding.UTF8.GetBytes("options")));

    private static AssetArtifactReference CreateDependency(string Name){
        StableId Namespace=StableId.Parse("c851270d-e4f7-5787-8731-60957a47fb58");
        AssetId Asset=new(StableId.FromName(Namespace,Name));
        return new AssetArtifactReference(
            new AssetKey(Asset,null),
            Hash256.Compute(Encoding.UTF8.GetBytes("content:"+Name)));
    }
}
