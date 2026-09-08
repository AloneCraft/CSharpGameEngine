using System.Text;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Core;

namespace CSharpGameEngine.Content.Tests;

public sealed class ShippingContentManifestTests:IDisposable {
    private static readonly StableId Namespace=StableId.Parse("816335c3-34e9-52e5-8aa5-1850fd4a7184");
    private readonly string Root=Path.Combine(Path.GetTempPath(),"CgeShippingManifestTests",Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task BuilderAndCodec_UseOnlyTheExactReachableClosureDeterministically(){
        var Fixture=await this.CreateGraphAsync();

        ShippingContentManifest First=await ShippingContentManifestBuilder.BuildAsync(
            Fixture.Store,Fixture.Index,[Fixture.Root.Reference]);
        ShippingContentManifest Second=await ShippingContentManifestBuilder.BuildAsync(
            Fixture.Store,Fixture.Index,[Fixture.Root.Reference]);
        byte[]Bytes=ShippingContentManifestCodec.Write(First);

        Assert.Equal(2,First.Entries.Length);
        Assert.DoesNotContain(First.Entries,Entry=>Entry.Reference==Fixture.Unreachable.Reference);
        Assert.Equal(Bytes,ShippingContentManifestCodec.Write(Second));
        Assert.Equal(First.Fingerprint,Second.Fingerprint);
        Assert.True(ShippingContentManifestCodec.TryRead(Bytes,out var Read,out var Error),Error.Message);
        Assert.NotNull(Read);
        Assert.Equal(First.Fingerprint,Read!.Fingerprint);
        Assert.True(First.Roots.SequenceEqual(Read.Roots));
        Assert.Equal(First.Entries.Select(static Entry=>Entry.Reference),Read.Entries.Select(static Entry=>Entry.Reference));
    }

    [Fact]
    public async Task Package_PublishesAndOpensOnlyManifestArtifacts(){
        var Fixture=await this.CreateGraphAsync();
        ShippingContentManifest Manifest=await ShippingContentManifestBuilder.BuildAsync(
            Fixture.Store,Fixture.Index,[Fixture.Root.Reference]);
        string Output=Path.Combine(this.Root,"shipping");

        await ShippingContentPackage.PublishAsync(Fixture.Store,Manifest,Output);
        ShippingContentPackage Opened=await ShippingContentPackage.OpenAsync(Output);

        Assert.Equal(Manifest.Fingerprint,Opened.Manifest.Fingerprint);
        Assert.Equal(2,Directory.EnumerateFiles(Path.Combine(Output,"objects"),"*.cgea",SearchOption.AllDirectories).Count());
        foreach(var Entry in Manifest.Entries){
            ReadOnlyMemory<byte>Bytes=await Opened.Artifacts.ReadAsync(Entry.Address);
            Assert.Equal(Entry.EnvelopeByteLength,Bytes.Length);
        }
    }

    [Fact]
    public async Task Package_RejectsExtraMissingAndCorruptArtifacts(){
        var Fixture=await this.CreateGraphAsync();
        ShippingContentManifest Manifest=await ShippingContentManifestBuilder.BuildAsync(
            Fixture.Store,Fixture.Index,[Fixture.Root.Reference]);
        string Output=Path.Combine(this.Root,"strict-package");
        await ShippingContentPackage.PublishAsync(Fixture.Store,Manifest,Output);

        string Extra=Path.Combine(Output,"objects","extra.cgea");
        await File.WriteAllBytesAsync(Extra,[1]);
        await Assert.ThrowsAsync<InvalidDataException>(async()=>await ShippingContentPackage.OpenAsync(Output));
        File.Delete(Extra);

        var Entry=Manifest.Entries[0];
        string ArtifactPath=ShippingContentPackage.GetArtifactPath(Output,Entry.Address);
        byte[]Original=await File.ReadAllBytesAsync(ArtifactPath);
        File.Delete(ArtifactPath);
        await Assert.ThrowsAsync<InvalidDataException>(async()=>await ShippingContentPackage.OpenAsync(Output));
        Directory.CreateDirectory(Path.GetDirectoryName(ArtifactPath)!);
        await File.WriteAllBytesAsync(ArtifactPath,Original);
        Original[^1]^=0x20;
        await File.WriteAllBytesAsync(ArtifactPath,Original);
        await Assert.ThrowsAsync<InvalidDataException>(async()=>await ShippingContentPackage.OpenAsync(Output));
    }

    [Fact]
    public async Task Package_DeduplicatesOneEnvelopeReferencedByDifferentAssetKeys(){
        var Store=new ContentAddressedStore(Path.Combine(this.Root,"dedup-cas"));
        byte[]Payload=Encoding.UTF8.GetBytes("shared");
        byte[]Envelope=ContentArtifactCodec.Write(new ContentArtifact(
            ContentArtifactKind.Mesh,
            new ArtifactProvenance("tests/1","shared",Hash256.Compute(Payload),"fixture",Hash256.Compute([1])),
            null,Payload));
        PublishedContentArtifact First=await Store.PublishAsync(Key("dedup-a"),Envelope);
        PublishedContentArtifact Second=await Store.PublishAsync(Key("dedup-b"),Envelope);
        Assert.Equal(First.Address,Second.Address);
        var Manifest=await ShippingContentManifestBuilder.BuildAsync(
            Store,new ContentArtifactIndex([First,Second]),[First.Reference,Second.Reference]);
        string Output=Path.Combine(this.Root,"dedup-package");

        await ShippingContentPackage.PublishAsync(Store,Manifest,Output);
        ShippingContentPackage Opened=await ShippingContentPackage.OpenAsync(Output);

        Assert.Equal(2,Opened.Manifest.Entries.Length);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(Output,"objects"),"*.cgea",SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Builder_RejectsMissingDependencyBeforePublishingManifest(){
        var Store=new ContentAddressedStore(Path.Combine(this.Root,"missing-cas"));
        AssetKey Missing=Key("missing");
        var MissingReference=new AssetArtifactReference(Missing,Hash256.Compute(Encoding.UTF8.GetBytes("missing")));
        PublishedContentArtifact RootArtifact=await PublishAsync(Store,Key("root-missing"),"root",[MissingReference]);
        var Index=new ContentArtifactIndex([RootArtifact]);

        await Assert.ThrowsAsync<InvalidDataException>(async()=>
            await ShippingContentManifestBuilder.BuildAsync(Store,Index,[RootArtifact.Reference]));
    }

    [Fact]
    public void Manifest_ValidatesDeepClosureWithoutRecursiveStackAndRejectsCycles(){
        const int Count=4096;
        var References=Enumerable.Range(0,Count).Select(Index=>new AssetArtifactReference(
            Key("deep-"+Index),Hash256.Compute(BitConverter.GetBytes(Index+1)))).ToArray();
        ArtifactProvenance Provenance=new(
            "tests/1","deep",Hash256.Compute([1]),"fixture",Hash256.Compute([2]));
        var Entries=new ShippingContentManifestEntry[Count];
        for(var Index=0;Index<Count;Index++){
            Entries[Index]=new ShippingContentManifestEntry(
                References[Index],Hash256.Compute(BitConverter.GetBytes(Index+Count+1)),
                ContentArtifactKind.Mesh,Provenance,
                Index+1<Count?[References[Index+1]]:[],1);
        }

        var Manifest=new ShippingContentManifest([References[0]],Entries);
        Assert.Equal(Count,Manifest.GetDependencyClosure(References[0]).Length);

        ShippingContentManifestEntry[]Cycle=[
            new ShippingContentManifestEntry(References[0],Entries[0].Address,ContentArtifactKind.Mesh,Provenance,[References[1]],1),
            new ShippingContentManifestEntry(References[1],Entries[1].Address,ContentArtifactKind.Mesh,Provenance,[References[0]],1),
        ];
        Assert.Throws<ArgumentException>(()=>new ShippingContentManifest([References[0]],Cycle));
    }

    public void Dispose(){ if(Directory.Exists(this.Root)){ Directory.Delete(this.Root,true); } }

    private async Task<GraphFixture>CreateGraphAsync(){
        var Store=new ContentAddressedStore(Path.Combine(this.Root,"cas-"+Guid.NewGuid().ToString("N")));
        PublishedContentArtifact Leaf=await PublishAsync(Store,Key("leaf"),"leaf",[]);
        PublishedContentArtifact RootArtifact=await PublishAsync(Store,Key("root"),"root",[Leaf.Reference]);
        PublishedContentArtifact Unreachable=await PublishAsync(Store,Key("unreachable"),"unreachable",[]);
        return new(Store,new ContentArtifactIndex([RootArtifact,Leaf,Unreachable]),RootArtifact,Leaf,Unreachable);
    }

    private static async Task<PublishedContentArtifact>PublishAsync(
        ContentAddressedStore Store,AssetKey Key,string Payload,IEnumerable<AssetArtifactReference>Dependencies){
        byte[]PayloadBytes=Encoding.UTF8.GetBytes(Payload);
        var Artifact=new ContentArtifact(
            ContentArtifactKind.Mesh,
            new ArtifactProvenance("tests/1",Payload,Hash256.Compute(PayloadBytes),"fixture",Hash256.Compute([1])),
            Dependencies,PayloadBytes);
        return await Store.PublishAsync(Key,ContentArtifactCodec.Write(Artifact));
    }

    private static AssetKey Key(string Name)=>new(new AssetId(StableId.FromName(Namespace,Name)),null);
    private sealed record GraphFixture(
        ContentAddressedStore Store,ContentArtifactIndex Index,
        PublishedContentArtifact Root,PublishedContentArtifact Leaf,PublishedContentArtifact Unreachable);
}
