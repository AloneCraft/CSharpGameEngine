using System.Text;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Core;

namespace CSharpGameEngine.Content.Tests;

public sealed class ContentAddressedStoreTests:IDisposable {
    private static readonly StableId Namespace=StableId.Parse("471c7445-ded2-5ed2-a026-8c81a6df332d");
    private readonly string Root=Path.Combine(Path.GetTempPath(),"CgeCasTests",Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PublishIsAtomicIdempotentAndReadableByFullEnvelopeAddress(){
        var Store=new ContentAddressedStore(this.Root);
        var Key=KeyFor("mesh");
        var Bytes=Envelope("source-a","payload");

        var First=await Store.PublishAsync(Key,Bytes);
        var Second=await Store.PublishAsync(Key,Bytes);
        var Read=await Store.ReadAsync(First.Address);

        Assert.Equal(First,Second);
        Assert.True(Bytes.AsSpan().SequenceEqual(Read.Span));
        Assert.Equal(Hash256.Compute(Encoding.UTF8.GetBytes("payload")),First.Reference.ContentHash);
    }

    [Fact]
    public async Task AddressIncludesProvenanceEvenWhenPayloadIdentityIsEqual(){
        var Store=new ContentAddressedStore(this.Root);
        var Key=KeyFor("same-key");

        var First=await Store.PublishAsync(Key,Envelope("source-a","same-payload"));
        var Second=await Store.PublishAsync(Key,Envelope("source-b","same-payload"));

        Assert.Equal(First.Reference,Second.Reference);
        Assert.NotEqual(First.Address,Second.Address);
        Assert.Throws<ArgumentException>(()=>new ContentArtifactIndex([First,Second]));
    }

    [Fact]
    public async Task ReadRejectsBytesThatNoLongerMatchTheirAddress(){
        var Store=new ContentAddressedStore(this.Root);
        var Published=await Store.PublishAsync(KeyFor("corrupt"),Envelope("source","payload"));
        var Text=Published.Address.ToString();
        var PathValue=Path.Combine(this.Root,"objects",Text.Substring(0,2),Text+".cgea");
        var Bytes=await File.ReadAllBytesAsync(PathValue);
        Bytes[^1]^=0x80;
        await File.WriteAllBytesAsync(PathValue,Bytes);

        await Assert.ThrowsAsync<InvalidDataException>(async()=>await Store.ReadAsync(Published.Address));
    }

    public void Dispose(){ if(Directory.Exists(this.Root)){ Directory.Delete(this.Root,true); } }

    private static AssetKey KeyFor(string Name)=>new(new AssetId(StableId.FromName(Namespace,Name)),null);
    private static byte[]Envelope(string Source,string Payload)=>ContentArtifactCodec.Write(new ContentArtifact(
        ContentArtifactKind.Mesh,
        new ArtifactProvenance("test",Source,Hash256.Compute(Encoding.UTF8.GetBytes(Source)),"fixture",Hash256.Compute([1])),
        null,Encoding.UTF8.GetBytes(Payload)));
}
