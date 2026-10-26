using System.Text;
using CSharpGameEngine.VSExtension;
namespace CSharpGameEngine.VSExtension.Tests;
/// <summary>Skeletal／Animation エディターの純粋なモデル（再生状態と glTF の判定。ADR-0090）。</summary>
public sealed class AssetSkeletalModelTests {
    private static byte[] Glb(string Json,uint Version=2,uint ChunkType=0x4E4F534A){
        var Body=Encoding.UTF8.GetBytes(Json);
        var Padded=(Body.Length+3)/4*4;
        var Bytes=new List<byte>();
        Bytes.AddRange(BitConverter.GetBytes(0x46546C67u));
        Bytes.AddRange(BitConverter.GetBytes(Version));
        Bytes.AddRange(BitConverter.GetBytes((uint)(20+Padded)));
        Bytes.AddRange(BitConverter.GetBytes((uint)Padded));
        Bytes.AddRange(BitConverter.GetBytes(ChunkType));
        Bytes.AddRange(Body);
        for(var Index=Body.Length;Index<Padded;Index++){ Bytes.Add(0x20); }
        return [..Bytes];
    }
    private static string WriteTemp(string Extension,byte[] Bytes){
        var Path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"CgeProbe-"+Guid.NewGuid().ToString("N")+Extension);
        File.WriteAllBytes(Path,Bytes);
        return Path;
    }
    private static string RepositoryRoot(){
        for(var Directory=new DirectoryInfo(AppContext.BaseDirectory);Directory is not null;Directory=Directory.Parent){
            if(File.Exists(System.IO.Path.Combine(Directory.FullName,"CSharpGameEngine.slnx"))){ return Directory.FullName; }
        }
        throw new InvalidOperationException("Repository root not found.");
    }
    [Fact]
    public void Probe_ReadsSkinsAndAnimationNamesFromTheGlbJsonChunk(){
        var Path=WriteTemp(".glb",Glb("{\"asset\":{\"version\":\"2.0\"},\"skins\":[{\"joints\":[0]}],\"animations\":[{\"name\":\"Run\"},{},{\"name\":\"\"}]}"));
        try {
            var Info=GltfSkeletalProbe.Probe(Path)!;
            Assert.True(Info.IsSkeletalMesh);
            Assert.False(Info.IsAnimationOnly);
            Assert.Equal(["Run","animation1","animation2"],Info.AnimationNames);
        } finally { File.Delete(Path); }
    }
    [Fact]
    public void Probe_ClassifiesAnimationOnlyStaticAndMultiSkinFiles(){
        var Cases=new (string Json,int Skins,bool Skeletal,bool AnimationOnly)[] {
            ("{\"animations\":[{\"name\":\"A\"}]}",0,false,true),
            ("{\"meshes\":[{}]}",0,false,false),
            ("{\"skins\":[{},{}]}",2,false,false),
            ("{\"skins\":\"nope\",\"animations\":5}",0,false,false),
        };
        foreach(var Case in Cases){
            var Path=WriteTemp(".gltf",Encoding.UTF8.GetBytes(Case.Json));
            try {
                var Info=GltfSkeletalProbe.Probe(Path)!;
                Assert.Equal(Case.Skins,Info.SkinCount);
                Assert.Equal(Case.Skeletal,Info.IsSkeletalMesh);
                Assert.Equal(Case.AnimationOnly,Info.IsAnimationOnly);
            } finally { File.Delete(Path); }
        }
    }
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Probe_ReturnsNullForBrokenOrUnsupportedFiles(int Case){
        byte[] Bytes=Case switch {
            1=>Glb("{}",Version:1),
            2=>Glb("{}",ChunkType:0x004E4942),
            3=>[1,2,3],
            4=>Glb("{ not json"),
            _=>Encoding.UTF8.GetBytes("[1,2,3]"),
        };
        var Path=WriteTemp(Case==5?".gltf":".glb",Bytes);
        try { Assert.Null(GltfSkeletalProbe.Probe(Path)); } finally { File.Delete(Path); }
        Assert.Null(GltfSkeletalProbe.Probe(System.IO.Path.Combine(System.IO.Path.GetTempPath(),"does-not-exist-"+Guid.NewGuid().ToString("N")+".glb")));
        Assert.Null(GltfSkeletalProbe.Probe(System.IO.Path.Combine(System.IO.Path.GetTempPath(),"other.fbx")));
    }
    [Fact]
    public void Probe_ClassifiesTheTemplateMannequinAndItsAnimation(){
        var Mannequins=System.IO.Path.Combine(RepositoryRoot(),"Templates","CSharpGameEngine.Templates.FirstPerson","Assets","Characters","Mannequins");
        var Mesh=GltfSkeletalProbe.Probe(System.IO.Path.Combine(Mannequins,"SKM_Manny_Simple.glb"))!;
        Assert.True(Mesh.IsSkeletalMesh);
        var Idle=GltfSkeletalProbe.Probe(System.IO.Path.Combine(Mannequins,"Anims","MM_Idle.glb"))!;
        Assert.True(Idle.IsAnimationOnly);
        Assert.Equal("MM_Idle_0",Assert.Single(Idle.AnimationNames));
    }
    [Fact]
    public void Timeline_StartsStoppedAtZeroAndIgnoresAdvanceWithoutAClip(){
        var Timeline=new AssetAnimationTimeline();
        Assert.False(Timeline.HasClip);
        Timeline.Play();
        Assert.False(Timeline.Playing);
        Assert.False(Timeline.Advance(1f));
        Assert.Equal(0f,Timeline.TimeSeconds);
    }
    [Fact]
    public void Timeline_LoopsAroundTheEndAndStopsAtTheEndWhenNotLooping(){
        var Timeline=new AssetAnimationTimeline();
        Timeline.SetClip(2f);
        Timeline.Play();
        Assert.True(Timeline.Advance(1.5f));
        Assert.Equal(1.5f,Timeline.TimeSeconds,3);
        Assert.True(Timeline.Advance(1f));
        Assert.Equal(0.5f,Timeline.TimeSeconds,3);
        Assert.True(Timeline.Playing);
        Timeline.Loop=false;
        Assert.True(Timeline.Advance(5f));
        Assert.Equal(2f,Timeline.TimeSeconds);
        Assert.False(Timeline.Playing);
        Timeline.Play();
        Assert.Equal(0f,Timeline.TimeSeconds);
        Assert.True(Timeline.Playing);
    }
    [Fact]
    public void Timeline_SpeedScalesTheAdvanceAndIsClamped(){
        var Timeline=new AssetAnimationTimeline();
        Timeline.SetClip(10f);
        Timeline.Speed=2f;
        Timeline.Play();
        Timeline.Advance(1f);
        Assert.Equal(2f,Timeline.TimeSeconds,3);
        Timeline.Speed=1000f;
        Assert.Equal(AssetAnimationTimeline.MaximumSpeed,Timeline.Speed);
        Timeline.Speed=0f;
        Assert.Equal(AssetAnimationTimeline.MinimumSpeed,Timeline.Speed);
        Timeline.Speed=float.NaN;
        Assert.Equal(1f,Timeline.Speed);
    }
    [Fact]
    public void Timeline_SeekClampsAndSetClipResetsPlaybackAndRejectsBadDurations(){
        var Timeline=new AssetAnimationTimeline();
        Timeline.SetClip(3f);
        Timeline.Seek(99f);
        Assert.Equal(3f,Timeline.TimeSeconds);
        Timeline.Seek(-4f);
        Assert.Equal(0f,Timeline.TimeSeconds);
        Timeline.Seek(float.NaN);
        Assert.Equal(0f,Timeline.TimeSeconds);
        Timeline.Play();
        Timeline.SetClip(1f);
        Assert.False(Timeline.Playing);
        Assert.Equal(0f,Timeline.TimeSeconds);
        foreach(var Bad in new[] { float.NaN,float.PositiveInfinity,-1f,0f }){
            Timeline.SetClip(Bad);
            Assert.False(Timeline.HasClip);
        }
    }
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-1f)]
    [InlineData(0f)]
    public void Timeline_IgnoresInvalidElapsedTimes(float Elapsed){
        var Timeline=new AssetAnimationTimeline();
        Timeline.SetClip(2f);
        Timeline.Play();
        Assert.False(Timeline.Advance(Elapsed));
        Assert.Equal(0f,Timeline.TimeSeconds);
    }
    [Fact]
    public void Timeline_ToggleAndFormat(){
        var Timeline=new AssetAnimationTimeline();
        Timeline.SetClip(1.25f);
        Timeline.Toggle();
        Assert.True(Timeline.Playing);
        Timeline.Toggle();
        Assert.False(Timeline.Playing);
        Timeline.Seek(0.5f);
        Assert.Equal("0.50 / 1.25 s",Timeline.Format());
    }
}
