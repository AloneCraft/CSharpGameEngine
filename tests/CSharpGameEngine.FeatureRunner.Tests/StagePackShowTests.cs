using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using CSharpGameEngine.FeatureStages;

namespace CSharpGameEngine.FeatureRunner.Tests;

/// <summary>
/// stages の Pack の <c>show</c> を本物の実行ファイルとして動かし、WARP の隠し窓が保存した PNG に、シーンの部品が写ることを確かめる。
/// </summary>
public sealed class StagePackShowTests {
    private const int Width=640;
    private const int Height=360;

    [Fact]
    public void ShowDrawsTheTriangleSceneWithoutDeviceErrors(){
        var Image=RunShow("Rendering","rendering.warp-triangle");
        // 三角形の Color は 0xFFFF8040（オレンジ）。光を受けても R が G より、G が B より明るいまま残る。
        var Orange=Image.Count(static Pixel=>Pixel.R>150&&Pixel.R>Pixel.G+40&&Pixel.G>Pixel.B+20);
        Assert.True(Orange>=500,$"The orange triangle covers only {Orange} pixels.");
    }

    [Fact]
    public void ShowDrawsThinRodsAtTheSceneScale(){
        // 軸（形 5）は 0.1×2×0.1 の棒。ビューアが大きさをさらに縮めていた頃は、緑の Y 軸がほぼ写らなかった。
        var Image=RunShow("Vsix","vsix.transform-tools");
        var Green=Image.Count(static Pixel=>Pixel.G>Pixel.R+30&&Pixel.G>Pixel.B+10);
        Assert.True(Green>=200,$"The green Y axis covers only {Green} pixels.");
    }

    private static List<(byte R,byte G,byte B)>RunShow(string Pack,string SceneId){
        var RepositoryRoot=FeatureStageRepositoryLocator.Find();
        var Project=Path.Combine(RepositoryRoot,"stages",$"CSharpGameEngine.FeatureStages.{Pack}",$"CSharpGameEngine.FeatureStages.{Pack}.csproj");
        var Capture=Path.Combine(Path.GetTempPath(),$"cge-show-{Guid.NewGuid():N}.png");
        try {
            var Start=new ProcessStartInfo("dotnet") {
                RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,WorkingDirectory=RepositoryRoot,
            };
            foreach(var Argument in new[]{"run","--project",Project,"-c","Debug","--","show",SceneId,"--hidden","--warp","--frames","3",
                "--width",Width.ToString(),"--height",Height.ToString(),"--capture",Capture}){
                Start.ArgumentList.Add(Argument);
            }
            using var Process=System.Diagnostics.Process.Start(Start)!;
            var Error=Process.StandardError.ReadToEndAsync();
            var Output=Process.StandardOutput.ReadToEnd();
            Assert.True(Process.WaitForExit(TimeSpan.FromMinutes(5)),"show did not finish within five minutes.");
            Assert.True(Process.ExitCode==0,$"show exited with {Process.ExitCode}.{Environment.NewLine}{Output}{Error.Result}");
            Assert.Contains("D3D12 errors: 0",Output);
            return ReadPng(Capture);
        } finally {
            if(File.Exists(Capture)){ File.Delete(Capture); }
        }
    }

    /// <summary>ビューアが書く形の PNG（8 ビット RGBA、フィルター 0、IDAT は zlib）だけを読む。</summary>
    private static List<(byte R,byte G,byte B)>ReadPng(string Path){
        var Bytes=File.ReadAllBytes(Path);
        int ImageWidth=0,ImageHeight=0;
        using var Compressed=new MemoryStream();
        for(var Offset=8;Offset<Bytes.Length;){
            var Length=BinaryPrimitives.ReadInt32BigEndian(Bytes.AsSpan(Offset));
            var Type=System.Text.Encoding.ASCII.GetString(Bytes,Offset+4,4);
            if(Type=="IHDR"){
                ImageWidth=BinaryPrimitives.ReadInt32BigEndian(Bytes.AsSpan(Offset+8));
                ImageHeight=BinaryPrimitives.ReadInt32BigEndian(Bytes.AsSpan(Offset+12));
            }
            if(Type=="IDAT"){ Compressed.Write(Bytes,Offset+8,Length); }
            Offset+=12+Length;
        }
        Assert.Equal((Width,Height),(ImageWidth,ImageHeight));
        Compressed.Position=0;
        using var Zlib=new ZLibStream(Compressed,CompressionMode.Decompress);
        using var Raw=new MemoryStream();
        Zlib.CopyTo(Raw);
        var Data=Raw.ToArray();
        var Pixels=new List<(byte R,byte G,byte B)>(ImageWidth*ImageHeight);
        var Stride=1+ImageWidth*4;
        for(var Y=0;Y<ImageHeight;Y++){
            Assert.Equal(0,Data[Y*Stride]);
            for(var X=0;X<ImageWidth;X++){
                var At=Y*Stride+1+X*4;
                Pixels.Add((Data[At],Data[At+1],Data[At+2]));
            }
        }
        return Pixels;
    }
}
