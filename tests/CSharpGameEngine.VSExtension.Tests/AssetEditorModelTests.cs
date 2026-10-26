using CSharpGameEngine.VSExtension;
namespace CSharpGameEngine.VSExtension.Tests;
/// <summary>Texture／Sound エディターの純粋な計算（ヘッダー、チャンネル、ズーム、波形）。</summary>
public sealed class AssetEditorModelTests {
    private static byte[] Png(int Width,int Height){
        var Header=new byte[33];
        new byte[]{ 0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A }.CopyTo(Header,0);
        Header[11]=13;
        Header[12]=(byte)'I';Header[13]=(byte)'H';Header[14]=(byte)'D';Header[15]=(byte)'R';
        Header[16]=(byte)(Width>>24);Header[17]=(byte)(Width>>16);Header[18]=(byte)(Width>>8);Header[19]=(byte)Width;
        Header[20]=(byte)(Height>>24);Header[21]=(byte)(Height>>16);Header[22]=(byte)(Height>>8);Header[23]=(byte)Height;
        return Header;
    }
    private static byte[] Jpeg(int Width,int Height,byte FrameMarker=0xC0){
        var Bytes=new List<byte>{ 0xFF,0xD8 };
        // APPn セグメント（EXIF 等）を先に置き、SOF までセグメント長で読み飛ばせることを確かめる。
        Bytes.AddRange([0xFF,0xE1,0x00,0x0A,1,2,3,4,5,6,7,8]);
        Bytes.AddRange([0xFF,0xC4,0x00,0x04,0,0]);
        Bytes.AddRange([0xFF,FrameMarker,0x00,0x11,0x08,(byte)(Height>>8),(byte)Height,(byte)(Width>>8),(byte)Width,3]);
        return [..Bytes];
    }
    [Fact]
    public void Header_ReadsPngDimensions(){
        Assert.True(AssetImageHeader.TryRead(Png(4096,2048),out var Width,out var Height));
        Assert.Equal(4096,Width);
        Assert.Equal(2048,Height);
    }
    [Theory]
    [InlineData(0xC0)]
    [InlineData(0xC2)]
    public void Header_ReadsJpegDimensionsAfterSkippingOtherSegments(byte Marker){
        Assert.True(AssetImageHeader.TryRead(Jpeg(1920,1080,Marker),out var Width,out var Height));
        Assert.Equal(1920,Width);
        Assert.Equal(1080,Height);
    }
    [Fact]
    public void Header_RejectsTruncatedUnknownAndZeroSizedData(){
        Assert.False(AssetImageHeader.TryRead([],out _,out _));
        Assert.False(AssetImageHeader.TryRead(new byte[]{ 1,2,3,4,5 },out _,out _));
        Assert.False(AssetImageHeader.TryRead(Png(0,10),out _,out _));
        Assert.False(AssetImageHeader.TryRead(Png(10,0),out _,out _));
        Assert.False(AssetImageHeader.TryRead(new byte[]{ 0xFF,0xD8,0xFF,0xE1,0x00 },out _,out _));
        // SOS（スキャン開始）まで SOF が見つからない JPEG は寸法なし。
        Assert.False(AssetImageHeader.TryRead(new byte[]{ 0xFF,0xD8,0xFF,0xDA,0x00,0x04,0,0 },out _,out _));
        Assert.False(AssetImageHeader.TryRead(Png(5,5).Take(20).ToArray(),out _,out _));
    }
    [Fact]
    public void Header_FileReadIsBoundedAndTolerantOfMissingFiles(){
        var Path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"CgeHeader"+Guid.NewGuid().ToString("N")+".png");
        File.WriteAllBytes(Path,Png(640,480));
        try {
            Assert.True(AssetImageHeader.TryReadFile(Path,out var Width,out var Height));
            Assert.Equal((640,480),(Width,Height));
        } finally { File.Delete(Path); }
        Assert.False(AssetImageHeader.TryReadFile(Path,out _,out _));
    }
    private static readonly byte[]Pixels=[ 10,20,30,40, 50,60,70,255 ];
    [Fact]
    public void Channels_ColorKeepsTheBytesAndOpaqueDropsOnlyTheAlpha(){
        Assert.Equal(Pixels,AssetImageChannels.Extract(Pixels,AssetImageChannelMode.Color));
        Assert.Equal(new byte[]{ 10,20,30,255, 50,60,70,255 },AssetImageChannels.Extract(Pixels,AssetImageChannelMode.ColorOpaque));
    }
    [Theory]
    [InlineData((int)AssetImageChannelMode.Red,30,70)]
    [InlineData((int)AssetImageChannelMode.Green,20,60)]
    [InlineData((int)AssetImageChannelMode.Blue,10,50)]
    [InlineData((int)AssetImageChannelMode.Alpha,40,255)]
    public void Channels_SingleChannelModesShowTheChannelAsOpaqueGray(int Mode,byte First,byte Second){
        var Shown=AssetImageChannels.Extract(Pixels,(AssetImageChannelMode)Mode);
        Assert.Equal(new byte[]{ First,First,First,255,Second,Second,Second,255 },Shown);
    }
    [Fact]
    public void Channels_DoNotModifyTheSourceAndRejectBadInput(){
        var Copy=(byte[])Pixels.Clone();
        _=AssetImageChannels.Extract(Copy,AssetImageChannelMode.Red);
        Assert.Equal(Pixels,Copy);
        Assert.Throws<ArgumentNullException>(()=>AssetImageChannels.Extract(null!,AssetImageChannelMode.Color));
        Assert.Throws<ArgumentException>(()=>AssetImageChannels.Extract(new byte[3],AssetImageChannelMode.Color));
        Assert.Throws<ArgumentOutOfRangeException>(()=>AssetImageChannels.Extract(Pixels,(AssetImageChannelMode)99));
        Assert.Empty(AssetImageChannels.Extract([],AssetImageChannelMode.Alpha));
    }
    [Fact]
    public void Zoom_FitScalesTheWholeImageIntoTheViewport(){
        Assert.Equal(0.5f,AssetImageZoom.Fit(500f,500f,1000,800),3);
        Assert.Equal(0.25f,AssetImageZoom.Fit(2000f,500f,1000,2000),3);
        Assert.Equal(2f,AssetImageZoom.Fit(200f,200f,100,100),3);
        Assert.Equal(1f,AssetImageZoom.Fit(0f,100f,100,100));
        Assert.Equal(1f,AssetImageZoom.Fit(100f,100f,0,100));
        Assert.Equal(AssetImageZoom.MinimumZoom,AssetImageZoom.Fit(1f,1f,100000,100000));
        Assert.Equal(AssetImageZoom.MaximumZoom,AssetImageZoom.Fit(100000f,100000f,1,1));
    }
    [Fact]
    public void Zoom_StepIsMultiplicativeAndClamped(){
        Assert.True(AssetImageZoom.Step(1f,1)>1f);
        Assert.True(AssetImageZoom.Step(1f,-1)<1f);
        Assert.Equal(1f,AssetImageZoom.Step(AssetImageZoom.Step(1f,3),-3),3);
        Assert.Equal(AssetImageZoom.MaximumZoom,AssetImageZoom.Step(1f,1000));
        Assert.Equal(AssetImageZoom.MinimumZoom,AssetImageZoom.Step(1f,-1000));
    }
    [Fact]
    public void Zoom_NearestNeighborOnlyWhenMagnified(){
        Assert.False(AssetImageZoom.UseNearestNeighbor(1f));
        Assert.False(AssetImageZoom.UseNearestNeighbor(1.99f));
        Assert.True(AssetImageZoom.UseNearestNeighbor(2f));
    }
    [Fact]
    public void Zoom_DisplayScaleCorrectsForAReducedPreview(){
        // 元画像が 4096px、派生プレビューが 2048px のとき、100% 表示はプレビューを 2 倍に引き伸ばす。
        Assert.Equal(2f,AssetImageZoom.DisplayScale(1f,4096,2048),3);
        Assert.Equal(1f,AssetImageZoom.DisplayScale(1f,2048,2048),3);
        Assert.Equal(0.75f,AssetImageZoom.DisplayScale(0.75f,0,2048),3);
        Assert.Equal(0.75f,AssetImageZoom.DisplayScale(0.75f,100,0),3);
    }
    [Fact]
    public void Waveform_ResampleTakesTheMaximumWhenReducing(){
        var Columns=AssetWaveform.Resample([0.1f,0.9f,0.2f,0.3f,0.5f,0.4f],3);
        Assert.Equal(new[]{ 0.9f,0.3f,0.5f },Columns);
    }
    [Fact]
    public void Waveform_ResampleRepeatsPeaksWhenExpandingAndHandlesEmptyInput(){
        Assert.Equal(new[]{ 0.2f,0.2f,0.8f,0.8f },AssetWaveform.Resample([0.2f,0.8f],4));
        Assert.Empty(AssetWaveform.Resample([],10));
        Assert.Empty(AssetWaveform.Resample([0.5f],0));
        Assert.Empty(AssetWaveform.Resample([0.5f],-1));
        Assert.Throws<ArgumentNullException>(()=>AssetWaveform.Resample(null!,4));
        Assert.Equal(7,AssetWaveform.Resample([0.5f,0.6f,0.7f],7).Length);
    }
    [Fact]
    public void Waveform_FractionAndSeekAreClamped(){
        Assert.Equal(0.5f,AssetWaveform.Fraction(1f,2f));
        Assert.Equal(1f,AssetWaveform.Fraction(5f,2f));
        Assert.Equal(0f,AssetWaveform.Fraction(-1f,2f));
        Assert.Equal(0f,AssetWaveform.Fraction(1f,0f));
        Assert.Equal(1.5f,AssetWaveform.SeekSeconds(25f,100f,6f),3);
        Assert.Equal(6f,AssetWaveform.SeekSeconds(500f,100f,6f),3);
        Assert.Equal(0f,AssetWaveform.SeekSeconds(10f,0f,6f));
    }
    [Theory]
    [InlineData(0f,"0:00.000")]
    [InlineData(1.5f,"0:01.500")]
    [InlineData(61.25f,"1:01.250")]
    [InlineData(-3f,"0:00.000")]
    [InlineData(float.NaN,"0:00.000")]
    [InlineData(float.PositiveInfinity,"0:00.000")]
    public void Waveform_FormatsTime(float Seconds,string Expected)=>Assert.Equal(Expected,AssetWaveform.FormatTime(Seconds));
}
