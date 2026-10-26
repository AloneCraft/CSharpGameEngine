using System.Text.Json;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Cook.Cli;
using CSharpGameEngine.Tools.Imaging;
namespace CSharpGameEngine.Cook.Tests;
public sealed class ImageThumbnailCommandTests {
    [WindowsThumbnailFact]
    public void SourcePixelsRoundTripAndScaleWithoutImportingOrAllocatingAFullManagedImage(){
        var Pixels=Enumerable.Range(0,128*64).SelectMany(_=>new byte[]{31,67,151,255}).ToArray();
        var Png=ThumbnailPng.Encode(128,64,Pixels);
        var Original=ThumbnailImageDecoder.DecodeThumbnail(Png,128);
        Assert.True(Original.Success,Original.Error);Assert.Equal(Pixels,Original.Image!.Pixels);
        var Small=ThumbnailImageDecoder.DecodeThumbnail(Png,16);
        Assert.True(Small.Success,Small.Error);Assert.Equal(16,Small.Image!.Width);Assert.Equal(8,Small.Image.Height);
        Assert.Equal(16*8*4,Small.Image.Pixels.Length);Assert.Equal(new byte[]{31,67,151,255},Small.Image.Pixels.Take(4));
    }
    [WindowsThumbnailFact]
    public void LargerPreviewEdgeKeepsDetailAndNeverUpscales(){
        var Pixels=Enumerable.Range(0,1500*750).SelectMany(static Index=>new byte[]{(byte)(Index%251),(byte)(Index/1500%251),9,255}).ToArray();
        var Png=ThumbnailPng.Encode(1500,750,Pixels);
        var Thumbnail=ThumbnailImageDecoder.DecodeThumbnail(Png,128);
        var Preview=ThumbnailImageDecoder.DecodeThumbnail(Png,1024);
        var Full=ThumbnailImageDecoder.DecodeThumbnail(Png,AssetThumbnailRequest.MaximumImageEdge);
        Assert.True(Thumbnail.Success,Thumbnail.Error);Assert.True(Preview.Success,Preview.Error);Assert.True(Full.Success,Full.Error);
        Assert.Equal(128,Thumbnail.Image!.Width);Assert.Equal(1024,Preview.Image!.Width);Assert.Equal(512,Preview.Image.Height);
        Assert.Equal(1500,Full.Image!.Width);Assert.Equal(750,Full.Image.Height);Assert.Equal(Pixels,Full.Image.Pixels);
    }
    [WindowsThumbnailFact]
    public void CommandHonorsTheRequestedPreviewEdge(){
        var Root=Path.Combine(Path.GetTempPath(),"CgeThumbnailTest",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Root);
        try {
            var Source=Path.Combine(Root,"source.png");var Request=Path.Combine(Root,"request.json");var Output=Path.Combine(Root,"preview.png");
            File.WriteAllBytes(Source,ThumbnailPng.Encode(600,300,Enumerable.Repeat((byte)200,600*300*4).ToArray()));
            File.WriteAllText(Request,JsonSerializer.Serialize(new AssetThumbnailRequest{ImagePath=Source,MaxEdge=256}));
            ImageThumbnailCommand.Run(Request,Output);
            var Decoded=ThumbnailImageDecoder.DecodeThumbnail(File.ReadAllBytes(Output),AssetThumbnailRequest.MaximumImageEdge);
            Assert.True(Decoded.Success,Decoded.Error);Assert.Equal(256,Decoded.Image!.Width);Assert.Equal(128,Decoded.Image.Height);
            File.WriteAllText(Request,JsonSerializer.Serialize(new AssetThumbnailRequest{ImagePath=Source,MaxEdge=AssetThumbnailRequest.MaximumImageEdge+1}));
            Assert.Throws<InvalidDataException>(()=>ImageThumbnailCommand.Run(Request,Output));
        }finally { Directory.Delete(Root,true); }
    }
    [WindowsThumbnailFact]
    public void ReadOnlyThumbnailFailureRetainsPreviousPngAndNeverWritesMetadata(){
        var Root=Path.Combine(Path.GetTempPath(),"CgeThumbnailTest",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Root);
        try {
            var Source=Path.Combine(Root,"source.png");var Request=Path.Combine(Root,"request.json");var Output=Path.Combine(Root,"thumbnail.png");
            var Bytes=ThumbnailPng.Encode(1,1,[17,43,97,255]);File.WriteAllBytes(Source,Bytes);
            File.WriteAllText(Request,JsonSerializer.Serialize(new AssetThumbnailRequest{ImagePath=Source}));
            ImageThumbnailCommand.Run(Request,Output);var Previous=File.ReadAllBytes(Output);
            Assert.Equal(Bytes,File.ReadAllBytes(Source));Assert.False(File.Exists(Source+".meta"));
            var Decoded=ThumbnailImageDecoder.DecodeThumbnail(Previous,128);Assert.True(Decoded.Success,Decoded.Error);Assert.Equal(new byte[]{17,43,97,255},Decoded.Image!.Pixels);
            File.WriteAllBytes(Source,[0,1,2,3]);Assert.Throws<InvalidDataException>(()=>ImageThumbnailCommand.Run(Request,Output));
            Assert.Equal(Previous,File.ReadAllBytes(Output));Assert.Empty(Directory.EnumerateFiles(Root,"*.tmp"));
        }finally { Directory.Delete(Root,true); }
    }
    [Theory]
    [InlineData(0)]
    [InlineData(2049)]
    public void InvalidDecodeBoundsFailBeforeNativeWork(int Edge)=>Assert.Throws<ArgumentOutOfRangeException>(()=>ThumbnailImageDecoder.DecodeThumbnail([1],Edge));
}
internal sealed class WindowsThumbnailFactAttribute:FactAttribute {
    public WindowsThumbnailFactAttribute(){ if(!OperatingSystem.IsWindows()){ this.Skip="Requires Windows WIC; no GPU is used."; } }
}