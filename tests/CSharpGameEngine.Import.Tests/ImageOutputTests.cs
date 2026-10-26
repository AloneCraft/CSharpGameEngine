using System.Text.Json;
using CSharpGameEngine.Content;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Import.Images;
namespace CSharpGameEngine.Import.Tests;
public sealed class ImageOutputTests {
    [Theory]
    [InlineData("wall.png",TextureUsage.Color,false)]
    [InlineData("wall_normal.PNG",TextureUsage.Normal,false)]
    [InlineData("wall_normal_dx.png",TextureUsage.Normal,true)]
    [InlineData("wall_normal_gl.png",TextureUsage.Normal,false)]
    [InlineData("normality.jpg",TextureUsage.Color,false)]
    [InlineData("wall_AO.jpeg",TextureUsage.AmbientOcclusion,false)]
    public void InitialDefaultsUseOnlyDelimitedSuffixes(string Name,TextureUsage Usage,bool Flip){
        var Settings=ImageImportSettings.Initial(Name);Assert.Equal(Usage,Settings.Outputs[0].Usage);Assert.Equal(Flip,Settings.Outputs[0].FlipGreen);
        Assert.Equal(TextureUsage.Mask,ImageImportSettings.Initial(Name,TextureUsage.Mask).Outputs[0].Usage);
    }
    [Fact]
    public void IdentityIgnoresSettingsOrderingDisplayNameAndPixels(){
        var Settings=ImageImportSettings.Initial("wall.png");var Color=Settings.Outputs[0];var Id=Color.GetSubAssetId();
        Color.Name="Other";Color.Mipmaps=false;Settings.Outputs=[new(){Key="image",Name="Image",Kind=ImageOutputKind.Image},Color];
        var Parsed=ImageImportSettings.FromOptions(Settings.ToOptions());Assert.Equal(Id,Parsed.Outputs[1].GetSubAssetId());
        Assert.NotEqual(Id,Parsed.Outputs[0].GetSubAssetId());
        Assert.Throws<ArgumentException>(()=>ImageImportSettings.FromOptions(new Dictionary<string,string>{{"image.defaultOutput","missing"}}));
    }
    [Fact]
    public void PackedMasksDoNotReceiveGammaAndNormalMipsAreUnitVectors(){
        byte[]Pixels=[20,80,160,255,20,80,160,255,20,80,160,255,20,80,160,255];
        var Mra=ImageImportSettings.Initial("wall_mra.png").Outputs[0];
        var Packed=ImageOutputCompiler.BuildTexture(2,2,Pixels,Mra);Assert.False(Packed.Srgb);Assert.Equal(new byte[]{160,80,20,255},Packed.Levels[1].Pixels);
        Mra.InvertRoughness=true;Assert.Equal(175,ImageOutputCompiler.BuildTexture(2,2,Pixels,Mra).Levels[1].Pixels[1]);
        var Mask=ImageOutputCompiler.BuildTexture(2,2,Pixels,ImageOutputSettings.For(TextureUsage.Mask));Assert.Equal(Pixels,Mask.Levels[0].Pixels);
        byte[]Normals=[128,128,255,255,218,128,218,255,128,218,218,255,128,128,255,255];
        var Normal=ImageOutputCompiler.BuildTexture(2,2,Normals,ImageOutputSettings.For(TextureUsage.Normal));
        foreach(var Level in Normal.Levels){
            for(var I=0;I<Level.Pixels.Length;I+=4){
                var X=Level.Pixels[I]/127.5f-1f;var Y=Level.Pixels[I+1]/127.5f-1f;var Z=Level.Pixels[I+2]/127.5f-1f;
                Assert.InRange(X*X+Y*Y+Z*Z,.98f,1.02f);
            }
        }
        Assert.True(ImageOutputCompiler.SuggestNormal(Normals));Assert.Equal(TextureUsage.Color,ImageImportSettings.Initial("unknown.png").Outputs[0].Usage);
    }
    [Fact]
    public async Task SourceAndImageBytesSurviveMultiOutputImportAndFailureKeepsManifest(){
        var Root=Path.Combine(Path.GetTempPath(),"CgeImageTests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Root);
        try {
            var Source=Path.Combine(Root,"wall.png");var Bytes=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aVEcAAAAASUVORK5CYII=");File.WriteAllBytes(Source,Bytes);
            AssetImageResult Run(int Command,ImageImportSettings?Settings=null,string Hash="")=>ImageOutputCompiler.Execute(new(){RequestId=Guid.NewGuid(),Root=Root,SourcePath=Source,ImageCommand=Command,Settings=Settings,ExpectedMetadataHash=Hash});
            var Preview=Run(1);Assert.True(Preview.Success,Preview.Diagnostic);Assert.False(File.Exists(Source+".meta"));
            var Settings=Preview.Settings!;Settings.Outputs=[Settings.Outputs[0],ImageOutputSettings.For(TextureUsage.Normal),new(){Key="image",Name="Image",Kind=ImageOutputKind.Image}];
            var Imported=Run(2,Settings);Assert.True(Imported.Success,Imported.Diagnostic);Assert.Equal(Bytes,File.ReadAllBytes(Source));
            var Manifest=JsonSerializer.Deserialize<ImageOutputManifest>(File.ReadAllBytes(Imported.ManifestPath))!;Assert.Equal(3,Manifest.Outputs.Length);
            var Image=Manifest.Outputs.Single(O=>O.Kind=="Image");var Store=new ContentAddressedStore(Path.GetDirectoryName(Imported.ManifestPath)!);
            var Envelope=await Store.ReadAsync(Hash256.Parse(Image.Address));Assert.True(ContentArtifactCodec.TryRead(Envelope.Span,out var Artifact,out _));
            Assert.Equal(Bytes,ImageArtifactCodec.ReadImage(Artifact!.Payload.AsSpan()).EncodedBytes);
            var Published=Manifest.Outputs.Select(O=>new PublishedContentArtifact(new(AssetUri.Parse(O.Uri).Key,Hash256.Parse(O.ContentHash)),Hash256.Parse(O.Address))).ToArray();
            var Shipping=await ShippingContentManifestBuilder.BuildAsync(Store,new ContentArtifactIndex(Published),Published.Select(P=>P.Reference));
            await using(var Runtime=new RuntimeAssetStore(Store,Shipping,[ImageArtifactCodec.CreateImageRuntimeCodec(),ImageArtifactCodec.CreateTextureRuntimeCodec()])){
                using var Raw=await Runtime.RetainAsync<ImageData>(AssetUri.Parse(Image.Uri).Key);Assert.Equal(Bytes,Raw.Value.EncodedBytes);
                var Color=Manifest.Outputs.Single(O=>O.Key=="color");using var Texture=await Runtime.RetainAsync<TextureOutputData>(AssetUri.Parse(Color.Uri).Key);Assert.True(Texture.Value.Srgb);
                await Assert.ThrowsAsync<InvalidOperationException>(async()=>{using var Wrong=await Runtime.RetainAsync<TextureOutputData>(AssetUri.Parse(Image.Uri).Key);});
            }
            var Current=Run(1);Settings=Current.Settings!;Settings.Outputs[0].Name="Renamed";Settings.Outputs=Settings.Outputs.Reverse().ToArray();
            var Reordered=Run(2,Settings,Current.MetadataHash);Assert.True(Reordered.Success,Reordered.Diagnostic);
            var Updated=JsonSerializer.Deserialize<ImageOutputManifest>(File.ReadAllBytes(Reordered.ManifestPath))!;
            foreach(var Output in Manifest.Outputs){ Assert.Equal(Output.Address,Updated.Outputs.Single(O=>O.Key==Output.Key).Address); }
            var Before=File.ReadAllBytes(Imported.ManifestPath);File.WriteAllText(Source,"broken");Assert.False(Run(3).Success);Assert.Equal(Before,File.ReadAllBytes(Imported.ManifestPath));
            var Inventory=AssetWorkspaceService.Inspect(new(){RequestId=Guid.NewGuid(),Sources=[new(){SourcePath=Source,RelativePath="wall.png",ProjectPath=Path.Combine(Root,"Game.csproj"),ProjectRoot=Root}]});
            Assert.Equal(AssetWorkspaceState.ImportFailed,Assert.Single(Inventory.Items).State);
            File.WriteAllBytes(Source,Bytes);Assert.False(Run(2,Settings,"stale").Success);
            Assert.True(Run(3).Success);Assert.Empty(AssetImportStatus.Read(Root,AssetId.Parse(Manifest.AssetId)));
        }finally { Directory.Delete(Root,true); }
    }
}
