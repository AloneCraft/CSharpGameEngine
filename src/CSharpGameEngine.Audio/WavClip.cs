namespace CSharpGameEngine.Audio;

/// <summary>Decoded 16-bit PCM of one sound (interleaved). The runtime plays it as is; it owns no codec.</summary>
public sealed class PcmClip {
    private readonly short[]SamplesStorage;

    public PcmClip(short[]Samples,int SampleRate,int Channels){
        ArgumentNullException.ThrowIfNull(Samples);
        if(SampleRate is <8000 or >384000){ throw new ArgumentOutOfRangeException(nameof(SampleRate)); }
        if(Channels is <1 or >2){ throw new ArgumentOutOfRangeException(nameof(Channels)); }
        if(Samples.Length%Channels!=0){ throw new ArgumentException("PCM sample count must contain complete interleaved frames.",nameof(Samples)); }
        this.SamplesStorage=Samples;
        this.SampleRate=SampleRate;
        this.Channels=Channels;
    }

    public ReadOnlySpan<short>Samples=>this.SamplesStorage;
    public int SampleRate { get;}
    public int Channels { get;}
    public int FrameCount=>this.SamplesStorage.Length/this.Channels;
    public float DurationSeconds=>(float)this.FrameCount/this.SampleRate;
}

/// <summary>
/// Reads a RIFF/WAVE container (PCM 8/16/24-bit or 32-bit float, mono or stereo) into <see cref="PcmClip"/>.
/// This is container parsing, not a codec: compressed formats are rejected. ADR-0056 plans a build-time conversion to a
/// stream artifact; until that exists the game's <c>.wav</c> files are read as they are (ADR-0077).
/// </summary>
public static class WavReader {
    public static bool TryRead(ReadOnlySpan<byte>Data,out PcmClip?Clip,out string?Error){
        Clip=null;
        Error=null;
        if(Data.Length<12||!Matches(Data,0,"RIFF")||!Matches(Data,8,"WAVE")){
            Error="not a RIFF/WAVE file";
            return false;
        }

        int Format=0;
        int Channels=0;
        int SampleRate=0;
        int Bits=0;
        var HasFormat=false;
        var Offset=12;
        while(Offset+8<=Data.Length){
            var Size=BitConverter.ToInt32(Data.Slice(Offset+4,4));
            var Body=Offset+8;
            if(Size<0||Body+(long)Size>Data.Length){ Size=Data.Length-Body; }
            if(Matches(Data,Offset,"fmt ")&&Size>=16){
                Format=BitConverter.ToUInt16(Data.Slice(Body,2));
                Channels=BitConverter.ToUInt16(Data.Slice(Body+2,2));
                SampleRate=BitConverter.ToInt32(Data.Slice(Body+4,4));
                Bits=BitConverter.ToUInt16(Data.Slice(Body+14,2));
                if(Format==0xFFFE&&Size>=26){ Format=BitConverter.ToUInt16(Data.Slice(Body+24,2)); }
                HasFormat=true;
            } else if(Matches(Data,Offset,"data")){
                if(!HasFormat){
                    Error="the data chunk comes before the fmt chunk";
                    return false;
                }

                return TryConvert(Data.Slice(Body,Size),Format,Channels,SampleRate,Bits,out Clip,out Error);
            }

            Offset=Body+Size+(Size&1);
        }

        Error="no data chunk";
        return false;
    }

    private static bool TryConvert(ReadOnlySpan<byte>Data,int Format,int Channels,int SampleRate,int Bits,out PcmClip?Clip,out string?Error){
        Clip=null;
        Error=null;
        if(Channels is <1 or >2||SampleRate is <8000 or >384000){
            Error=$"unsupported channel count {Channels} or sample rate {SampleRate}";
            return false;
        }

        var IsPcm=Format==1&&Bits is 8 or 16 or 24;
        var IsFloat=Format==3&&Bits==32;
        if(!IsPcm&&!IsFloat){
            Error=$"unsupported format tag {Format} with {Bits} bits (only PCM 8/16/24-bit and 32-bit float are read)";
            return false;
        }

        var BytesPerSample=Bits/8;
        var Count=Data.Length/BytesPerSample/Channels*Channels;
        var Samples=new short[Count];
        for(var Index=0;Index<Count;Index++){
            var Slice=Data.Slice(Index*BytesPerSample,BytesPerSample);
            Samples[Index]=Bits switch {
                8=>(short)((Slice[0]-128)<<8),
                16=>BitConverter.ToInt16(Slice),
                24=>(short)(((Slice[2]<<24)|(Slice[1]<<16)|(Slice[0]<<8))>>16),
                _=>FromFloat(BitConverter.ToSingle(Slice)),
            };
        }

        Clip=new PcmClip(Samples,SampleRate,Channels);
        return true;
    }

    private static short FromFloat(float Value)=>float.IsFinite(Value)?(short)Math.Clamp((int)MathF.Round(Value*32767f),short.MinValue,short.MaxValue):(short)0;

    private static bool Matches(ReadOnlySpan<byte>Data,int Offset,string Tag){
        if(Offset+4>Data.Length){ return false; }
        for(var Index=0;Index<4;Index++){
            if(Data[Offset+Index]!=(byte)Tag[Index]){ return false; }
        }

        return true;
    }
}
