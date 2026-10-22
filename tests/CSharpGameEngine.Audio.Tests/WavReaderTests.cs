namespace CSharpGameEngine.Audio.Tests;

/// <summary>ADR-0077 stage 5: the RIFF/WAVE container reader behind <c>Audio.Play</c>.</summary>
public sealed class WavReaderTests {
    private static byte[] Wav(int Format,int Channels,int SampleRate,int Bits,byte[] Data,bool ExtraChunk=false){
        using var Stream=new MemoryStream();
        using var Writer=new BinaryWriter(Stream);
        Writer.Write("RIFF"u8);
        Writer.Write(0);
        Writer.Write("WAVE"u8);
        if(ExtraChunk){
            Writer.Write("LIST"u8);
            Writer.Write(3);
            Writer.Write(new byte[]{1,2,3,0});
        }
        Writer.Write("fmt "u8);
        Writer.Write(16);
        Writer.Write((ushort)Format);
        Writer.Write((ushort)Channels);
        Writer.Write(SampleRate);
        Writer.Write(SampleRate*Channels*Bits/8);
        Writer.Write((ushort)(Channels*Bits/8));
        Writer.Write((ushort)Bits);
        Writer.Write("data"u8);
        Writer.Write(Data.Length);
        Writer.Write(Data);
        return Stream.ToArray();
    }

    [Fact]
    public void Pcm16_IsReadAsIs(){
        var Samples=new short[]{0,1000,-1000,short.MaxValue,short.MinValue,5};
        var Bytes=new byte[Samples.Length*2];
        Buffer.BlockCopy(Samples,0,Bytes,0,Bytes.Length);

        Assert.True(WavReader.TryRead(Wav(1,2,44100,16,Bytes),out var Clip,out var Error),Error);

        Assert.Equal(2,Clip!.Channels);
        Assert.Equal(44100,Clip.SampleRate);
        Assert.Equal(3,Clip.FrameCount);
        Assert.Equal(Samples,Clip.Samples.ToArray());
    }

    [Fact]
    public void Pcm8_Pcm24_AndFloat32_AreConvertedTo16Bit(){
        Assert.True(WavReader.TryRead(Wav(1,1,22050,8,[128,255,0]),out var Eight,out _));
        Assert.Equal(new short[]{0,127<<8,-128<<8},Eight!.Samples.ToArray());

        // 24-bit little endian: 0x400000 is a quarter of full scale.
        Assert.True(WavReader.TryRead(Wav(1,1,22050,24,[0,0,0x40,0,0,0xC0]),out var TwentyFour,out _));
        Assert.Equal(new short[]{0x4000,unchecked((short)0xC000)},TwentyFour!.Samples.ToArray());

        var Floats=new float[]{0f,0.5f,-1f,2f,float.NaN};
        var FloatBytes=new byte[Floats.Length*4];
        Buffer.BlockCopy(Floats,0,FloatBytes,0,FloatBytes.Length);
        Assert.True(WavReader.TryRead(Wav(3,1,22050,32,FloatBytes),out var Float,out _));
        Assert.Equal(new short[]{0,16384,-32767,short.MaxValue,0},Float!.Samples.ToArray());
    }

    [Fact]
    public void AnUnknownChunkBeforeFmt_IsSkipped_AndTheDurationIsFramesOverRate(){
        var Bytes=new byte[48000*2];
        Assert.True(WavReader.TryRead(Wav(1,1,48000,16,Bytes,ExtraChunk:true),out var Clip,out _));

        Assert.Equal(48000,Clip!.FrameCount);
        Assert.Equal(1f,Clip.DurationSeconds);
    }

    [Theory]
    [InlineData(0,"not a RIFF/WAVE")]
    public void NotRiff_IsRejectedWithAReason(int Length,string Reason){
        Assert.False(WavReader.TryRead(new byte[Length],out var Clip,out var Error));
        Assert.Null(Clip);
        Assert.Contains(Reason,Error);
    }

    [Fact]
    public void CompressedFormats_AndUnsupportedLayouts_AreRejected(){
        Assert.False(WavReader.TryRead(Wav(2,1,22050,4,[1,2,3,4]),out _,out var Adpcm));
        Assert.Contains("unsupported format tag 2",Adpcm);
        Assert.False(WavReader.TryRead(Wav(1,6,48000,16,[0,0]),out _,out var Surround));
        Assert.Contains("unsupported channel count 6",Surround);
        Assert.False(WavReader.TryRead(Wav(1,1,100,16,[0,0]),out _,out var Rate));
        Assert.Contains("sample rate 100",Rate);
    }

    [Fact]
    public void ATruncatedDataChunk_ReadsWhatIsThere(){
        var Bytes=Wav(1,1,22050,16,new byte[10]);
        var Cut=Bytes[..(Bytes.Length-3)];

        Assert.True(WavReader.TryRead(Cut,out var Clip,out _));
        Assert.Equal(3,Clip!.FrameCount);
    }
}
