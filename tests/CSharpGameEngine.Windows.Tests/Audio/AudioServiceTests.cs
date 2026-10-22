using CSharpGameEngine.Runtime.Windows;

namespace CSharpGameEngine.Audio.Tests;

/// <summary>ADR-0077 stage 5: <c>Audio.Play</c> collects requests and hands them to the device once per frame.</summary>
public sealed class AudioServiceTests:IDisposable {
    private readonly string Directory=Path.Combine(Path.GetTempPath(),"cge-audio-"+Guid.NewGuid().ToString("N"));

    public AudioServiceTests(){
        System.IO.Directory.CreateDirectory(this.Directory);
    }

    public void Dispose(){
        System.IO.Directory.Delete(this.Directory,true);
    }

    private string WriteTone(string Name){
        var Path=System.IO.Path.Combine(this.Directory,Name);
        var Samples=new short[2205];
        for(var Index=0;Index<Samples.Length;Index++){ Samples[Index]=(short)(8000*MathF.Sin(Index*0.1f)); }
        using var Stream=File.Create(Path);
        using var Writer=new BinaryWriter(Stream);
        Writer.Write("RIFF"u8);
        Writer.Write(36+Samples.Length*2);
        Writer.Write("WAVEfmt "u8);
        Writer.Write(16);
        Writer.Write((ushort)1);
        Writer.Write((ushort)1);
        Writer.Write(22050);
        Writer.Write(44100);
        Writer.Write((ushort)2);
        Writer.Write((ushort)16);
        Writer.Write("data"u8);
        Writer.Write(Samples.Length*2);
        foreach(var Sample in Samples){ Writer.Write(Sample); }
        return Path;
    }

    [Fact]
    public void RequestsWaitForTheFlush_ThenReachTheDeviceOnce(){
        using var Host=new EngineHost();
        var Tone=this.WriteTone("tone.wav");

        // An inaudible volume: a machine with a sound device must not play a test tone.
        Host.Sound.PlayLocal(new CSharpGameEngine.Authoring.SoundAsset(Tone),0.0001f);
        Host.Sound.PlayLocal(new CSharpGameEngine.Authoring.SoundAsset(Tone),0.0001f);
        Assert.Equal(0,Host.EnsureAudioDevice().OneShotsRequested);

        Host.Sound.Flush();

        Assert.Equal(2,Host.EnsureAudioDevice().OneShotsRequested);
        Assert.Equal(2,Host.Sound.FlushedRequests);
        Host.Sound.Flush();
        Assert.Equal(2,Host.EnsureAudioDevice().OneShotsRequested);
    }

    [Fact]
    public void AMissingOrBrokenFile_IsSkippedAndNeverReachesTheDevice(){
        using var Host=new EngineHost();
        var Broken=System.IO.Path.Combine(this.Directory,"broken.wav");
        File.WriteAllBytes(Broken,[1,2,3]);

        Host.Sound.PlayLocal(new CSharpGameEngine.Authoring.SoundAsset(System.IO.Path.Combine(this.Directory,"missing.wav")));
        Host.Sound.PlayLocal(new CSharpGameEngine.Authoring.SoundAsset(Broken));
        Host.Sound.Flush();

        Assert.Equal(0,Host.EnsureAudioDevice().OneShotsRequested);
    }

    [Fact]
    public void MoreRequestsThanTheFrameLimit_AreDropped(){
        using var Host=new EngineHost();
        var Tone=this.WriteTone("tone.wav");

        for(var Index=0;Index<CSharpGameEngine.Runtime.Windows.Audio.MaxRequestsPerFrame+5;Index++){ Host.Sound.PlayLocal(new CSharpGameEngine.Authoring.SoundAsset(Tone),0.0001f); }
        Host.Sound.Flush();

        Assert.Equal(5,Host.Sound.DroppedRequests);
        Assert.Equal(CSharpGameEngine.Runtime.Windows.Audio.MaxRequestsPerFrame,Host.EnsureAudioDevice().OneShotsRequested);
    }
}

/// <summary>The placeholder sounds of the ThirdPerson template (ADR-0077) are valid PCM the game can play.</summary>
public sealed class TemplateSoundTests {
    private static string RepositoryRoot(){
        var Directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(Directory is not null&&!File.Exists(Path.Combine(Directory.FullName,"CSharpGameEngine.slnx"))){ Directory=Directory.Parent; }
        return Directory?.FullName??throw new InvalidOperationException("The repository root was not found.");
    }

    [Theory]
    [InlineData("Jump.wav")]
    [InlineData("Land.wav")]
    [InlineData("Footstep.wav")]
    public void ThePlaceholderSoundIsADecodableShortMonoClip(string Name){
        var Path=System.IO.Path.Combine(RepositoryRoot(),"Templates","CSharpGameEngine.Templates.ThirdPerson","Assets","Sounds",Name);

        Assert.True(WavReader.TryRead(File.ReadAllBytes(Path),out var Clip,out var Error),Error);

        Assert.Equal(1,Clip!.Channels);
        Assert.InRange(Clip.DurationSeconds,0.05f,0.5f);
        Assert.Contains(Clip.Samples.ToArray(),static Sample=>Sample!=0);
    }
}
